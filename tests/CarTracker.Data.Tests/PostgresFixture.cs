using Testcontainers.PostgreSql;

namespace CarTracker.Data.Tests;

/// <summary>
/// A real PostgreSQL 17 instance for the test suite, started once per collection.
/// </summary>
/// <remarks>
/// Deliberately not the in-memory provider: it ignores column types, check constraints and foreign-key
/// behaviour, which is most of what the schema spec actually asserts. Tests against it would pass while
/// the real schema was wrong. Requires Docker to be running.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("cartracker_test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
