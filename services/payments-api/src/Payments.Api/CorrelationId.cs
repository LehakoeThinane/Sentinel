namespace Payments.Api;

/// <summary>
/// Ties together every log line, event and (later) trace caused by one request.
/// Taken from the X-Correlation-Id header when valid, otherwise generated.
/// </summary>
public static class CorrelationId
{
    public const string Header = "X-Correlation-Id";
    public const string ItemKey = "correlation_id";
    private const int MaxLength = 128;

    public static string FromRequest(HttpRequest request)
    {
        var supplied = request.Headers[Header].ToString();
        return IsValid(supplied) ? supplied : Guid.CreateVersion7().ToString();
    }

    // Printable ASCII only, so a caller cannot inject line breaks into logs or headers.
    private static bool IsValid(string value) =>
        value.Length is > 0 and <= MaxLength && value.All(c => c is > ' ' and <= '~');
}
