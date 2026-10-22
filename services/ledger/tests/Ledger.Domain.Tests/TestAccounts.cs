namespace Ledger.Domain.Tests;

/// <summary>Synthetic accounts shared by the domain tests.</summary>
internal static class TestAccounts
{
    public static readonly Currency Zar = new("ZAR");
    public static readonly Currency Usd = new("USD");

    public static readonly Guid PayerId = Guid.Parse("0192f3a4-0000-7000-8000-000000000001");
    public static readonly Guid PayeeId = Guid.Parse("0192f3a4-0000-7000-8000-000000000900");
    public static readonly Guid SettlementId = Guid.Parse("0192f3a4-0000-7000-8000-0000000000aa");

    public static Account Customer(Guid id, AccountStatus status = AccountStatus.Open, Currency? currency = null) =>
        new(id, AccountType.Liability, currency ?? Zar, status);

    public static Account Settlement() => new(SettlementId, AccountType.Asset, Zar);

    public static Dictionary<Guid, Account> Map(params Account[] accounts) => accounts.ToDictionary(a => a.Id);

    public static PostingRequest Payment(long amountMinor, Currency? currency = null) =>
        new(Guid.CreateVersion7(), PayerId, PayeeId, amountMinor, currency ?? Zar);
}
