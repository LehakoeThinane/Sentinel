using Dapper;
using Npgsql;
using Sentinel.Testing;

namespace Payments.Infrastructure.Tests;

/// <summary>One PostgreSQL container per test run, with the payments migrations applied by its owner role.</summary>
public sealed class PaymentsDatabaseFixture : IAsyncLifetime
{
    public const string Owner = "payments_owner";
    public const string Runtime = "payments_svc";

    public PostgresWithRoles Postgres { get; } = new();

    public MigrationResult FirstRun { get; private set; } = new([]);

    public async Task InitializeAsync()
    {
        await Postgres.StartAsync();
        FirstRun = PaymentsMigrator.Run(Postgres.ConnectionStringFor(Owner), Runtime);
    }

    public async Task DisposeAsync() => await Postgres.DisposeAsync();

    public async Task<int> ExecuteAsync(string role, string sql, object? args = null)
    {
        await using var conn = new NpgsqlConnection(Postgres.ConnectionStringFor(role));
        return await conn.ExecuteAsync(sql, args);
    }

    public async Task<T> ScalarAsync<T>(string role, string sql, object? args = null)
    {
        await using var conn = new NpgsqlConnection(Postgres.ConnectionStringFor(role));
        return (await conn.ExecuteScalarAsync<T>(sql, args))!;
    }

    /// <summary>Inserts a Requested payment as the runtime role and returns its id.</summary>
    public async Task<Guid> InsertRequestedPaymentAsync(string clientId = "client-a")
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(Runtime, """
            INSERT INTO payments.payments
              (payment_id, client_id, payer_account_id, payee_account_id, amount_minor, currency, channel, state, correlation_id)
            VALUES (@id, @clientId, @payer, @payee, 25000, 'ZAR', 'app', 'Requested', 'corr-test')
            """, new { id, clientId, payer = Guid.CreateVersion7(), payee = Guid.CreateVersion7() });
        return id;
    }
}

[CollectionDefinition(Name)]
public sealed class PaymentsDatabaseDefinition : ICollectionFixture<PaymentsDatabaseFixture>
{
    public const string Name = "payments database";
}
