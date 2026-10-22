namespace Ledger.Domain;

/// <summary>Which side of an entry a line is on.</summary>
public enum Side
{
    Debit,
    Credit,
}

public enum AccountType
{
    Asset,
    Liability,
    Revenue,
    Expense,
}

public enum AccountStatus
{
    Open,
    Closed,
}

public static class NormalSide
{
    /// <summary>
    /// The side on which an account's balance grows: debit for assets and expenses,
    /// credit for liabilities and revenue. Customer and merchant accounts are
    /// liabilities of the platform; the settlement bank account is an asset.
    /// </summary>
    public static Side For(AccountType type) => type switch
    {
        AccountType.Asset or AccountType.Expense => Side.Debit,
        AccountType.Liability or AccountType.Revenue => Side.Credit,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown account type."),
    };
}

/// <summary>An ISO 4217 alphabetic currency code, always three upper-case letters.</summary>
public readonly record struct Currency
{
    public Currency(string code)
    {
        if (code is not { Length: 3 } || !code.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException($"'{code}' is not a three-letter upper-case ISO 4217 code.", nameof(code));
        }
        Code = code;
    }

    public string Code { get; }

    public override string ToString() => Code;
}

public sealed record Account
{
    public Account(Guid id, AccountType type, Currency currency, AccountStatus status = AccountStatus.Open,
        bool allowNegative = false, Side? normalSide = null)
    {
        var expected = Domain.NormalSide.For(type);
        if (normalSide is { } given && given != expected)
        {
            // The stored normal side must agree with the type, or balances are computed with the wrong sign.
            throw new ArgumentException($"A {type} account is {expected}-normal, not {given}-normal.", nameof(normalSide));
        }
        Id = id;
        Type = type;
        Currency = currency;
        Status = status;
        AllowNegative = allowNegative;
        NormalSide = expected;
    }

    public Guid Id { get; }

    public AccountType Type { get; }

    public Side NormalSide { get; }

    public Currency Currency { get; }

    public AccountStatus Status { get; }

    public bool AllowNegative { get; }

    /// <summary>
    /// The signed effect of one entry on this account's balance, measured in the
    /// account's normal direction: an entry on the normal side increases it.
    /// </summary>
    public long SignedEffect(Entry entry) =>
        entry.Side == NormalSide ? entry.AmountMinor : checked(-entry.AmountMinor);
}
