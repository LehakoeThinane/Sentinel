namespace Ledger.Domain;

public enum TransactionKind
{
    Payment,
    Reversal,
    Adjustment,
}

/// <summary>One debit or credit line. Amounts are positive integer minor units.</summary>
public sealed record Entry
{
    public Entry(Guid accountId, Side side, long amountMinor, Currency currency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);
        AccountId = accountId;
        Side = side;
        AmountMinor = amountMinor;
        Currency = currency;
    }

    public Guid AccountId { get; }

    public Side Side { get; }

    public long AmountMinor { get; }

    public Currency Currency { get; }
}

/// <summary>A request to move an amount from a payer account to a payee account for one payment.</summary>
public sealed record PostingRequest
{
    public PostingRequest(Guid paymentId, Guid payerAccountId, Guid payeeAccountId, long amountMinor, Currency currency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountMinor);
        if (payerAccountId == payeeAccountId)
        {
            throw new ArgumentException("Payer and payee must be different accounts.", nameof(payeeAccountId));
        }
        PaymentId = paymentId;
        PayerAccountId = payerAccountId;
        PayeeAccountId = payeeAccountId;
        AmountMinor = amountMinor;
        Currency = currency;
    }

    public Guid PaymentId { get; }

    public Guid PayerAccountId { get; }

    public Guid PayeeAccountId { get; }

    public long AmountMinor { get; }

    public Currency Currency { get; }
}

/// <summary>Why a posting was refused. Codes match the ledger's internal API contract.</summary>
public enum Rejection
{
    AccountNotFound,
    AccountClosed,
    CurrencyMismatch,
    InsufficientFunds,
}

public static class RejectionCodes
{
    public static string ToCode(this Rejection rejection) => rejection switch
    {
        Rejection.AccountNotFound => "account_not_found",
        Rejection.AccountClosed => "account_closed",
        Rejection.CurrencyMismatch => "currency_mismatch",
        Rejection.InsufficientFunds => "insufficient_funds",
        _ => throw new ArgumentOutOfRangeException(nameof(rejection), rejection, null),
    };
}

/// <summary>Thrown when a set of entries would break double-entry rules. Indicates a bug, never a business outcome.</summary>
public sealed class UnbalancedTransactionException(string message) : Exception(message);

/// <summary>An account's balance before and after a posting, in its normal direction.</summary>
public sealed record BalanceChange(Guid AccountId, long Before, long After, bool AllowNegative)
{
    public bool IsOverdrawn => After < 0 && !AllowNegative;
}
