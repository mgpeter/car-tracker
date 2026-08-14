using CarTracker.ModelContextProtocol;
using Npgsql;

namespace CarTracker.Chat.Tests;

/// <summary>
/// A refusal the caller can act on, rather than the one EF gives.
/// </summary>
/// <remarks>
/// Found on the assistant's first real day: `set_fluids` was given "OAT red/pink (e.g. Havoline XLC) — never
/// mix with blue/green IAT, ~7 L" for a `varchar(60)` column, and what came back — to the model *and* to the
/// owner, verbatim in the panel — was "An error occurred while saving the entity changes. See the inner
/// exception for details." Neither of them could do anything with that. The model's own guess, that an em dash
/// was to blame, is what a caller does when told nothing.
/// </remarks>
public sealed class ToolFaultPolicyTests
{
    /// <summary>A Postgres fault as EF delivers it: wrapped, and never the outermost exception.</summary>
    private static Exception Wrapped(string sqlState, string message, string? constraint = null)
    {
        var postgres = new PostgresException(message, "ERROR", "ERROR", sqlState);
        if (constraint is not null) postgres.Data["ConstraintName"] = constraint;

        return new InvalidOperationException(
            "An error occurred while saving the entity changes. See the inner exception for details.",
            postgres);
    }

    [Fact]
    public void A_value_too_long_says_so_and_says_to_shorten_it()
    {
        var fault = ToolFaultPolicy.FindDataFault(
            Wrapped("22001", "value too long for type character varying(60)"));

        Assert.NotNull(fault);

        var said = ToolFaultPolicy.ExplainData("set_fluids", fault!);

        // The limit is quoted rather than paraphrased: it is the part that tells the caller how much to cut.
        Assert.Contains("character varying(60)", said);
        Assert.Contains("Shorten", said);
        Assert.Contains("set_fluids", said);
    }

    [Fact]
    public void A_duplicate_says_to_update_the_existing_row()
    {
        var fault = ToolFaultPolicy.FindDataFault(Wrapped("23505", "duplicate key value violates unique constraint"));

        Assert.Contains("already exists", ToolFaultPolicy.ExplainData("add_vehicle", fault!));
    }

    [Fact]
    public void An_availability_fault_is_not_a_value_fault()
    {
        // The two need opposite advice — "retrying will not help" against "change this and try again" — so a
        // lock timeout must not fall into the value branch, and vice versa.
        var locked = Wrapped("55P03", "canceling statement due to lock timeout");

        Assert.Null(ToolFaultPolicy.FindDataFault(locked));
        Assert.NotNull(ToolFaultPolicy.FindPostgresFault(locked));

        var tooLong = Wrapped("22001", "value too long for type character varying(60)");

        Assert.Null(ToolFaultPolicy.FindPostgresFault(tooLong));
        Assert.NotNull(ToolFaultPolicy.FindDataFault(tooLong));
    }
}
