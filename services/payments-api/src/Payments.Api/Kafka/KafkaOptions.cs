namespace Payments.Api.Kafka;

/// <summary>Bound from the "Kafka" configuration section (env: Kafka__BootstrapServers, ...).</summary>
public sealed class KafkaOptions
{
    public const string Section = "Kafka";

    public string BootstrapServers { get; set; } = "127.0.0.1:19092";

    public string PaymentsTopic { get; set; } = "payments.events";

    public string ConsumerGroupId { get; set; } = "payments-api";

    /// <summary>How long a publish may wait for the broker's acknowledgement.</summary>
    public int PublishTimeoutMs { get; set; } = 5000;

    /// <summary>Consume PaymentScored events. Disabled in tests that run without a broker.</summary>
    public bool ConsumeScoredEvents { get; set; } = true;
}
