using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace CarTracker.Data.Tests;

/// <summary>
/// <c>AddPhotoGalleryAndDocumentCategories</c>: the one migration in the photo gallery spec that touches stored
/// data (every <c>Photo</c> row becomes <c>ConditionPhoto</c>), and the constraints it adds.
/// </summary>
/// <remarks>
/// The rename test migrates to the migration before this one, seeds through that schema with raw SQL (today's EF
/// model knows <c>cover_photo_id</c> and the rendition columns, which do not exist yet), and then migrates up.
/// The constraint tests run on a fully migrated database, also through raw SQL, because the point is what
/// PostgreSQL refuses, not what EF would have sent.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class PhotoGalleryMigrationTests(PostgresFixture postgres)
{
    /// <summary>The migration immediately before <c>AddPhotoGalleryAndDocumentCategories</c>.</summary>
    private const string Before = "AddTermsAcceptance";

    private static CarTrackerDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(connectionString).Options,
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero)));

    private static Task MigrateToAsync(CarTrackerDbContext context, string target) =>
        context.Database.GetService<IMigrator>().MigrateAsync(target);

    private static async Task<int> ScalarAsync(CarTrackerDbContext context, FormattableString sql) =>
        (await context.Database.SqlQuery<int>(sql).ToListAsync()).Single();

    private static async Task<int> SeedVehicleAsync(CarTrackerDbContext context, string externalId, string registration)
    {
        var email = $"{externalId.Replace('|', '.')}@example.test";
        var ownerId = await ScalarAsync(context,
            $"""
             INSERT INTO users (external_id, email, created_at)
             VALUES ({externalId}, {email}, '1970-01-01T00:00:00Z')
             RETURNING id AS "Value"
             """);

        return await ScalarAsync(context,
            $"""
             INSERT INTO vehicles (owner_id, registration, make, model, year, purchase_date, purchase_mileage,
                                   fuel_type, status, is_default, created_at, updated_at, source)
             VALUES ({ownerId}, {registration}, 'Land Rover', 'Freelander 1', 2003, '2026-03-14', 76632,
                     'Petrol', 'Active', false, now(), now(), 'web')
             RETURNING id AS "Value"
             """);
    }

    private static Task<int> SeedDocumentAsync(CarTrackerDbContext context, int vehicleId, string type, string title)
    {
        var path = $"{vehicleId}/{Guid.NewGuid():N}.jpg";
        return ScalarAsync(context,
            $"""
             INSERT INTO documents (vehicle_id, type, title, file_path, content_type, size_bytes,
                                    created_at, updated_at, source)
             VALUES ({vehicleId}, {type}, {title}, {path}, 'image/jpeg', 100, now(), now(), 'web')
             RETURNING id AS "Value"
             """);
    }

    private static async Task<PostgresException> RefusedAsync(CarTrackerDbContext context, FormattableString sql) =>
        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(sql));

    private async Task<CarTrackerDbContext> MigratedAsync(string database)
    {
        var context = NewContext(await postgres.EnsureDatabaseAsync(database));
        await context.Database.MigrateAsync();
        return context;
    }

    [Fact]
    public async Task Existing_photo_rows_become_condition_photos_and_nothing_else_moves()
    {
        var connectionString = await postgres.EnsureDatabaseAsync("cartracker_photogallery_rename");

        int photo, receipt, v5c;
        await using (var old = NewContext(connectionString))
        {
            await MigrateToAsync(old, Before);
            var vehicleId = await SeedVehicleAsync(old, "auth0|photo-rename", "BT53 AKJ");
            photo = await SeedDocumentAsync(old, vehicleId, "Photo", "Front three-quarter");
            receipt = await SeedDocumentAsync(old, vehicleId, "Receipt", "Oil and filter");
            v5c = await SeedDocumentAsync(old, vehicleId, "V5C", "Logbook");
        }

        await using (var migrate = NewContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        await using var reader = NewContext(connectionString);
        var types = await reader.Database
            .SqlQueryRaw<TypeRow>("SELECT id AS \"Id\", type AS \"Type\" FROM documents")
            .ToDictionaryAsync(r => r.Id, r => r.Type);

        Assert.Equal("ConditionPhoto", types[photo]);
        Assert.Equal("Receipt", types[receipt]);
        Assert.Equal("V5C", types[v5c]);
    }

    [Fact]
    public async Task The_type_constraint_accepts_every_member_and_refuses_the_old_photo()
    {
        await using var context = await MigratedAsync("cartracker_photogallery_schema");
        var vehicleId = await SeedVehicleAsync(context, $"auth0|types-{Guid.NewGuid():N}", "TYP 001");

        foreach (var type in Enum.GetNames<CarTracker.Shared.DocumentType>())
            await SeedDocumentAsync(context, vehicleId, type, $"A {type}");

        var refused = await Assert.ThrowsAsync<PostgresException>(
            () => SeedDocumentAsync(context, vehicleId, "Photo", "Old"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        Assert.Equal("ck_documents_type", refused.ConstraintName);
    }

    [Fact]
    public async Task Dimensions_come_as_a_positive_pair_or_not_at_all()
    {
        await using var context = await MigratedAsync("cartracker_photogallery_schema");
        var vehicleId = await SeedVehicleAsync(context, $"auth0|dims-{Guid.NewGuid():N}", "DIM 001");
        var id = await SeedDocumentAsync(context, vehicleId, "CarPhoto", "On the drive");

        var halfPair = await RefusedAsync(context, $"UPDATE documents SET width = 4032 WHERE id = {id}");
        Assert.Equal("ck_documents_dimensions", halfPair.ConstraintName);

        var zero = await RefusedAsync(context, $"UPDATE documents SET width = 0, height = 3024 WHERE id = {id}");
        Assert.Equal("ck_documents_dimensions", zero.ConstraintName);

        await context.Database.ExecuteSqlAsync($"UPDATE documents SET width = 4032, height = 3024 WHERE id = {id}");
    }

    [Fact]
    public async Task A_thumbnail_never_exists_without_a_display_copy()
    {
        await using var context = await MigratedAsync("cartracker_photogallery_schema");
        var vehicleId = await SeedVehicleAsync(context, $"auth0|rend-{Guid.NewGuid():N}", "REN 001");
        var id = await SeedDocumentAsync(context, vehicleId, "CarPhoto", "After a wash");

        var thumbOnly = await RefusedAsync(context, $"UPDATE documents SET thumbnail_path = 'a/t.webp' WHERE id = {id}");
        Assert.Equal("ck_documents_renditions", thumbOnly.ConstraintName);

        var displayOnly = await RefusedAsync(context, $"UPDATE documents SET display_path = 'a/d.webp' WHERE id = {id}");
        Assert.Equal("ck_documents_renditions", displayOnly.ConstraintName);

        await context.Database.ExecuteSqlAsync(
            $"UPDATE documents SET thumbnail_path = 'a/t.webp', display_path = 'a/d.webp' WHERE id = {id}");
    }

    [Fact]
    public async Task Deleting_the_cover_document_clears_the_vehicles_cover()
    {
        await using var context = await MigratedAsync("cartracker_photogallery_schema");
        var vehicleId = await SeedVehicleAsync(context, $"auth0|cover-{Guid.NewGuid():N}", "COV 001");
        var id = await SeedDocumentAsync(context, vehicleId, "CarPhoto", "Cover");

        await context.Database.ExecuteSqlAsync($"UPDATE vehicles SET cover_photo_id = {id} WHERE id = {vehicleId}");
        await context.Database.ExecuteSqlAsync($"DELETE FROM documents WHERE id = {id}");

        var remaining = await ScalarAsync(context,
            $"SELECT count(*)::int AS \"Value\" FROM vehicles WHERE id = {vehicleId} AND cover_photo_id IS NULL");
        Assert.Equal(1, remaining);
    }

    private sealed record TypeRow(int Id, string Type);
}
