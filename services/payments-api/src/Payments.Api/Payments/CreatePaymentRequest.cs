using System.Text.Json;
using System.Text.RegularExpressions;

namespace Payments.Api.Payments;

/// <summary>Body of POST /v1/payments (contracts/openapi/payments.v1.yaml).</summary>
public sealed record CreatePaymentRequest
{
    public required Guid PayerAccountId { get; init; }

    public required Guid PayeeAccountId { get; init; }

    /// <summary>Integer minor units (cents). Never fractional.</summary>
    public required long AmountMinor { get; init; }

    public required string Currency { get; init; }

    public required string Channel { get; init; }

    public string? DeviceId { get; init; }
}

public sealed record FieldError(string Field, string Message);

public static partial class CreatePaymentRequestValidator
{
    /// <summary>Largest integer that survives JSON parsers using IEEE-754 doubles (2^53 - 1).</summary>
    public const long MaxAmountMinor = 9_007_199_254_740_991;

    /// <summary>
    /// Parses and validates the body. Returns the request, or the field errors that
    /// make it invalid. Malformed JSON, wrong types, fractional amounts and unknown
    /// fields are all rejected by the strict serializer settings.
    /// </summary>
    public static (CreatePaymentRequest? Request, IReadOnlyList<FieldError> Errors) Parse(string body)
    {
        CreatePaymentRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<CreatePaymentRequest>(body, Json.Options);
        }
        catch (JsonException ex)
        {
            return (null, [new FieldError(FieldName(ex.Path), Clean(ex.Message))]);
        }

        if (request is null)
        {
            return (null, [new FieldError("body", "A JSON object is required.")]);
        }

        var errors = new List<FieldError>();
        if (request.AmountMinor is < 1 or > MaxAmountMinor)
        {
            errors.Add(new FieldError("amount_minor", $"Must be an integer from 1 to {MaxAmountMinor}."));
        }
        if (!CurrencyPattern().IsMatch(request.Currency))
        {
            errors.Add(new FieldError("currency", "Must be a three-letter upper-case ISO 4217 code."));
        }
        if (request.Channel.Length is < 1 or > 32)
        {
            errors.Add(new FieldError("channel", "Must be 1 to 32 characters."));
        }
        if (request.DeviceId is { Length: > 128 })
        {
            errors.Add(new FieldError("device_id", "Must be at most 128 characters."));
        }

        return errors.Count == 0 ? (request, errors) : (null, errors);
    }

    private static string FieldName(string? path) =>
        string.IsNullOrEmpty(path) || path == "$" ? "body" : path.TrimStart('$', '.');

    // Serializer messages end with internal type details; keep only the first sentence.
    private static string Clean(string message)
    {
        var end = message.IndexOf(". ", StringComparison.Ordinal);
        return end > 0 ? message[..(end + 1)] : message;
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyPattern();
}
