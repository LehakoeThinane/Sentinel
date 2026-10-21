namespace Payments.Api.Health;

/// <summary>
/// Tracks whether the service can do its job. The API is ready once the
/// PaymentScored consumer has partitions assigned (or when consuming is disabled).
/// </summary>
public sealed class Readiness
{
    private volatile bool _consumerAssigned;

    public bool ConsumerRequired { get; init; } = true;

    public bool IsReady => !ConsumerRequired || _consumerAssigned;

    public void MarkConsumerAssigned(bool assigned) => _consumerAssigned = assigned;
}
