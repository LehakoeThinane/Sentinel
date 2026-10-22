using Dapper;
using Npgsql;
using Sentinel.Testing;

namespace Ledger.Infrastructure.Tests;

/// <summary>One PostgreSQL container per test run, with the ledger migrations applied by its owner role.</summary>
public sealed class LedgerDatabaseFixture : IAsyncLifetime
{
    public const string Owner = "ledger_owner";
    public const string Runtime = "ledger_svc";

    public PostgresWithRoles Postgres { get; } = new();

    public MigrationResult FirstRun { get; private set; } = new([]);

    public async Task InitializeAsync()
    {
        await Postgres.StartAsync();
        FirstRun = LedgerMigrator.Run(Postgres.ConnectionStringFor(Owner), Runtime);
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

    /// <summary>Creates two open ZAR liability accounts with opening balances, as the owner would.</summary>
    public async Task<(Guid Payer, Guid Payee)> CreateAccountPairAsync(long payerBalance = 100_000)
    {
        var payer = Guid.CreateVersion7();
        var payee = Guid.CreateVersion7();
        await ExecuteAsync(Owner, """
            INSERT INTO ledger.accounts (account_id, owner_ref, type, normal_side, currency)
            VALUES (@payer, 'cust_test_a', 'liability', 'C', 'ZAR'), (@payee, 'cust_test_b', 'liability', 'C', 'ZAR');
            INSERT INTO ledger.account_balances (account_id, balance_minor) VALUES (@payer, @payerBalance), (@payee, 0);
            """, new { payer, payee, payerBalance });
        return (payer, payee);
    }

    /// <summary>Appends a balanced two-line ZAR payment transaction as the runtime role.</summary>
    public async Task<Guid> AppendPaymentAsync(Guid payer, Guid payee, long amount)
    {
        var txn = Guid.CreateVersion7();
        await ExecuteAsync(Runtime, """
            INSERT INTO ledger.transactions (transaction_id, payment_id, kind, currency) VALUES (@txn, @payment, 'payment', 'ZAR');
            INSERT INTO ledger.entries (entry_id, transaction_id, account_id, direction, amount_minor, currency) VALUES
              (@e1, @txn, @payer, 'D', @amount, 'ZAR'),
              (@e2, @txn, @payee, 'C', @amount, 'ZAR');
            """, new { txn, payment = Guid.CreateVersion7(), e1 = Guid.CreateVersion7(), e2 = Guid.CreateVersion7(), payer, payee, amount });
        return txn;
    }
}

[CollectionDefinition(Name)]
public sealed class LedgerDatabaseDefinition : ICollectionFixture<LedgerDatabaseFixture>
{
    public const string Name = "ledger database";
}
