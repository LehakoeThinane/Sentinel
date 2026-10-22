using Npgsql;
using static Ledger.Infrastructure.Tests.LedgerDatabaseFixture;

namespace Ledger.Infrastructure.Tests;

[Collection(LedgerDatabaseDefinition.Name)]
public sealed class LedgerSchemaTests(LedgerDatabaseFixture db)
{
    private const string PermissionDenied = "42501";
    private const string RaisedByTrigger = "P0001";
    private const string ForeignKeyViolation = "23503";
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";

    // --- Migrations --------------------------------------------------------------

    [Fact]
    public void AllFiveLedgerMigrationsApplyInOrder() =>
        Assert.Equal(
            ["L001__schema.sql", "L002__accounts_and_balances.sql", "L003__transactions_and_entries.sql", "L004__audit.sql", "L005__indexes.sql"],
            db.FirstRun.AppliedScripts.Select(s => s[(s.IndexOf(".L", StringComparison.Ordinal) + 1)..]));

    [Fact]
    public void RunningMigrationsAgainAppliesNothing() =>
        Assert.Empty(LedgerMigrator.Run(db.Postgres.ConnectionStringFor(Owner), Runtime).AppliedScripts);

    [Fact]
    public async Task JournalIsKeptInTheLedgerSchema() =>
        Assert.Equal(5, await db.ScalarAsync<long>(Owner, "SELECT count(*) FROM ledger.schemaversions"));

    [Fact]
    public void InvalidRuntimeRoleNameIsRefused() =>
        Assert.Throws<ArgumentException>(() => LedgerMigrator.Run(db.Postgres.ConnectionStringFor(Owner), "x; DROP ROLE y"));

    // --- What the runtime role may do ------------------------------------------

    [Fact]
    public async Task RuntimeRoleCanAppendAPostingAndUpdateBalances()
    {
        var (payer, payee) = await db.CreateAccountPairAsync();

        var txn = await db.AppendPaymentAsync(payer, payee, 25_000);
        await db.ExecuteAsync(Runtime,
            "UPDATE ledger.account_balances SET balance_minor = balance_minor - 25000 WHERE account_id = @payer", new { payer });

        Assert.Equal(2, await db.ScalarAsync<long>(Runtime, "SELECT count(*) FROM ledger.entries WHERE transaction_id = @txn", new { txn }));
        Assert.Equal(75_000, await db.ScalarAsync<long>(Runtime, "SELECT balance_minor FROM ledger.account_balances WHERE account_id = @payer", new { payer }));
    }

    // --- What the runtime role may never do ------------------------------------

    [Theory]
    [InlineData("UPDATE ledger.entries SET amount_minor = 1")]
    [InlineData("DELETE FROM ledger.entries")]
    [InlineData("TRUNCATE ledger.entries")]
    [InlineData("UPDATE ledger.transactions SET kind = 'adjustment'")]
    [InlineData("DELETE FROM ledger.transactions")]
    [InlineData("UPDATE ledger.audit_events SET action = 'x'")]
    [InlineData("DELETE FROM ledger.audit_events")]
    [InlineData("TRUNCATE ledger.audit_events")]
    [InlineData("INSERT INTO ledger.accounts (account_id, owner_ref, type, normal_side, currency) VALUES (gen_random_uuid(), 'x', 'asset', 'D', 'ZAR')")]
    [InlineData("UPDATE ledger.accounts SET status = 'closed'")]
    [InlineData("DELETE FROM ledger.account_balances")]
    [InlineData("CREATE TABLE ledger.sneaky (x int)")]
    [InlineData("CREATE TABLE public.sneaky (x int)")]
    [InlineData("DROP TABLE ledger.entries")]
    public async Task RuntimeRoleIsDenied(string sql) =>
        await AssertSqlStateAsync(PermissionDenied, () => db.ExecuteAsync(Runtime, sql));

    [Fact]
    public async Task RuntimeRoleCannotReadAnotherServicesSchema()
    {
        await db.ExecuteAsync("payments_owner", """
            CREATE SCHEMA IF NOT EXISTS payments;
            CREATE TABLE IF NOT EXISTS payments.isolation_probe (x int);
            """);

        await AssertSqlStateAsync(PermissionDenied, () => db.ExecuteAsync(Runtime, "SELECT * FROM payments.isolation_probe"));
    }

    // --- Insert-only holds even for the owner -----------------------------------

    [Theory]
    [InlineData("UPDATE ledger.entries SET amount_minor = amount_minor + 1")]
    [InlineData("DELETE FROM ledger.entries")]
    [InlineData("TRUNCATE ledger.entries CASCADE")]
    [InlineData("UPDATE ledger.transactions SET currency = 'USD'")]
    [InlineData("DELETE FROM ledger.transactions")]
    public async Task OwnerCannotRewritePostedRows(string sql)
    {
        var (payer, payee) = await db.CreateAccountPairAsync();
        await db.AppendPaymentAsync(payer, payee, 100);

        await AssertSqlStateAsync(RaisedByTrigger, () => db.ExecuteAsync(Owner, sql));
    }

    // --- Money rules the database enforces --------------------------------------

    [Fact]
    public async Task EntryInADifferentCurrencyFromItsTransactionIsRejected()
    {
        var (payer, _) = await db.CreateAccountPairAsync();
        var txn = Guid.CreateVersion7();
        await db.ExecuteAsync(Runtime,
            "INSERT INTO ledger.transactions (transaction_id, payment_id, kind, currency) VALUES (@txn, @p, 'payment', 'USD')",
            new { txn, p = Guid.CreateVersion7() });

        await AssertSqlStateAsync(ForeignKeyViolation, () => db.ExecuteAsync(Runtime,
            "INSERT INTO ledger.entries VALUES (@id, @txn, @payer, 'D', 100, 'ZAR')", new { id = Guid.CreateVersion7(), txn, payer }));
    }

    [Fact]
    public async Task EntryInADifferentCurrencyFromItsAccountIsRejected()
    {
        var (payer, _) = await db.CreateAccountPairAsync();
        var txn = Guid.CreateVersion7();
        await db.ExecuteAsync(Runtime,
            "INSERT INTO ledger.transactions (transaction_id, payment_id, kind, currency) VALUES (@txn, @p, 'payment', 'USD')",
            new { txn, p = Guid.CreateVersion7() });

        await AssertSqlStateAsync(ForeignKeyViolation, () => db.ExecuteAsync(Runtime,
            "INSERT INTO ledger.entries VALUES (@id, @txn, @payer, 'D', 100, 'USD')", new { id = Guid.CreateVersion7(), txn, payer }));
    }

    [Theory]
    [InlineData("liability", "D")]
    [InlineData("asset", "C")]
    [InlineData("revenue", "D")]
    [InlineData("expense", "C")]
    public async Task AccountWithTheWrongNormalSideIsRejected(string type, string normalSide) =>
        await AssertSqlStateAsync(CheckViolation, () => db.ExecuteAsync(Owner,
            "INSERT INTO ledger.accounts (account_id, owner_ref, type, normal_side, currency) VALUES (@id, 'x', @type, @normalSide, 'ZAR')",
            new { id = Guid.CreateVersion7(), type, normalSide }));

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task NonPositiveEntryAmountIsRejected(long amount)
    {
        var (payer, _) = await db.CreateAccountPairAsync();
        var txn = Guid.CreateVersion7();
        await db.ExecuteAsync(Runtime,
            "INSERT INTO ledger.transactions (transaction_id, payment_id, kind, currency) VALUES (@txn, @p, 'payment', 'ZAR')",
            new { txn, p = Guid.CreateVersion7() });

        await AssertSqlStateAsync(CheckViolation, () => db.ExecuteAsync(Runtime,
            "INSERT INTO ledger.entries VALUES (@id, @txn, @payer, 'D', @amount, 'ZAR')", new { id = Guid.CreateVersion7(), txn, payer, amount }));
    }

    [Fact]
    public async Task SecondPostingForTheSamePaymentIsRejected()
    {
        var payment = Guid.CreateVersion7();
        const string Sql = "INSERT INTO ledger.transactions (transaction_id, payment_id, kind, currency) VALUES (@txn, @payment, 'payment', 'ZAR')";
        await db.ExecuteAsync(Runtime, Sql, new { txn = Guid.CreateVersion7(), payment });

        await AssertSqlStateAsync(UniqueViolation, () => db.ExecuteAsync(Runtime, Sql, new { txn = Guid.CreateVersion7(), payment }));
    }

    [Fact]
    public async Task PaymentTransactionWithoutAPaymentIdIsRejected() =>
        await AssertSqlStateAsync(CheckViolation, () => db.ExecuteAsync(Runtime,
            "INSERT INTO ledger.transactions (transaction_id, kind, currency) VALUES (@txn, 'payment', 'ZAR')", new { txn = Guid.CreateVersion7() }));

    [Fact]
    public async Task ReversalMustNameTheTransactionItReverses() =>
        await AssertSqlStateAsync(CheckViolation, () => db.ExecuteAsync(Runtime,
            "INSERT INTO ledger.transactions (transaction_id, kind, currency) VALUES (@txn, 'reversal', 'ZAR')", new { txn = Guid.CreateVersion7() }));

    private static async Task AssertSqlStateAsync(string expected, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.True(expected == ex.SqlState, $"expected SQLSTATE {expected}, got {ex.SqlState}: {ex.MessageText}");
    }
}
