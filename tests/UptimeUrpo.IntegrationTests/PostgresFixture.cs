using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using UptimeUrpo.Api.Persistence;

namespace UptimeUrpo.IntegrationTests;

[CollectionDefinition("PostgreSQL", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string _migrationPassword = Guid.NewGuid().ToString("N");
    private readonly string _runtimePassword = Guid.NewGuid().ToString("N");
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18.6")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .WithCreateParameterModifier(parameters => parameters.HostConfig!.PortBindings!["5432/tcp"] =
            [new() { HostIP = "127.0.0.1", HostPort = "" }])
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        // These generated hex credentials exist only for this disposable container.
        await using var command = new NpgsqlCommand($"""
            CREATE ROLE urpo_migrator LOGIN PASSWORD '{_migrationPassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE;
            CREATE ROLE urpo_runtime LOGIN PASSWORD '{_runtimePassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE;
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<TestDatabase> CreateDatabaseAsync(bool migrate = true)
    {
        var name = "urpo_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name };
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\" OWNER urpo_migrator", connection);
            await create.ExecuteNonQueryAsync();
        }
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var permissions = new NpgsqlCommand($"REVOKE ALL ON DATABASE \"{name}\" FROM PUBLIC", connection);
            await permissions.ExecuteNonQueryAsync();
            permissions.CommandText = """
                REVOKE CREATE ON SCHEMA public FROM PUBLIC;
                GRANT USAGE ON SCHEMA public TO urpo_runtime;
                ALTER DEFAULT PRIVILEGES FOR ROLE urpo_migrator IN SCHEMA public
                  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO urpo_runtime;
                ALTER DEFAULT PRIVILEGES FOR ROLE urpo_migrator IN SCHEMA public
                  GRANT USAGE, SELECT ON SEQUENCES TO urpo_runtime;
                """;
            await permissions.ExecuteNonQueryAsync();
            permissions.CommandText = $"GRANT CONNECT ON DATABASE \"{name}\" TO urpo_runtime";
            await permissions.ExecuteNonQueryAsync();
        }
        string ForRole(string username, string password) => new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        {
            Username = username,
            Password = password,
            Timeout = 2,
            CommandTimeout = 3,
            MaxPoolSize = 10,
            IncludeErrorDetail = false
        }.ConnectionString;
        var database = new TestDatabase(admin.ConnectionString,
            ForRole("urpo_migrator", _migrationPassword), ForRole("urpo_runtime", _runtimePassword));
        if (migrate)
        {
            await using var context = database.MigrationContext();
            await context.Database.MigrateAsync();
            await database.ExecuteAdminAsync("REVOKE INSERT, UPDATE, DELETE ON \"__EFMigrationsHistory\" FROM urpo_runtime");
        }
        return database;
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

public sealed record TestDatabase(string Admin, string Migration, string Runtime)
{
    public MonitoringDbContext Context() => new(new DbContextOptionsBuilder<MonitoringDbContext>().UseNpgsql(Runtime).Options);
    public MonitoringDbContext MigrationContext() => new(new DbContextOptionsBuilder<MonitoringDbContext>().UseNpgsql(Migration).Options);

    public async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
