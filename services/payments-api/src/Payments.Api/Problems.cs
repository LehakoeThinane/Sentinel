using Payments.Api.Payments;

namespace Payments.Api;

/// <summary>RFC 9457 problem details with a machine-readable "code" extension member.</summary>
public static class Problems
{
    private const string TypeBase = "https://sentinel.example/problems/";

    public static IResult ClientIdMissing() => Create(
        StatusCodes.Status400BadRequest, "client_id_missing",
        "X-Client-Id header is required", "POST /v1/payments requires an X-Client-Id header.");

    public static IResult IdempotencyKeyMissing() => Create(
        StatusCodes.Status400BadRequest, "idempotency_key_missing",
        "Idempotency-Key header is required", "POST /v1/payments requires an Idempotency-Key header.");

    public static IResult ValidationFailed(IReadOnlyList<FieldError> errors) => Create(
        StatusCodes.Status400BadRequest, "validation_failed",
        "The request body is invalid", "One or more fields are missing or malformed.",
        new Dictionary<string, object?>
        {
            ["errors"] = errors.Select(e => new { field = e.Field, message = e.Message }).ToArray(),
        });

    /// <summary>Walking skeleton only: the broker did not acknowledge the event. Safe to retry.</summary>
    public static IResult EventPublishFailed(string detail) => Create(
        StatusCodes.Status503ServiceUnavailable, "event_publish_failed",
        "The payment could not be accepted right now", detail);

    private static IResult Create(int status, string code, string title, string detail,
        Dictionary<string, object?>? extensions = null)
    {
        extensions ??= [];
        extensions["code"] = code;
        return Results.Problem(
            type: TypeBase + code.Replace('_', '-'),
            title: title,
            statusCode: status,
            detail: detail,
            extensions: extensions);
    }
}
