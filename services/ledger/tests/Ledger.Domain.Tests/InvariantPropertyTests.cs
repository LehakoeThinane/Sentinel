namespace Ledger.Domain.Tests;

/// <summary>
/// Property-based invariant tests: STUBS. These define what "correct" means for the
/// ledger, so the project owner writes or approves them; the coding agent must not
/// fill them in. Each stub states the property and how to generate inputs. They will
/// use FsCheck with a fixed seed in CI and a random seed nightly, logging the seed on
/// failure.
/// </summary>
public sealed class InvariantPropertyTests
{
    private const string OwnerWritten = "Stub: invariant test to be written or approved by the project owner.";

    /// <summary>
    /// Property: for any sequence of accepted postings (random accounts of every type,
    /// random positive amounts, one currency per transaction), the sum of all debit
    /// amounts equals the sum of all credit amounts, both per transaction and in total.
    /// The ledger never creates or loses money.
    /// </summary>
    [Fact(Skip = OwnerWritten)]
    public void RandomPaymentSequencesKeepDebitsEqualToCredits()
    {
    }

    /// <summary>
    /// Property: for any sequence of posting requests applied through
    /// <see cref="PostingRules.Decide"/>, no account with AllowNegative = false ever has
    /// a negative balance; requests that would overdraw are rejected with
    /// InsufficientFunds and change nothing.
    /// </summary>
    [Fact(Skip = OwnerWritten)]
    public void NoBalanceGoesBelowZeroUnlessAllowed()
    {
    }

    /// <summary>
    /// Property: after any sequence of accepted postings, each account's running balance
    /// (the BalanceChange.After values applied in order) equals
    /// <see cref="PostingRules.BalanceFromEntries"/> over all entries written so far,
    /// for every account type (asset, liability, revenue, expense).
    /// </summary>
    [Fact(Skip = OwnerWritten)]
    public void StoredBalanceEqualsTheSignedSumOfEntries()
    {
    }
}
