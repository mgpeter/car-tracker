using Npgsql;
using Testcontainers.PostgreSql;

namespace CarTracker.Data.Tests;

/// <summary>
/// A real PostgreSQL 17 instance for the test suite, started once per collection.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not the in-memory provider: it ignores column types, check constraints and foreign-key
/// behaviour, which is most of what the schema spec actually asserts. Tests against it would pass while
/// the real schema was wrong. Requires Docker to be running.
/// </para>
/// <para>
/// Tests against the real model apply <b>migrations</b>, not EnsureCreated — so the suite verifies the
/// migration that will actually run in production, rather than a schema EF derives from the model.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("cartracker_test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Creates (idempotently) a dedicated database in the container and returns its connection string.
    /// </summary>
    /// <remarks>
    /// Each DbContext model gets its own database because <c>EnsureCreated</c> is a no-op once *any*
    /// tables exist — two models sharing one database means whichever test class runs second silently
    /// gets no schema.
    /// </remarks>
    public async Task<string> EnsureDatabaseAsync(string name)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        exists.Parameters.AddWithValue("name", name);

        if (await exists.ExecuteScalarAsync() is null)
        {
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
