using Npgsql;
using static Payments.Infrastructure.Tests.PaymentsDatabaseFixture;

namespace Payments.Infrastructure.Tests;

[Collection(PaymentsDatabaseDefinition.Name)]
public sealed class PaymentsSchemaTests(PaymentsDatabaseFixture db)
{
    private const string PermissionDenied = "42501";
    private const string RaisedByTrigger = "P0001";
    private const string CheckViolation = "23514";

    // --- Migrations --------------------------------------------------------------

    [Fact]
    public void AllFivePaymentsMigrationsApplyInOrder() =>
        Assert.Equal(
            ["P001__schema.sql", "P002__payments.sql", "P003__idempotency_keys.sql", "P004__audit.sql", "P005__indexes.sql"],
            db.FirstRun.AppliedScripts.Select(s => s[(s.IndexOf(".P", StringComparison.Ordinal) + 1)..]));

    [Fact]
    public void RunningMigrationsAgainAppliesNothing() =>
        Assert.Empty(PaymentsMigrator.Run(db.Postgres.ConnectionStringFor(Owner), Runtime).AppliedScripts);

    [Fact]
    public async Task JournalIsKeptInThePaymentsSchema() =>
        Assert.Equal(5, await db.ScalarAsync<long>(Owner, "SELECT count(*) FROM payments.schemaversions"));

    // --- What the runtime role may do ------------------------------------------

    [Fact]
    public async Task RuntimeRoleCanMoveAPaymentThroughItsStatesAndCompleteItsKey()
    {
        var payment = await db.InsertRequestedPaymentAsync();
        var key = Guid.NewGuid().ToString();

        await db.ExecuteAsync(Runtime, """
            INSERT INTO payments.idempotency_keys (client_id, key, request_hash, payment_id, status, locked_until)
            VALUES ('client-a', @key, 'hash', @payment, 'in_progress', now() + interval '30 seconds');
            UPDATE payments.payments SET state = 'Approved', updated_at = now() WHERE payment_id = @payment;
            UPDATE payments.payments SET state = 'Posted', ledger_txn_id = @txn, updated_at = now() WHERE payment_id = @payment;
            UPDATE payments.idempotency_keys SET status = 'completed', response_code = 201, response_body = '{}'::jsonb
              WHERE client_id = 'client-a' AND key = @key;
            INSERT INTO payments.audit_events (audit_id, actor_type, actor_id, action, entity_type, entity_id, correlation_id)
            VALUES (@auditId, 'system', 'payments-api', 'payment.state_changed', 'payment', @payment, 'corr-test');
            """, new { key, payment, txn = Guid.CreateVersion7(), auditId = Guid.CreateVersion7() });

        Assert.Equal("Posted", await db.ScalarAsync<string>(Runtime, "SELECT state FROM payments.payments WHERE payment_id = @payment", new { payment }));
    }

    // --- What the runtime role may never do ------------------------------------

    [Theory]
    [InlineData("DELETE FROM payments.payments")]
    [InlineData("TRUNCATE payments.payments")]
    [InlineData("DELETE FROM payments.idempotency_keys")]
    [InlineData("UPDATE payments.audit_events SET action = 'x'")]
    [InlineData("DELETE FROM payments.audit_events")]
    [InlineData("TRUNCATE payments.audit_events")]
    [InlineData("CREATE TABLE payments.sneaky (x int)")]
    [InlineData("CREATE TABLE public.sneaky (x int)")]
    [InlineData("ALTER TABLE payments.payments DROP CONSTRAINT payments_posted_has_ledger_txn")]
    public async Task RuntimeRoleIsDenied(string sql) =>
        await AssertSqlStateAsync(PermissionDenied, () => db.ExecuteAsync(Runtime, sql));

    [Fact]
    public async Task RuntimeRoleCannotReadAnotherServicesSchema()
    {
        await db.ExecuteAsync("ledger_owner", """
            CREATE SCHEMA IF NOT EXISTS ledger;
            CREATE TABLE IF NOT EXISTS ledger.isolation_probe (x int);
            """);

        await AssertSqlStateAsync(PermissionDenied, () => db.ExecuteAsync(Runtime, "SELECT * FROM ledger.isolation_probe"));
    }

    // --- Audit records are insert-only, even for the owner ------------------------

    [Theory]
    [InlineData("UPDATE payments.audit_events SET actor_id = 'someone-else'")]
    [InlineData("DELETE FROM payments.audit_events")]
    [InlineData("TRUNCATE payments.audit_events")]
    public async Task OwnerCannotRewriteAuditRecords(string sql)
    {
        await db.ExecuteAsync(Runtime, """
            INSERT INTO payments.audit_events (audit_id, actor_type, actor_id, action, entity_type, entity_id, correlation_id)
            VALUES (@id, 'system', 'payments-api', 'payment.created', 'payment', @id, 'corr-test')
            """, new { id = Guid.CreateVersion7() });

        await AssertSqlStateAsync(RaisedByTrigger, () => db.ExecuteAsync(Owner, sql));
    }

    // --- Rules the database enforces ----------------------------------------------

    [Theory]
    [InlineData("UPDATE payments.payments SET state = 'Posted' WHERE payment_id = @id")]                                          // posted without a ledger transaction
    [InlineData("UPDATE payments.payments SET state = 'Declined' WHERE payment_id = @id")]                                        // declined without a reason
    [InlineData("UPDATE payments.payments SET decline_reason = 'insufficient_funds' WHERE payment_id = @id")]                    // reason without being declined
    [InlineData("UPDATE payments.payments SET state = 'Refunded' WHERE payment_id = @id")]                                        // unknown state
    [InlineData("UPDATE payments.payments SET payee_account_id = payer_account_id WHERE payment_id = @id")]                       // same account both sides
    [InlineData("UPDATE payments.payments SET amount_minor = 0 WHERE payment_id = @id")]
    [InlineData("UPDATE payments.payments SET currency = 'zar' WHERE payment_id = @id")]
    public async Task InconsistentPaymentRowsAreRejected(string sql)
    {
        var id = await db.InsertRequestedPaymentAsync();

        await AssertSqlStateAsync(CheckViolation, () => db.ExecuteAsync(Runtime, sql, new { id }));
    }

    [Fact]
    public async Task CompletedKeyWithoutAStoredResponseIsRejected() =>
        await AssertSqlStateAsync(CheckViolation, () => db.ExecuteAsync(Runtime, """
            INSERT INTO payments.idempotency_keys (client_id, key, request_hash, status, locked_until)
            VALUES ('client-a', @key, 'hash', 'completed', now())
            """, new { key = Guid.NewGuid().ToString() }));

    private static async Task AssertSqlStateAsync(string expected, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.True(expected == ex.SqlState, $"expected SQLSTATE {expected}, got {ex.SqlState}: {ex.MessageText}");
    }
}
