using System.Text.Json;

namespace Honua.Console.IntegrationTests;

// 400 skips because the seeded service does not declare Sync. 402 skips when the image has no
// offline-sync entitlement. 404, 405 and 501 skip when the pinned image does not serve the verb.
internal static class ReplicaCreateResponses
{
    internal readonly record struct Decision(bool Skip, string? ReplicaId, string? Failure);

    internal static Decision Classify(int statusCode, string body)
    {
        if (statusCode is 400 or 402 or 404 or 405 or 501)
        {
            return new Decision(true, null, null);
        }

        if (statusCode is >= 200 and < 300)
        {
            var replicaId = ReadReplicaId(body);
            if (replicaId is not null)
            {
                return new Decision(false, replicaId, null);
            }

            return new Decision(false, null, $"createReplica returned {statusCode} without replicaID.");
        }

        return new Decision(false, null, $"createReplica returned {statusCode}.");
    }

    private static string? ReadReplicaId(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("replicaID", out var id)
                && id.ValueKind == JsonValueKind.String)
            {
                var value = id.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
