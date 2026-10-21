using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Payments.Api.Events;
using Payments.Api.Health;

namespace Payments.Api.Kafka;

/// <summary>
/// Walking skeleton only: consumes PaymentScored from payments.events and logs it.
/// From the scoring phase on, scoring is a synchronous call and this consumer goes.
/// Offsets are stored only after an event is handled, so delivery is at-least-once.
/// </summary>
public sealed partial class PaymentScoredConsumer(
    IOptions<KafkaOptions> options,
    Readiness readiness,
    ILogger<PaymentScoredConsumer> logger) : BackgroundService
{
    private readonly KafkaOptions _options = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // Consume() blocks, so run the loop on its own thread rather than a pool thread.
        Task.Factory.StartNew(() => Run(stoppingToken), stoppingToken,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private void Run(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            // Commit only offsets we have explicitly stored after handling a message.
            EnableAutoOffsetStore = false,
        };

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetPartitionsAssignedHandler((_, partitions) =>
            {
                readiness.MarkConsumerAssigned(partitions.Count > 0);
                LogAssigned(partitions.Count);
            })
            .SetPartitionsRevokedHandler((_, _) => readiness.MarkConsumerAssigned(false))
            .SetErrorHandler((_, error) => LogKafkaError(error.Reason))
            .Build();

        consumer.Subscribe(_options.PaymentsTopic);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);
                Handle(result.Message.Value);
                consumer.StoreOffset(result);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            consumer.Close();
        }
    }

    private void Handle(string value)
    {
        EventEnvelope<JsonElement>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope<JsonElement>>(value, Json.Options);
        }
        catch (JsonException ex)
        {
            // A malformed message cannot be fixed by retrying; log it and move on.
            LogMalformed(ex.Message);
            return;
        }

        if (envelope is null || envelope.EventType != EventTypes.PaymentScored)
        {
            return; // Other event types on this topic are not for this consumer.
        }

        var payload = envelope.Payload.Deserialize<PaymentScoredPayload>(Json.Options);
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["correlation_id"] = envelope.CorrelationId,
            ["payment_id"] = envelope.AggregateId,
        }))
        {
            LogScored(envelope.EventType, payload?.RiskScore, payload?.ModelVersion);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{EventType} received: risk_score={RiskScore} model_version={ModelVersion}")]
    private partial void LogScored(string eventType, decimal? riskScore, string? modelVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Consumer assigned {Count} partitions")]
    private partial void LogAssigned(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kafka error: {Reason}")]
    private partial void LogKafkaError(string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Skipping malformed event: {Reason}")]
    private partial void LogMalformed(string reason);
}
