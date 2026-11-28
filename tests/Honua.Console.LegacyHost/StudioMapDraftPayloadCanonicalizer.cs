using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Honua.Console.Web;

// Locked Studio bundle writes honua_map_package.v1 + mapPackageId while current servers validate
// honua_map.v1 + mapId; canonicalize legacy map draft payloads without masking ID conflicts.
internal static class StudioMapDraftPayloadCanonicalizer
{
    private const string LegacyMapFormat = "honua_map_package.v1";
    private const string CanonicalMapFormat = "honua_map.v1";

    public static byte[] Canonicalize(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return payload;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            return payload;
        }

        if (root is not JsonObject document
            || document["envelope"] is not JsonObject envelope
            || !IsMapFamily(envelope))
        {
            return payload;
        }

        return TryCanonicalizeEnvelope(envelope)
            ? Encoding.UTF8.GetBytes(document.ToJsonString())
            : payload;
    }

    private static bool IsMapFamily(JsonObject envelope) =>
        envelope["family"] is JsonValue value
        && value.TryGetValue<string>(out var family)
        && string.Equals(family, "map", StringComparison.OrdinalIgnoreCase);

    private static bool TryCanonicalizeEnvelope(JsonObject envelope)
    {
        var bodyStatus = envelope["body"] is JsonObject body
            ? CanonicalizeBody(body)
            : BodyOutcome.Unchanged;
        if (bodyStatus == BodyOutcome.Conflict)
        {
            return false;
        }

        var formatChanged = ReplaceLegacyFormat(envelope);
        return formatChanged || bodyStatus == BodyOutcome.Changed;
    }

    private static BodyOutcome CanonicalizeBody(JsonObject body)
    {
        var hasLegacyId = body.TryGetPropertyValue("mapPackageId", out var legacyId);
        var hasCanonicalId = body.TryGetPropertyValue("mapId", out var canonicalId);
        if (hasLegacyId && hasCanonicalId && !JsonNode.DeepEquals(legacyId, canonicalId))
        {
            return BodyOutcome.Conflict;
        }

        var changed = ReplaceLegacyFormat(body);
        if (!hasLegacyId)
        {
            return changed ? BodyOutcome.Changed : BodyOutcome.Unchanged;
        }

        if (!hasCanonicalId)
        {
            body["mapId"] = legacyId?.DeepClone();
        }

        body.Remove("mapPackageId");
        return BodyOutcome.Changed;
    }

    private static bool ReplaceLegacyFormat(JsonObject container)
    {
        if (container["format"] is not JsonValue value
            || !value.TryGetValue<string>(out var format)
            || !string.Equals(format, LegacyMapFormat, StringComparison.Ordinal))
        {
            return false;
        }

        container["format"] = CanonicalMapFormat;
        return true;
    }

    private enum BodyOutcome
    {
        Unchanged,
        Changed,
        Conflict
    }
}
