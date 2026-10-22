using static Ledger.Domain.Tests.TestAccounts;

namespace Ledger.Domain.Tests;

public sealed class AccountTests
{
    [Theory]
    [InlineData(AccountType.Asset, Side.Debit)]
    [InlineData(AccountType.Expense, Side.Debit)]
    [InlineData(AccountType.Liability, Side.Credit)]
    [InlineData(AccountType.Revenue, Side.Credit)]
    public void EachAccountTypeHasItsNormalSide(AccountType type, Side expected) =>
        Assert.Equal(expected, NormalSide.For(type));

    [Theory]
    [InlineData(AccountType.Asset, Side.Debit, 500)]
    [InlineData(AccountType.Asset, Side.Credit, -500)]
    [InlineData(AccountType.Expense, Side.Debit, 500)]
    [InlineData(AccountType.Liability, Side.Credit, 500)]
    [InlineData(AccountType.Liability, Side.Debit, -500)]
    [InlineData(AccountType.Revenue, Side.Credit, 500)]
    public void AnEntryOnTheNormalSideIncreasesTheBalance(AccountType type, Side side, long expected)
    {
        var account = new Account(Guid.CreateVersion7(), type, Zar);

        Assert.Equal(expected, account.SignedEffect(new Entry(account.Id, side, 500, Zar)));
    }

    [Fact]
    public void StoredNormalSideMustAgreeWithTheType() =>
        Assert.Throws<ArgumentException>(() =>
            new Account(Guid.CreateVersion7(), AccountType.Liability, Zar, normalSide: Side.Debit));

    [Fact]
    public void StoredNormalSideThatAgreesIsAccepted()
    {
        var account = new Account(Guid.CreateVersion7(), AccountType.Asset, Zar, normalSide: Side.Debit);

        Assert.Equal(Side.Debit, account.NormalSide);
    }

    [Theory]
    [InlineData("zar")]
    [InlineData("ZA")]
    [InlineData("ZARS")]
    [InlineData("Z1R")]
    [InlineData("")]
    public void CurrencyMustBeThreeUpperCaseLetters(string code) =>
        Assert.Throws<ArgumentException>(() => new Currency(code));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EntryAmountMustBePositive(long amount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Entry(PayerId, Side.Debit, amount, Zar));

    [Theory]
    [InlineData(0)]
    [InlineData(-25000)]
    public void PostingAmountMustBePositive(long amount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Payment(amount));

    [Fact]
    public void PayerAndPayeeMustDiffer() =>
        Assert.Throws<ArgumentException>(() => new PostingRequest(Guid.CreateVersion7(), PayerId, PayerId, 100, Zar));
}
