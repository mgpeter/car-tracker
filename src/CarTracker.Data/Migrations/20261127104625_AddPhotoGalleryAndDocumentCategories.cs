using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Data.Migrations
{
    /// <summary>
    /// Photo renditions and dimensions on documents, the grouped document categories, and the vehicle's cover
    /// photo. Hand-ordered: the <c>Photo</c> to <c>ConditionPhoto</c> rename has to run between dropping the old
    /// type constraint and adding the new one.
    /// </summary>
    /// <remarks>
    /// Every existing <c>Photo</c> becomes <c>ConditionPhoto</c>, because the screen before this one presented
    /// every unlinked photo as the condition baseline and every linked one as issue evidence. The owner can
    /// re-tag any of them as <c>CarPhoto</c>. The rename keeps every row and changes only its label, so unlike
    /// <c>AddPerOwnerReferenceLists</c> it carries no precondition.
    /// </remarks>
    public partial class AddPhotoGalleryAndDocumentCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Renditions and dimensions on documents.
            migrationBuilder.AddColumn<string>(
                name: "thumbnail_path",
                table: "documents",
                type: "varchar(400)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_path",
                table: "documents",
                type: "varchar(400)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "width",
                table: "documents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "height",
                table: "documents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_dimensions",
                table: "documents",
                sql: "(width IS NULL AND height IS NULL) OR (width IS NOT NULL AND height IS NOT NULL AND width > 0 AND height > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_renditions",
                table: "documents",
                sql: "(thumbnail_path IS NULL) = (display_path IS NULL)");

            // 2. Categories: drop, rename, re-add.
            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.Sql("UPDATE documents SET type = 'ConditionPhoto' WHERE type = 'Photo';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "type IN ('V5C', 'BillOfSale', 'MOT', 'Insurance', 'RoadTax', 'Breakdown', 'ServiceInvoice', 'PartsReceipt', 'Warranty', 'Receipt', 'Manual', 'WorkshopManual', 'Diagram', 'CarPhoto', 'ConditionPhoto', 'Other')");

            // 3. Cover photo.
            migrationBuilder.AddColumn<int>(
                name: "cover_photo_id",
                table: "vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_vehicles_documents_cover_photo_id",
                table: "vehicles",
                column: "cover_photo_id",
                principalTable: "documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_cover_photo_id",
                table: "vehicles",
                column: "cover_photo_id");
        }

        /// <summary>
        /// LOSSY. Nine categories fold back onto the four the old constraint knew, and the renditions, dimensions
        /// and cover are dropped. Rendition files on disk are left where they are.
        /// </summary>
        /// <remarks>
        /// The mapping: <c>CarPhoto</c>, <c>ConditionPhoto</c> to <c>Photo</c>; <c>BillOfSale</c>,
        /// <c>ServiceInvoice</c>, <c>PartsReceipt</c> to <c>Receipt</c>; <c>WorkshopManual</c>, <c>Diagram</c> to
        /// <c>Manual</c>; <c>RoadTax</c>, <c>Breakdown</c>, <c>Warranty</c> to <c>Other</c>. Migrating up again
        /// turns every photo into a <c>ConditionPhoto</c>; which ones were car photos is not recoverable.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vehicles_cover_photo_id",
                table: "vehicles");

            migrationBuilder.DropForeignKey(
                name: "fk_vehicles_documents_cover_photo_id",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "cover_photo_id",
                table: "vehicles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_type",
                table: "documents");

            migrationBuilder.Sql(
                """
                UPDATE documents SET type = CASE
                    WHEN type IN ('CarPhoto', 'ConditionPhoto') THEN 'Photo'
                    WHEN type IN ('BillOfSale', 'ServiceInvoice', 'PartsReceipt') THEN 'Receipt'
                    WHEN type IN ('WorkshopManual', 'Diagram') THEN 'Manual'
                    WHEN type IN ('RoadTax', 'Breakdown', 'Warranty') THEN 'Other'
                    ELSE type
                END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_type",
                table: "documents",
                sql: "type IN ('V5C', 'Insurance', 'MOT', 'Receipt', 'Photo', 'Manual', 'Other')");

            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_renditions",
                table: "documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_dimensions",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "height",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "width",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "display_path",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "thumbnail_path",
                table: "documents");
        }
    }
}
