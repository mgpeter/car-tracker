using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <summary>
    /// Removes the mirrored expenses behind kit nobody has bought.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No schema change — this migration exists only to correct data.</b> The equipment expense mirror was
    /// gated on a cost and a purchase date and never on the item's status, while the add sheet pre-filled
    /// today's date on every new item. So pricing something on the shopping list gave it a date, the mirror
    /// fired, and a <c>Tools/Equipment</c> expense for kit that was never bought landed in spend, cost-per-mile
    /// and the Equipment &amp; Tools budget. <c>MirrorFor</c> and <c>shouldMirror</c> now check
    /// <c>EquipmentRules.CostIsSpend</c>, but nothing re-runs the mirror on rows already written — an existing
    /// one would keep counting until somebody happened to edit that item.
    /// </para>
    /// <para>
    /// Deleting the expense is not data loss. A mirror is a shadow, and this is the same reasoning that lets a
    /// fill's mirrored expense die with the fill: the item, its estimated cost, its status and its date all
    /// stay exactly as they are, and moving it to Owned or On order re-creates the expense through the ordinary
    /// write path. A no-op where there are none.
    /// </para>
    /// </remarks>
    public partial class DropMirrorsForUnboughtEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM expense_entries e
                USING equipment_items i
                WHERE e.equipment_item_id = i.id
                  AND i.status = 'ToOrder';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. Re-creating the expenses would restore figures that were wrong — and the rows
            // carried no information the equipment item does not still hold, so there is nothing to restore
            // from. Rolling back the code is what puts the old behaviour back; it will re-mirror on next edit.
        }
    }
}
