namespace CarTracker.Domain.Accounts;

/// <summary>What an account is allowed to spend.</summary>
/// <remarks>
/// <para>
/// Two members, because there are two answers to give today and inventing a tier nothing can reach would be a
/// guess dressed as a design. <b>The enum is not stored anywhere</b> - it is derived on every request from the
/// comp list and the account's verified address, so there is no column to fall out of step with the truth and
/// no migration when the way of earning <see cref="Pro"/> changes.
/// </para>
/// <para>
/// When checkout lands, an active subscription becomes the second way to be <see cref="Pro"/> and this enum
/// does not move.
/// </para>
/// </remarks>
public enum AccountPlan
{
    /// <summary>The default, and what every unknown person gets. Bounded on all three costly surfaces.</summary>
    Free = 0,

    /// <summary>Comped today, subscribed later. The assistant, and headroom on the other two.</summary>
    Pro = 1,
}

/// <summary>
/// What one plan may spend, on each of the three surfaces that cost this deployment money or quota.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three numbers in one record rather than three services, because they answer one question</b> - "what is
/// this account entitled to?" - and splitting them is how two of them come to disagree about which plan
/// somebody is on.
/// </para>
/// <para>
/// <b>Per-file size is deliberately absent.</b> <c>DocumentStore.MaxSizeBytes</c> is 25 MB for everybody and
/// stays a deployment constant: the plan varies how many files an account may keep, not how big one may be.
/// A 30 MB scan is a badly-made scan on any plan, and making the limit vary would mean a document that uploads
/// on one account and fails on another with nothing on screen explaining why.
/// </para>
/// </remarks>
/// <param name="ChatEnabled">
/// Whether the assistant is offered at all. Not derivable from <paramref name="DailyChatTokens"/> being zero:
/// zero also means "on, and spent", and the two refusals say different things and carry different statuses.
/// </param>
/// <param name="DailyChatTokens">
/// The daily token ceiling, counted the way <c>ChatUsage.Total</c> counts. Zero on <see cref="AccountPlan.Free"/>.
/// <b>Null means the plan sets no ceiling of its own</b> and the deployment's <c>Chat:DailyTokensPerOwner</c>
/// applies - which is where an operator already configures chat spend, and naming the same ceiling in two
/// sections is how the two come to disagree.
/// </param>
/// <param name="MaxDocuments">
/// How many documents the account may hold in total, across every vehicle. Per account rather than per vehicle,
/// because the volume is what is being bounded and a per-vehicle cap is lifted by adding a car.
/// </param>
/// <param name="DailyVehicleLookups">
/// DVLA registration lookups per day. The one allowance protecting somebody else's quota rather than this
/// deployment's wallet, which is why even the paid tier has a number.
/// </param>
public sealed record PlanAllowances(
    bool ChatEnabled,
    long? DailyChatTokens,
    int MaxDocuments,
    int DailyVehicleLookups);

/// <summary>Everything the plans read from configuration, bound from the <c>Plans:</c> section.</summary>
/// <remarks>
/// <para>
/// These are commercial dials rather than invariants - the number of documents a free account may keep is a
/// pricing decision, and pricing decisions belong in configuration where they can move without a release. The
/// defaults here are what <c>cambelt.app</c> ships with.
/// </para>
/// <para>
/// <b>The comp list is how anybody is <see cref="AccountPlan.Pro"/> today.</b> It is the same shape as the
/// invitation list it replaces at the door - and it stays useful after checkout exists, because every product
/// of this kind ends up with a list of staff and friends who do not pay.
/// </para>
/// </remarks>
public sealed class PlanOptions
{
    /// <summary>Comma-separated addresses on the paid tier without paying. Blank comps nobody.</summary>
    public string? CompEmails { get; set; }

    /// <summary>Comma-separated domains, every verified address at which is comped. Blank comps nobody.</summary>
    public string? CompDomains { get; set; }

    /// <summary>
    /// Overrides for the free tier. <b>Every field starts null, and null means the shipped default</b> - the
    /// numbers themselves live in <c>AccountEntitlements.Defaults</c> and only there. Stating them here too
    /// would put one fact in two files, which is the drift this codebase keeps paying for.
    /// </summary>
    public PlanLimits Free { get; set; } = new();

    /// <summary>Overrides for the paid tier. Same rule as <see cref="Free"/>.</summary>
    public PlanLimits Pro { get; set; } = new();

    /// <summary>The allowances for one plan, as configuration writes them.</summary>
    /// <remarks>
    /// Nullable numbers throughout for the reason <c>ChatSettings.DailyTokensPerOwner</c> records: the compose
    /// file writes every key it knows about, an unset one arrives as <c>""</c>, and <c>""</c> bound to a plain
    /// <c>int</c> throws at boot over a key nobody filled in. Absent means the default; an explicit zero means
    /// zero.
    /// </remarks>
    public sealed class PlanLimits
    {
        public bool? ChatEnabled { get; set; }

        public long? DailyChatTokens { get; set; }

        public int? MaxDocuments { get; set; }

        public int? DailyVehicleLookups { get; set; }
    }
}
