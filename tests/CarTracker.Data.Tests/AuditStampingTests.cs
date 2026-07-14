using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class AuditStampingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Reference = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Reference);

    private string _connectionString = string.Empty;

    private AuditProbeContext NewContext() =>
        new(new DbContextOptionsBuilder<AuditProbeContext>()
                .UseNpgsql(_connectionString)
                .Options,
            _time);

    public async Task InitializeAsync()
    {
        // Own database: the probe model must not share EnsureCreated with the real model.
        _connectionString = await postgres.EnsureDatabaseAsync("audit_probe");
        await using var context = NewContext();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Insert_stamps_both_timestamps_to_the_current_time()
    {
        await using var context = NewContext();
        var probe = new AuditProbe { Name = "insert", Source = EntrySource.Web };

        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        Assert.Equal(Reference, probe.CreatedAt);
        Assert.Equal(Reference, probe.UpdatedAt);
    }

    [Fact]
    public async Task Insert_stamps_in_utc()
    {
        await using var context = NewContext();
        var probe = new AuditProbe { Name = "utc", Source = EntrySource.Web };

        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        Assert.Equal(TimeSpan.Zero, probe.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, probe.UpdatedAt.Offset);
    }

    [Fact]
    public async Task Update_advances_UpdatedAt_and_leaves_CreatedAt_alone()
    {
        int id;

        await using (var context = NewContext())
        {
            var probe = new AuditProbe { Name = "before", Source = EntrySource.Web };
            context.Probes.Add(probe);
            await context.SaveChangesAsync();
            id = probe.Id;
        }

        _time.Advance(TimeSpan.FromHours(3));

        await using (var context = NewContext())
        {
            var probe = await context.Probes.SingleAsync(p => p.Id == id);
            probe.Name = "after";
            await context.SaveChangesAsync();
        }

        // Read through a third context so the assertion is about what is stored, not what is tracked.
        await using (var context = NewContext())
        {
            var reloaded = await context.Probes.SingleAsync(p => p.Id == id);

            Assert.Equal(Reference, reloaded.CreatedAt);
            Assert.Equal(Reference.AddHours(3), reloaded.UpdatedAt);
        }
    }

    [Fact]
    public async Task Update_cannot_rewrite_CreatedAt()
    {
        int id;

        await using (var context = NewContext())
        {
            var probe = new AuditProbe { Name = "history", Source = EntrySource.Web };
            context.Probes.Add(probe);
            await context.SaveChangesAsync();
            id = probe.Id;
        }

        await using (var context = NewContext())
        {
            var probe = await context.Probes.SingleAsync(p => p.Id == id);
            probe.CreatedAt = Reference.AddYears(-5);
            await context.SaveChangesAsync();
        }

        await using (var reader = NewContext())
        {
            var reloaded = await reader.Probes.SingleAsync(p => p.Id == id);

            Assert.Equal(Reference, reloaded.CreatedAt);
        }
    }

    [Fact]
    public async Task Insert_without_a_Source_is_refused()
    {
        await using var context = NewContext();
        // No Source set — default(EntrySource) is 0, which the enum deliberately leaves undefined.
        context.Probes.Add(new AuditProbe { Name = "unattributed" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        Assert.Contains("Source", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_without_a_Source_is_refused()
    {
        int id;

        await using (var context = NewContext())
        {
            var probe = new AuditProbe { Name = "attributed", Source = EntrySource.Web };
            context.Probes.Add(probe);
            await context.SaveChangesAsync();
            id = probe.Id;
        }

        await using (var context = NewContext())
        {
            var probe = await context.Probes.SingleAsync(p => p.Id == id);
            probe.Name = "now unattributed";
            probe.Source = default;

            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Every_EntrySource_member_is_accepted()
    {
        await using var context = NewContext();

        foreach (var source in Enum.GetValues<EntrySource>())
        {
            context.Probes.Add(new AuditProbe { Name = $"source-{source}", Source = source });
        }

        await context.SaveChangesAsync();

        Assert.Equal(4, Enum.GetValues<EntrySource>().Length);
    }
}
