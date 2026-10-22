using static Ledger.Domain.Tests.TestAccounts;

namespace Ledger.Domain.Tests;

public sealed class PostingRulesTests
{
    private static readonly Dictionary<Guid, Account> _openAccounts = Map(Customer(PayerId), Customer(PayeeId));

    // --- Account checks -------------------------------------------------------

    [Fact]
    public void ValidRequestPassesAccountChecks() =>
        Assert.Null(PostingRules.Validate(Payment(100), _openAccounts));

    [Fact]
    public void MissingPayerIsAccountNotFound() =>
        Assert.Equal(Rejection.AccountNotFound, PostingRules.Validate(Payment(100), Map(Customer(PayeeId))));

    [Fact]
    public void MissingPayeeIsAccountNotFound() =>
        Assert.Equal(Rejection.AccountNotFound, PostingRules.Validate(Payment(100), Map(Customer(PayerId))));

    [Fact]
    public void ClosedPayerIsAccountClosed() =>
        Assert.Equal(Rejection.AccountClosed,
            PostingRules.Validate(Payment(100), Map(Customer(PayerId, AccountStatus.Closed), Customer(PayeeId))));

    [Fact]
    public void ClosedPayeeIsAccountClosed() =>
        Assert.Equal(Rejection.AccountClosed,
            PostingRules.Validate(Payment(100), Map(Customer(PayerId), Customer(PayeeId, AccountStatus.Closed))));

    [Fact]
    public void PaymentInAnotherCurrencyIsCurrencyMismatch() =>
        Assert.Equal(Rejection.CurrencyMismatch, PostingRules.Validate(Payment(100, Usd), _openAccounts));

    [Fact]
    public void PayeeInAnotherCurrencyIsCurrencyMismatch() =>
        Assert.Equal(Rejection.CurrencyMismatch,
            PostingRules.Validate(Payment(100), Map(Customer(PayerId), Customer(PayeeId, currency: Usd))));

    [Fact]
    public void MissingAccountIsReportedBeforeClosedOrCurrency() =>
        Assert.Equal(Rejection.AccountNotFound,
            PostingRules.Validate(Payment(100, Usd), Map(Customer(PayerId, AccountStatus.Closed))));

    [Fact]
    public void ClosedAccountIsReportedBeforeCurrencyMismatch() =>
        Assert.Equal(Rejection.AccountClosed,
            PostingRules.Validate(Payment(100, Usd), Map(Customer(PayerId, AccountStatus.Closed), Customer(PayeeId))));

    [Theory]
    [InlineData(Rejection.AccountNotFound, "account_not_found")]
    [InlineData(Rejection.AccountClosed, "account_closed")]
    [InlineData(Rejection.CurrencyMismatch, "currency_mismatch")]
    [InlineData(Rejection.InsufficientFunds, "insufficient_funds")]
    public void RejectionCodesMatchTheApiContract(Rejection rejection, string code) =>
        Assert.Equal(code, rejection.ToCode());

    // --- Entries ----------------------------------------------------------------

    [Fact]
    public void PaymentDebitsPayerAndCreditsPayeeWithTheSameAmount()
    {
        var entries = PostingRules.BuildEntries(Payment(25000));

        Assert.Collection(entries,
            e => Assert.Equal((PayerId, Side.Debit, 25000L, Zar), (e.AccountId, e.Side, e.AmountMinor, e.Currency)),
            e => Assert.Equal((PayeeId, Side.Credit, 25000L, Zar), (e.AccountId, e.Side, e.AmountMinor, e.Currency)));
        PostingRules.EnsureBalanced(entries);
    }

    [Fact]
    public void FewerThanTwoEntriesIsUnbalanced() =>
        Assert.Throws<UnbalancedTransactionException>(() =>
            PostingRules.EnsureBalanced([new Entry(PayerId, Side.Debit, 100, Zar)]));

    [Fact]
    public void DebitsNotEqualToCreditsIsUnbalanced() =>
        Assert.Throws<UnbalancedTransactionException>(() => PostingRules.EnsureBalanced(
            [new Entry(PayerId, Side.Debit, 100, Zar), new Entry(PayeeId, Side.Credit, 99, Zar)]));

    [Fact]
    public void MixedCurrenciesAreUnbalanced() =>
        Assert.Throws<UnbalancedTransactionException>(() => PostingRules.EnsureBalanced(
            [new Entry(PayerId, Side.Debit, 100, Zar), new Entry(PayeeId, Side.Credit, 100, Usd)]));

    [Fact]
    public void ThreeLineTransactionThatBalancesIsAccepted() =>
        PostingRules.EnsureBalanced(
        [
            new Entry(PayerId, Side.Debit, 100, Zar),
            new Entry(PayeeId, Side.Credit, 60, Zar),
            new Entry(SettlementId, Side.Credit, 40, Zar),
        ]);

    [Fact]
    public void SummingEntriesPastLongMaxThrowsInsteadOfWrapping() =>
        Assert.Throws<OverflowException>(() => PostingRules.EnsureBalanced(
        [
            new Entry(PayerId, Side.Debit, long.MaxValue, Zar),
            new Entry(SettlementId, Side.Debit, 1, Zar),
            new Entry(PayeeId, Side.Credit, long.MaxValue, Zar),
        ]));

    // --- Balances ---------------------------------------------------------------

    [Fact]
    public void PaymentLowersPayerAndRaisesPayee()
    {
        var balances = new Dictionary<Guid, long> { [PayerId] = 100_000, [PayeeId] = 5_000 };

        var changes = PostingRules.Apply(balances, _openAccounts, PostingRules.BuildEntries(Payment(25_000)));

        Assert.Contains(new BalanceChange(PayerId, 100_000, 75_000, false), changes);
        Assert.Contains(new BalanceChange(PayeeId, 5_000, 30_000, false), changes);
    }

    [Fact]
    public void TopUpRaisesBothTheSettlementAssetAndTheCustomer()
    {
        var accounts = Map(Settlement(), Customer(PayerId));
        var balances = new Dictionary<Guid, long> { [SettlementId] = 0, [PayerId] = 0 };

        var changes = PostingRules.Apply(balances, accounts, PostingRules.Transfer(SettlementId, PayerId, 1_000_000, Zar));

        Assert.All(changes, c => Assert.Equal(1_000_000, c.After));
    }

    [Fact]
    public void ChangesAreOrderedByAccountId()
    {
        var balances = new Dictionary<Guid, long> { [PayerId] = 100, [PayeeId] = 0 };

        var changes = PostingRules.Apply(balances, _openAccounts, PostingRules.BuildEntries(Payment(10)));

        Assert.Equal(changes.Select(c => c.AccountId).Order(), changes.Select(c => c.AccountId));
    }

    [Fact]
    public void BalanceOverflowThrowsInsteadOfWrapping()
    {
        var balances = new Dictionary<Guid, long> { [PayerId] = long.MaxValue, [PayeeId] = long.MaxValue };

        Assert.Throws<OverflowException>(() =>
            PostingRules.Apply(balances, _openAccounts, PostingRules.BuildEntries(Payment(1))));
    }

    // --- The full decision ------------------------------------------------------

    [Fact]
    public void FundedPaymentIsAccepted()
    {
        var decision = PostingRules.Decide(Payment(100), _openAccounts, Balances(payer: 100));

        Assert.True(decision.Accepted);
        Assert.Equal(2, decision.Entries.Count);
        Assert.Contains(decision.Changes, c => c.AccountId == PayerId && c.After == 0);
    }

    [Fact]
    public void PaymentBeyondTheBalanceIsInsufficientFunds()
    {
        var decision = PostingRules.Decide(Payment(101), _openAccounts, Balances(payer: 100));

        Assert.False(decision.Accepted);
        Assert.Equal(Rejection.InsufficientFunds, decision.Rejection);
        Assert.Empty(decision.Entries);
    }

    [Fact]
    public void AccountAllowedToGoNegativeMayOverdraw()
    {
        var accounts = Map(new Account(PayerId, AccountType.Liability, Zar, allowNegative: true), Customer(PayeeId));

        var decision = PostingRules.Decide(Payment(500), accounts, Balances(payer: 100));

        Assert.True(decision.Accepted);
        Assert.Contains(decision.Changes, c => c.AccountId == PayerId && c.After == -400);
    }

    [Fact]
    public void AccountChecksComeBeforeTheOverdraftRule()
    {
        var decision = PostingRules.Decide(Payment(101, Usd), _openAccounts, Balances(payer: 100));

        Assert.Equal(Rejection.CurrencyMismatch, decision.Rejection);
    }

    // --- Reversals and derived balances ----------------------------------------

    [Fact]
    public void ReversalSwapsEverySideAndCancelsTheOriginal()
    {
        var original = PostingRules.BuildEntries(Payment(25_000));

        var reversal = PostingRules.Reverse(original);

        PostingRules.EnsureBalanced(reversal);
        Assert.All(original.Zip(reversal), pair =>
        {
            Assert.Equal(pair.First.AccountId, pair.Second.AccountId);
            Assert.Equal(pair.First.AmountMinor, pair.Second.AmountMinor);
            Assert.NotEqual(pair.First.Side, pair.Second.Side);
        });
        foreach (var account in _openAccounts.Values)
        {
            Assert.Equal(0, PostingRules.BalanceFromEntries(account, original.Concat(reversal)));
        }
    }

    [Fact]
    public void BalanceFromEntriesUsesTheAccountsNormalSide()
    {
        var settlement = Settlement();
        var customer = Customer(PayerId);
        var history = PostingRules.Transfer(SettlementId, PayerId, 10_000, Zar)
            .Concat(PostingRules.BuildEntries(Payment(2_500)))
            .ToArray();

        Assert.Equal(10_000, PostingRules.BalanceFromEntries(settlement, history));
        Assert.Equal(7_500, PostingRules.BalanceFromEntries(customer, history));
    }

    private static Dictionary<Guid, long> Balances(long payer, long payee = 0) =>
        new() { [PayerId] = payer, [PayeeId] = payee };
}
