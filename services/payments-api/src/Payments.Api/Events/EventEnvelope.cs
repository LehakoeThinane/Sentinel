namespace Payments.Api.Events;

/// <summary>
/// The envelope every event uses (contracts/events/envelope.v1.schema.json).
/// Serialised with snake_case names.
/// </summary>
public sealed record EventEnvelope<TPayload>(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    Guid AggregateId,
    string CorrelationId,
    TPayload Payload);

/// <summary>Payload of PaymentRequested (contracts/events/payment-requested.v1.schema.json).</summary>
public sealed record PaymentRequestedPayload(
    Guid PayerAccountId,
    Guid PayeeAccountId,
    long AmountMinor,
    string Currency,
    string Channel,
    string? DeviceId);

/// <summary>
/// Payload of PaymentScored. Walking skeleton only: from the scoring phase on,
/// scoring is a synchronous call and this event is removed.
/// </summary>
public sealed record PaymentScoredPayload(decimal RiskScore, string ModelVersion);

public static class EventTypes
{
    public const string PaymentRequested = "PaymentRequested";
    public const string PaymentScored = "PaymentScored";
}
