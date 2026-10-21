using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Payments.Api.Events;

namespace Payments.Api.Kafka;

public interface IEventPublisher
{
    /// <summary>
    /// Publishes an event keyed by its aggregate id and returns once the broker has
    /// acknowledged it. Throws <see cref="EventPublishException"/> if it cannot.
    /// </summary>
    Task PublishAsync<TPayload>(EventEnvelope<TPayload> envelope, CancellationToken ct);
}

public sealed class EventPublishException(string message, Exception inner) : Exception(message, inner);

public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;

    public KafkaEventPublisher(IOptions<KafkaOptions> options)
    {
        _options = options.Value;
        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            // Wait for all in-sync replicas and let the broker discard producer retries,
            // so a retried send can never be written twice.
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = _options.PublishTimeoutMs,
        };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync<TPayload>(EventEnvelope<TPayload> envelope, CancellationToken ct)
    {
        var message = new Message<string, string>
        {
            // The aggregate id is the partition key, so one payment's events stay in order.
            Key = envelope.AggregateId.ToString(),
            Value = JsonSerializer.Serialize(envelope, Json.Options),
        };
        try
        {
            await _producer.ProduceAsync(_options.PaymentsTopic, message, ct);
        }
        catch (ProduceException<string, string> ex)
        {
            throw new EventPublishException($"Could not publish {envelope.EventType}: {ex.Error.Reason}", ex);
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
