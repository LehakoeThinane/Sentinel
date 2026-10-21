using Payments.Api;
using Payments.Api.Events;
using Payments.Api.Health;
using Payments.Api.Kafka;
using Payments.Api.Payments;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs; correlation_id and payment_id travel as scope fields.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.IncludeScopes = true;
    o.UseUtcTimestamp = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.Section));
var kafka = builder.Configuration.GetSection(KafkaOptions.Section).Get<KafkaOptions>() ?? new KafkaOptions();

builder.Services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
builder.Services.AddSingleton(new Readiness { ConsumerRequired = kafka.ConsumeScoredEvents });
if (kafka.ConsumeScoredEvents)
{
    builder.Services.AddHostedService<PaymentScoredConsumer>();
}
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = Json.Options.PropertyNamingPolicy;
});

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    var correlationId = CorrelationId.FromRequest(ctx.Request);
    ctx.Items[CorrelationId.ItemKey] = correlationId;
    ctx.Response.Headers[CorrelationId.Header] = correlationId;
    var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Payments.Api.Http");
    using (logger.BeginScope(new Dictionary<string, object> { ["correlation_id"] = correlationId }))
    {
        await next(ctx);
    }
});

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", (Readiness readiness) =>
    readiness.IsReady
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "starting" }, statusCode: StatusCodes.Status503ServiceUnavailable));

// Walking skeleton: validates and publishes PaymentRequested, stores nothing.
// It answers 202 because nothing is persisted yet; the contract's 201 responses
// arrive with the ledger. The Idempotency-Key check is enforced from day one.
app.MapPost("/v1/payments", async (HttpContext ctx, IEventPublisher publisher, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(ctx.Request.Headers["X-Client-Id"]))
    {
        return Problems.ClientIdMissing();
    }
    if (string.IsNullOrWhiteSpace(ctx.Request.Headers["Idempotency-Key"]))
    {
        return Problems.IdempotencyKeyMissing();
    }

    using var reader = new StreamReader(ctx.Request.Body);
    var (request, errors) = CreatePaymentRequestValidator.Parse(await reader.ReadToEndAsync(ct));
    if (request is null)
    {
        return Problems.ValidationFailed(errors);
    }

    var paymentId = Guid.CreateVersion7();
    var envelope = new EventEnvelope<PaymentRequestedPayload>(
        EventId: Guid.CreateVersion7(),
        EventType: EventTypes.PaymentRequested,
        SchemaVersion: 1,
        OccurredAt: DateTimeOffset.UtcNow,
        AggregateId: paymentId,
        CorrelationId: (string)ctx.Items[CorrelationId.ItemKey]!,
        Payload: new PaymentRequestedPayload(
            request.PayerAccountId, request.PayeeAccountId, request.AmountMinor,
            request.Currency, request.Channel, request.DeviceId));

    try
    {
        await publisher.PublishAsync(envelope, ct);
    }
    catch (EventPublishException ex)
    {
        return Problems.EventPublishFailed(ex.Message);
    }

    return Results.Json(new { payment_id = paymentId, state = "Requested" },
        statusCode: StatusCodes.Status202Accepted);
});

app.Run();

/// <summary>Entry point; public so integration tests can host the app.</summary>
public partial class Program;
