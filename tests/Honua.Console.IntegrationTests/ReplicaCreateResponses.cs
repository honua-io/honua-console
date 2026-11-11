using System.Text.Json;

namespace Honua.Console.IntegrationTests;

// 400 skips because the seeded service does not declare Sync. 402 skips when the image has no
// offline-sync entitlement. 404, 405 and 501 skip when the pinned image does not serve the verb.
// GeoServices reports those same codes inside an HTTP 200 error envelope.
internal static class ReplicaCreateResponses
{
    internal readonly record struct Decision(bool Skip, string? ReplicaId, string? Failure);

    internal static Decision Classify(int statusCode, string body)
    {
        if (IsUnavailable(statusCode))
        {
            return new Decision(true, null, null);
        }

        if (statusCode is >= 200 and < 300)
        {
            return ClassifySuccess(statusCode, body);
        }

        return new Decision(false, null, $"createReplica returned {statusCode}.");
    }

    private static Decision ClassifySuccess(int statusCode, string body)
    {
        var document = Parse(body);
        if (document is null)
        {
            return MissingReplica(statusCode);
        }

        using (document)
        {
            var replicaId = ReadReplicaId(document.RootElement);
            if (replicaId is not null)
            {
                return new Decision(false, replicaId, null);
            }

            var errorCode = ReadErrorCode(document.RootElement);
            if (errorCode is int code)
            {
                if (IsUnavailable(code))
                {
                    return new Decision(true, null, null);
                }

                return new Decision(false, null, $"createReplica returned {statusCode} with error {code}.");
            }
        }

        return MissingReplica(statusCode);
    }

    private static bool IsUnavailable(int code) => code is 400 or 402 or 404 or 405 or 501;

    private static Decision MissingReplica(int statusCode) =>
        new(false, null, $"createReplica returned {statusCode} without replicaID.");

    private static JsonDocument? Parse(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadReplicaId(JsonElement root)
    {
        if (!root.TryGetProperty("replicaID", out var id) || id.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = id.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? ReadErrorCode(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error)
            || error.ValueKind != JsonValueKind.Object
            || !error.TryGetProperty("code", out var code)
            || code.ValueKind != JsonValueKind.Number
            || !code.TryGetInt32(out var value))
        {
            return null;
        }

        return value;
    }
}
