using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents", t =>
        {
            t.HasCheckConstraint("ck_documents_type", "type IN ('V5C', 'Insurance', 'MOT', 'Receipt', 'Photo', 'Manual', 'Other')");
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
