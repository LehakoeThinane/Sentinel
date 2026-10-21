using System.Text.Json;
using System.Text.Json.Serialization;

namespace Payments.Api;

/// <summary>Serializer settings shared by the HTTP API and events: snake_case, strict input.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = Create();

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            // Reject fields the contract does not define instead of silently dropping them.
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            // Numbers must be JSON numbers; "25000" is not an amount.
            NumberHandling = JsonNumberHandling.Strict,
            // "currency": null is invalid for a non-nullable property.
            RespectNullableAnnotations = true,
        };
        return options;
    }
}
