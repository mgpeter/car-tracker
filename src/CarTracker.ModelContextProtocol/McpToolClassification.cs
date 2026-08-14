namespace CarTracker.ModelContextProtocol;

/// <summary>
/// Which tools write. The single answer, read by everything that needs to know.
/// </summary>
/// <remarks>
/// <para>
/// Three consumers, and that is why this is not a private field on any one of them: <see cref="McpAuditFilter"/>
/// records a write to the audit trail, the in-app chat marks the same tools approval-required so its loop
/// suspends on them, and the chat's confirm gate refuses an id that does not name one. Two copies of "which
/// tools are writes" is exactly the drift that would make the confirm gate quietly skippable — a tool missing
/// from the chat's copy would execute without a draft card.
/// </para>
/// <para>
/// The list is the *declaration*; <c>[Authorize(Policy = "McpWrite")]</c> on the tool type is the *enforcement*.
/// They must agree, and a test asserts it rather than a comment asking politely — see
/// <c>McpToolClassificationTests</c>.
/// </para>
/// </remarks>
public static class McpToolClassification
{
    /// <summary>The write tools — those under the <c>McpWrite</c> scope. Reads are every other tool.</summary>
    public static IReadOnlySet<string> WriteToolNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "log_fuel_fillup", "add_service", "add_vehicle", "add_task", "complete_task",
        "log_expense", "update_mileage", "mark_check_done", "log_wash", "log_tyre_reading",
        "add_equipment", "add_issue", "add_issue_observation",
        "set_insurance", "set_road_tax", "update_vehicle_profile", "set_fluids", "set_tyre_specs",
        // Edit/delete suite (Phase 5) — DEC-014's original "no edit or delete via the assistant", reversed by
        // its own amendment.
        "update_fuel_fillup", "delete_fuel_fillup", "update_service", "delete_service",
        "update_mileage_reading", "delete_mileage_reading", "update_tyre_reading", "delete_tyre_reading",
        "update_wash", "delete_wash", "update_equipment", "delete_equipment",
    };

    /// <summary>True when <paramref name="toolName"/> changes a row, and so needs an audit entry and a human.</summary>
    public static bool IsWrite(string toolName) => WriteToolNames.Contains(toolName);
}
