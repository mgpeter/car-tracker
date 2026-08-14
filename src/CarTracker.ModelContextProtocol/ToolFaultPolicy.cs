using Npgsql;

namespace CarTracker.ModelContextProtocol;

/// <summary>
/// What to do when a tool call hits a blocked or slow database, and what to say about it — shared by every
/// surface that invokes a tool.
/// </summary>
/// <remarks>
/// <para>
/// This was the body of <c>McpDatabaseFaultFilter</c>, which is wired onto the <b>MCP server pipeline</b>. That
/// placement is invisible until a second surface invokes the same tools in-process: the in-app chat would have
/// skipped it entirely and shown the SDK's opaque "An error occurred" for a fault this codebase already knows
/// how to explain. Lifting the policy out is what stops "one tool catalogue" from quietly meaning "one
/// catalogue, two behaviours".
/// </para>
/// <para>
/// The <b>audit</b> half of that pipeline is deliberately not lifted. <c>AssistantWriteAudit</c> is keyed to an
/// <c>AssistantToken</c> and a chat write has none — the human who confirmed it is the record, and the row's own
/// <c>EntrySource.Chat</c> is the attribution. See the spec's Out of Scope.
/// </para>
/// </remarks>
public static class ToolFaultPolicy
{
    /// <summary>Postgres raises this when <c>lock_timeout</c> expires: another session holds the table lock.</summary>
    private const string LockNotAvailable = "55P03";

    /// <summary>…and this when <c>statement_timeout</c> expires, or the command timeout cancels the query.</summary>
    private const string QueryCanceled = "57014";

    /// <summary>A value longer than its column. The commonest way an assistant's prose meets a schema.</summary>
    private const string StringTooLong = "22001";

    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";
    private const string ForeignKeyViolation = "23503";
    private const string NotNullViolation = "23502";
    private const string NumericOverflow = "22003";

    /// <summary>
    /// The ceiling on a whole tool call, and the belt to the connection timeouts' braces.
    ///
    /// <c>lock_timeout</c> alone is not enough, because Aspire's <c>EnrichNpgsqlDbContext</c> installs a retrying
    /// execution strategy and Npgsql classifies a lock timeout (55P03) as *transient* — so EF dutifully retries it
    /// with backoff, turning a 5s database-level failure into a ~90s one. That is worse than the original hang: it
    /// still outlives the caller's patience, and now burns the database doing it. Bounding the call here cuts the
    /// retry loop whatever the underlying fault, so the assistant always gets an answer rather than a timeout.
    ///
    /// Well under a typical 60s client timeout, and far above the ~0.5s these tools actually take.
    /// </summary>
    public static TimeSpan CallBudget { get; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// True when <paramref name="ex"/> is this policy's own budget expiring rather than a genuine client
    /// cancellation — which must propagate untouched.
    /// </summary>
    /// <remarks>
    /// Tested BEFORE the Postgres fault, and deliberately. Cancelling the call makes Npgsql cancel the in-flight
    /// statement, so the server answers 57014 "canceling statement due to user request" — the fault we ourselves
    /// caused masks the one we were waiting on. Reporting that verbatim would tell the reader their query was
    /// slow, when what actually happened is that it never got to start.
    /// </remarks>
    public static bool IsBudgetExpiry(Exception ex, CancellationTokenSource budget, CancellationToken client)
    {
        if (!budget.IsCancellationRequested || client.IsCancellationRequested) return false;

        // The cancellation surfaces either as OperationCanceledException or, once Npgsql has round-tripped the
        // cancel request to the server, as a PostgresException carrying 57014.
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is OperationCanceledException) return true;
            if (e is PostgresException { SqlState: QueryCanceled }) return true;
        }

        return false;
    }

    /// <summary>The sentence to give the model in place of the fault.</summary>
    public static string Explain(string tool, string? sqlState, string? detail) => sqlState switch
    {
        LockNotAvailable =>
            $"'{tool}' could not run: another database session is holding a lock on the table it needs, so the "
            + "read/write could not start. This is a database-level block, not a problem with the arguments or the "
            + "data — tools that touch other tables will keep working normally, which is why only some tools fail. "
            + "It usually means an interrupted migration or a SQL client left open mid-transaction. Retrying will "
            + "not help until that session ends.",

        QueryCanceled =>
            $"'{tool}' was stopped after running too long ({detail}). If this repeats, the database is overloaded "
            + "or blocked by another session.",

        _ =>
            $"'{tool}' was stopped after {CallBudget.TotalSeconds:N0} seconds without answering — most likely the "
            + "database is blocked by another session holding a lock on the table it needs. Tools that touch other "
            + "tables should still work. Retrying is unlikely to help until that session ends.",
    };

    /// <summary>
    /// Walks the exception chain: EF wraps provider exceptions, and the retrying execution strategy wraps again
    /// once it gives up, so the <see cref="PostgresException"/> is rarely the outermost one.
    /// </summary>
    public static PostgresException? FindPostgresFault(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is PostgresException { SqlState: LockNotAvailable or QueryCanceled } postgres)
                return postgres;
        }

        return null;
    }

    /// <summary>
    /// The other half: a fault about the <b>value</b> rather than the database's availability.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept apart from <see cref="FindPostgresFault"/> because the two need opposite advice. A lock timeout says
    /// "retrying will not help"; a value fault says "change this and try again", and the caller usually can.
    /// </para>
    /// <para>
    /// This exists because the assistant met it on its first real day: <c>set_fluids</c> was given "OAT red/pink
    /// (e.g. Havoline XLC) — never mix with blue/green IAT, ~7 L" for a <c>varchar(60)</c> column, and what came
    /// back was EF's "An error occurred while saving the entity changes. See the inner exception for details."
    /// The model could not act on that, and neither could the owner, who was shown it verbatim.
    /// </para>
    /// </remarks>
    public static PostgresException? FindDataFault(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is PostgresException
                {
                    SqlState: StringTooLong or UniqueViolation or CheckViolation
                        or ForeignKeyViolation or NotNullViolation or NumericOverflow,
                } postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    /// <summary>
    /// What to say about a value the database refused — enough for the caller to fix it and try again.
    /// </summary>
    /// <remarks>
    /// The Postgres message is quoted rather than paraphrased. For 22001 it carries the limit ("value too long
    /// for type character varying(60)") and for the constraint violations it names the constraint, which is the
    /// part that identifies the field. Rewriting either into house prose would drop exactly that.
    /// </remarks>
    public static string ExplainData(string tool, PostgresException fault) => fault.SqlState switch
    {
        StringTooLong =>
            $"'{tool}' was refused: {fault.MessageText}. One of the values is longer than the column that holds "
            + "it. Shorten it — a spec field is a label, not a note — and call the tool again.",

        NumericOverflow =>
            $"'{tool}' was refused: {fault.MessageText}. A number is out of range for its column.",

        UniqueViolation =>
            $"'{tool}' was refused: that record already exists ({fault.ConstraintName}). Read the existing one "
            + "and update it rather than adding a second.",

        CheckViolation =>
            $"'{tool}' was refused by the constraint '{fault.ConstraintName}': the combination of values is not "
            + "one the schema allows. Check the arguments against what the tool's description says it accepts.",

        ForeignKeyViolation =>
            $"'{tool}' was refused: it points at a record that does not exist ({fault.ConstraintName}).",

        NotNullViolation =>
            $"'{tool}' was refused: '{fault.ColumnName}' is required and was not supplied.",

        _ => $"'{tool}' was refused by the database: {fault.MessageText}.",
    };
}
