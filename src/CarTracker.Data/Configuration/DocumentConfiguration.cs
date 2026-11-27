using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents", t =>
        {
            t.HasCheckConstraint("ck_documents_type",
                "type IN ('V5C', 'BillOfSale', 'MOT', 'Insurance', 'RoadTax', 'Breakdown', "
                + "'ServiceInvoice', 'PartsReceipt', 'Warranty', 'Receipt', 'Manual', 'WorkshopManual', 'Diagram', "
                + "'CarPhoto', 'ConditionPhoto', 'Other')");
            // The IS NOT NULL tests are load-bearing: "width > 0 AND height > 0" is NULL, not false, when height is
            // NULL, and a CHECK passes on NULL, so a half pair would slip through without them.
            t.HasCheckConstraint("ck_documents_dimensions",
                "(width IS NULL AND height IS NULL) OR (width IS NOT NULL AND height IS NOT NULL AND width > 0 AND height > 0)");
            // A display copy and a thumbnail are made together, so one without the other is a bug.
            t.HasCheckConstraint("ck_documents_renditions", "(thumbnail_path IS NULL) = (display_path IS NULL)");
            t.HasCheckConstraint("ck_documents_size_bytes", "size_bytes > 0");
            t.HasCheckConstraint("ck_documents_notes", "notes <> ''");
        });

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Type).HasColumnType("varchar(20)").HasConversion<string>().IsRequired();
        builder.Property(d => d.Title).HasColumnType("varchar(160)").IsRequired();
        builder.Property(d => d.DocumentDate).HasColumnType("date");
        builder.Property(d => d.FilePath).HasColumnType("varchar(400)").IsRequired();
        builder.Property(d => d.ContentType).HasColumnType("varchar(100)").IsRequired();
        builder.Property(d => d.SizeBytes).HasColumnType("bigint").IsRequired();
        builder.Property(d => d.Sha256).HasColumnType("char(64)");
        builder.Property(d => d.Notes).HasColumnType("text");
        builder.Property(d => d.ThumbnailPath).HasColumnType("varchar(400)");
        builder.Property(d => d.DisplayPath).HasColumnType("varchar(400)");
        builder.Property(d => d.Width).HasColumnType("integer");
        builder.Property(d => d.Height).HasColumnType("integer");

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(d => d.VehicleId).OnDelete(DeleteBehavior.Cascade);

        // Links are severed, never cascaded: the document outlives whatever it was attached to.
        builder.HasOne<ServiceRecord>()
            .WithMany()
            .HasForeignKey(d => d.ServiceRecordId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ExpenseEntry>()
            .WithMany()
            .HasForeignKey(d => d.ExpenseEntryId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Issue>()
            .WithMany()
            .HasForeignKey(d => d.IssueId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(d => new { d.VehicleId, d.Type })
            .HasDatabaseName("ix_documents_vehicle_type");

        builder.ConfigureAudit("documents");
    }
}
