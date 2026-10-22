namespace Ledger.Domain;

/// <summary>
/// The ledger's posting and balance rules in one place, free of I/O so they can be
/// tested exhaustively. The posting service calls them inside one database transaction.
/// </summary>
public static class PostingRules
{
    /// <summary>
    /// Checks the accounts a payment touches. Order matters and is stable: a missing
    /// account is reported before a closed one, and a closed one before a currency mismatch.
    /// </summary>
    public static Rejection? Validate(PostingRequest request, IReadOnlyDictionary<Guid, Account> accounts)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(accounts);

        if (!accounts.TryGetValue(request.PayerAccountId, out var payer) ||
            !accounts.TryGetValue(request.PayeeAccountId, out var payee))
        {
            return Rejection.AccountNotFound;
        }
        if (payer.Status == AccountStatus.Closed || payee.Status == AccountStatus.Closed)
        {
            return Rejection.AccountClosed;
        }
        if (payer.Currency != request.Currency || payee.Currency != request.Currency)
        {
            return Rejection.CurrencyMismatch;
        }
        return null;
    }

    /// <summary>A payment debits the payer and credits the payee with the same amount.</summary>
    public static IReadOnlyList<Entry> BuildEntries(PostingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Transfer(request.PayerAccountId, request.PayeeAccountId, request.AmountMinor, request.Currency);
    }

    /// <summary>A balanced two-line transfer: debit one account, credit another.</summary>
    public static IReadOnlyList<Entry> Transfer(Guid debitAccountId, Guid creditAccountId, long amountMinor, Currency currency) =>
    [
        new Entry(debitAccountId, Side.Debit, amountMinor, currency),
        new Entry(creditAccountId, Side.Credit, amountMinor, currency),
    ];

    /// <summary>
    /// The entries of a reversing transaction: every line of the original on the
    /// opposite side. Posted entries are never edited; corrections are reversals.
    /// </summary>
    public static IReadOnlyList<Entry> Reverse(IReadOnlyList<Entry> original)
    {
        ArgumentNullException.ThrowIfNull(original);
        EnsureBalanced(original);
        return original
            .Select(e => new Entry(e.AccountId, e.Side == Side.Debit ? Side.Credit : Side.Debit, e.AmountMinor, e.Currency))
            .ToArray();
    }

    /// <summary>
    /// Asserts the double-entry invariants: at least two entries, one currency,
    /// debits equal to credits. A violation is a programming error, so it throws.
    /// </summary>
    public static void EnsureBalanced(IReadOnlyList<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count < 2)
        {
            throw new UnbalancedTransactionException($"A transaction needs at least two entries; got {entries.Count}.");
        }
        if (entries.Select(e => e.Currency).Distinct().Count() > 1)
        {
            throw new UnbalancedTransactionException("A transaction must use exactly one currency.");
        }
        long debits = 0, credits = 0;
        foreach (var e in entries)
        {
            if (e.Side == Side.Debit)
            {
                debits = checked(debits + e.AmountMinor);
            }
            else
            {
                credits = checked(credits + e.AmountMinor);
            }
        }
        if (debits != credits)
        {
            throw new UnbalancedTransactionException($"Debits ({debits}) must equal credits ({credits}).");
        }
    }

    /// <summary>
    /// Applies entries to current balances using normal-side arithmetic and returns
    /// one change per affected account, ordered by account id. Overflow throws.
    /// </summary>
    public static IReadOnlyList<BalanceChange> Apply(
        IReadOnlyDictionary<Guid, long> balances,
        IReadOnlyDictionary<Guid, Account> accounts,
        IReadOnlyList<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(balances);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(entries);

        var next = new SortedDictionary<Guid, long>();
        foreach (var entry in entries)
        {
            var account = accounts[entry.AccountId];
            var current = next.TryGetValue(entry.AccountId, out var running) ? running : balances[entry.AccountId];
            next[entry.AccountId] = checked(current + account.SignedEffect(entry));
        }
        return next
            .Select(kv => new BalanceChange(kv.Key, balances[kv.Key], kv.Value, accounts[kv.Key].AllowNegative))
            .ToArray();
    }

    /// <summary>
    /// The full check a posting goes through once its balances are locked: account
    /// rules, balanced entries, then the overdraft rule. Returns the rejection, or the
    /// entries and new balances to write.
    /// </summary>
    public static PostingDecision Decide(
        PostingRequest request,
        IReadOnlyDictionary<Guid, Account> accounts,
        IReadOnlyDictionary<Guid, long> balances)
    {
        if (Validate(request, accounts) is { } rejection)
        {
            return PostingDecision.Reject(rejection);
        }
        var entries = BuildEntries(request);
        EnsureBalanced(entries);
        var changes = Apply(balances, accounts, entries);
        return changes.Any(c => c.IsOverdrawn)
            ? PostingDecision.Reject(Rejection.InsufficientFunds)
            : PostingDecision.Accept(entries, changes);
    }

    /// <summary>An account's balance derived from its entries alone, in its normal direction.</summary>
    public static long BalanceFromEntries(Account account, IEnumerable<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(entries);
        long balance = 0;
        foreach (var entry in entries.Where(e => e.AccountId == account.Id))
        {
            balance = checked(balance + account.SignedEffect(entry));
        }
        return balance;
    }
}

public sealed record PostingDecision
{
    private PostingDecision(Rejection? rejection, IReadOnlyList<Entry> entries, IReadOnlyList<BalanceChange> changes)
    {
        Rejection = rejection;
        Entries = entries;
        Changes = changes;
    }

    public Rejection? Rejection { get; }

    public IReadOnlyList<Entry> Entries { get; }

    public IReadOnlyList<BalanceChange> Changes { get; }

    public bool Accepted => Rejection is null;

    public static PostingDecision Reject(Rejection rejection) => new(rejection, [], []);

    public static PostingDecision Accept(IReadOnlyList<Entry> entries, IReadOnlyList<BalanceChange> changes) =>
        new(null, entries, changes);
}
