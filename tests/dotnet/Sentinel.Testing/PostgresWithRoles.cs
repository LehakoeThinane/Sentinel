using Npgsql;
using Testcontainers.PostgreSql;

namespace Sentinel.Testing;

/// <summary>
/// A throwaway PostgreSQL 16 container initialised with the repository's real
/// infra/local/postgres/00-roles.sql, so tests run against the same roles and
/// privileges as the local stack. Every role gets a fixed test-only password.
/// </summary>
public sealed class PostgresWithRoles : IAsyncDisposable
{
    public const string Database = "sentinel";
    private const string AdminUser = "sentinel_admin";
    private const string TestPassword = "test-only";

    private static readonly string[] _roles =
        ["ledger_owner", "ledger_svc", "payments_owner", "payments_svc", "cases_svc", "recon_svc"];

    private readonly PostgreSqlContainer _container;

    public PostgresWithRoles()
    {
        var builder = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase(Database)
            .WithUsername(AdminUser)
            .WithPassword(TestPassword)
            .WithResourceMapping(new FileInfo(RolesScriptPath()), "/docker-entrypoint-initdb.d/");
        foreach (var role in _roles)
        {
            builder = builder.WithEnvironment($"{role.ToUpperInvariant()}_PASSWORD", TestPassword);
        }
        _container = builder.Build();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _container.StartAsync(ct);
        await WaitUntilRolesExistAsync(ct);
    }

    /// <summary>A connection string that logs in as the given role.</summary>
    public string ConnectionStringFor(string role)
    {
        var csb = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Username = role,
            Password = TestPassword,
            Database = Database,
            Pooling = false,
        };
        return csb.ConnectionString;
    }

    public string AdminConnectionString => ConnectionStringFor(AdminUser);

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    // The init script runs while the server first starts; wait until its last role can log in.
    private async Task WaitUntilRolesExistAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            try
            {
                await using var conn = new NpgsqlConnection(ConnectionStringFor("recon_svc"));
                await conn.OpenAsync(ct);
                return;
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250, ct);
            }
        }
    }

    private static string RolesScriptPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Sentinel.sln")))
            {
                return Path.Combine(dir.FullName, "infra", "local", "postgres", "00-roles.sql");
            }
        }
        throw new InvalidOperationException("Could not find the repository root (Sentinel.sln).");
    }
}
