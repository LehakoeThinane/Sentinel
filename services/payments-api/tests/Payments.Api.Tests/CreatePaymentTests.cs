using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Payments.Api.Events;
using Payments.Api.Kafka;

namespace Payments.Api.Tests;

public sealed class CreatePaymentTests : IClassFixture<CreatePaymentTests.ApiFactory>
{
    private const string ValidBody = """
        {
          "payer_account_id": "0192f3a4-0000-7000-8000-000000000001",
          "payee_account_id": "0192f3a4-0000-7000-8000-000000000900",
          "amount_minor": 25000,
          "currency": "ZAR",
          "channel": "app",
          "device_id": "dev-7781"
        }
        """;

    private readonly ApiFactory _factory;

    public CreatePaymentTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Publisher.Reset();
    }

    [Fact]
    public async Task ValidRequestPublishesPaymentRequestedAndReturns202()
    {
        var response = await Post(ValidBody, correlationId: "test-corr-1");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var paymentId = body.GetProperty("payment_id").GetGuid();
        Assert.Equal("Requested", body.GetProperty("state").GetString());

        var published = Assert.Single(_factory.Publisher.Published);
        var envelope = Assert.IsType<EventEnvelope<PaymentRequestedPayload>>(published);
        Assert.Equal(EventTypes.PaymentRequested, envelope.EventType);
        Assert.Equal(1, envelope.SchemaVersion);
        Assert.Equal(paymentId, envelope.AggregateId);
        Assert.Equal("test-corr-1", envelope.CorrelationId);
        Assert.Equal(7, envelope.EventId.Version);
        Assert.Equal(25000L, envelope.Payload.AmountMinor);
        Assert.Equal("ZAR", envelope.Payload.Currency);
    }

    [Fact]
    public async Task SuppliedCorrelationIdIsEchoed()
    {
        var response = await Post(ValidBody, correlationId: "test-corr-2");

        Assert.Equal("test-corr-2", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task MissingCorrelationIdIsGeneratedEchoedAndPublished()
    {
        var response = await Post(ValidBody, correlationId: null);

        var echoed = response.Headers.GetValues("X-Correlation-Id").Single();
        Assert.False(string.IsNullOrWhiteSpace(echoed));
        var envelope = Assert.IsType<EventEnvelope<PaymentRequestedPayload>>(Assert.Single(_factory.Publisher.Published));
        Assert.Equal(echoed, envelope.CorrelationId);
    }

    [Fact]
    public async Task CorrelationIdWithControlCharactersIsReplaced()
    {
        var response = await Post(ValidBody, correlationId: "bad\tvalue");

        Assert.NotEqual("bad\tvalue", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task MissingIdempotencyKeyIsRejectedAndNothingIsPublished()
    {
        var response = await Post(ValidBody, idempotencyKey: null);

        await AssertProblem(response, HttpStatusCode.BadRequest, "idempotency_key_missing");
        Assert.Empty(_factory.Publisher.Published);
    }

    [Fact]
    public async Task MissingClientIdIsRejectedAndNothingIsPublished()
    {
        var response = await Post(ValidBody, clientId: null);

        await AssertProblem(response, HttpStatusCode.BadRequest, "client_id_missing");
        Assert.Empty(_factory.Publisher.Published);
    }

    [Theory]
    [InlineData("fractional amount", "\"amount_minor\": 250.5")]
    [InlineData("amount as string", "\"amount_minor\": \"25000\"")]
    [InlineData("zero amount", "\"amount_minor\": 0")]
    [InlineData("negative amount", "\"amount_minor\": -1")]
    [InlineData("amount above 2^53-1", "\"amount_minor\": 9007199254740992")]
    [InlineData("lower-case currency", "\"currency\": \"zar\"")]
    [InlineData("null currency", "\"currency\": null")]
    [InlineData("empty channel", "\"channel\": \"\"")]
    [InlineData("payer not a uuid", "\"payer_account_id\": \"acc-1\"")]
    public async Task InvalidFieldIsRejectedAndNothingIsPublished(string because, string replacement)
    {
        var field = replacement[..(replacement.IndexOf(':', StringComparison.Ordinal) + 1)];
        var body = string.Join('\n', ValidBody.Split('\n')
            .Select(line => line.TrimStart().StartsWith(field, StringComparison.Ordinal)
                ? "  " + replacement + (line.TrimEnd().EndsWith(',') ? "," : "")
                : line));

        var response = await Post(body);

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed", because);
        Assert.Empty(_factory.Publisher.Published);
    }

    [Theory]
    [InlineData("unknown field", """{"payer_account_id":"0192f3a4-0000-7000-8000-000000000001","payee_account_id":"0192f3a4-0000-7000-8000-000000000900","amount_minor":1,"currency":"ZAR","channel":"app","amount":2.5}""")]
    [InlineData("missing currency", """{"payer_account_id":"0192f3a4-0000-7000-8000-000000000001","payee_account_id":"0192f3a4-0000-7000-8000-000000000900","amount_minor":1,"channel":"app"}""")]
    [InlineData("not json", "amount=25000")]
    [InlineData("empty body", "")]
    public async Task MalformedBodyIsRejectedAndNothingIsPublished(string because, string body)
    {
        var response = await Post(body);

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed", because);
        Assert.Empty(_factory.Publisher.Published);
    }

    [Fact]
    public async Task BrokerFailureReturns503()
    {
        _factory.Publisher.FailNext = true;

        var response = await Post(ValidBody);

        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "event_publish_failed");
    }

    private async Task<HttpResponseMessage> Post(string body, string? clientId = "test-client",
        string? idempotencyKey = "key-1", string? correlationId = "test-corr")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/payments")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (clientId is not null) request.Headers.Add("X-Client-Id", clientId);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (correlationId is not null) request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        return await _factory.CreateClient().SendAsync(request);
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code,
        string because = "")
    {
        Assert.True(status == response.StatusCode, $"{because}: expected {status}, got {response.StatusCode}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.StartsWith("https://sentinel.example/problems/", problem.GetProperty("type").GetString(), StringComparison.Ordinal);
    }

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        public FakePublisher Publisher { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Kafka:ConsumeScoredEvents", "false");
            builder.ConfigureServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IEventPublisher>(Publisher)));
        }
    }

    public sealed class FakePublisher : IEventPublisher
    {
        private readonly List<object> _published = [];

        public IReadOnlyList<object> Published => _published;

        public bool FailNext { get; set; }

        public void Reset()
        {
            _published.Clear();
            FailNext = false;
        }

        public Task PublishAsync<TPayload>(EventEnvelope<TPayload> envelope, CancellationToken ct)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new EventPublishException("broker unavailable", new InvalidOperationException("test"));
            }
            _published.Add(envelope);
            return Task.CompletedTask;
        }
    }
}
