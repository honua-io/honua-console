using System.Text.Json;
using System.Text.RegularExpressions;

namespace Honua.Console.Web;

internal static partial class AgentWorkspaceBff
{
    internal const int DatasetImportChunkMaximumBytes = 4 * 1024 * 1024;
    private const int DatasetImportJsonMaximumBytes = 65536;

    internal sealed record DatasetImportRoute(string WorkspaceId, string? TaskId, long Revision, string Action)
    {
        internal bool IsChunk => Action == "chunks";
        internal bool Allows(string method) => Action switch
        {
            "create" or "renew" or "seal" or "source" => HttpMethods.IsPost(method),
            "chunks" => HttpMethods.IsPut(method),
            "upload" => HttpMethods.IsGet(method) || HttpMethods.IsPost(method) || HttpMethods.IsDelete(method),
            _ => HttpMethods.IsGet(method)
        };
    }

    internal static DatasetImportRoute? ReadDatasetImportRoute(string? path)
    {
        if (path is null) return null;
        var match = DatasetImportPath().Match(path);
        if (!match.Success) return null;
        var revision = 0L;
        if (match.Groups["revision"].Success && (!long.TryParse(match.Groups["revision"].Value, out revision) || revision < 1)) return null;
        var action = match.Groups["intent"].Success ? "intent" : !match.Groups["task"].Success ? "create"
            : match.Groups["authority"].Success ? (match.Groups["renewal"].Success ? "renew" : "authority")
            : match.Groups["upload"].Success ? (match.Groups["action"].Success ? match.Groups["action"].Value : "upload") : "version";
        return new(match.Groups["workspace"].Value, match.Groups["task"].Success ? match.Groups["task"].Value : null, revision, action);
    }

    internal static bool TryDatasetImportQuery(DatasetImportRoute route, string method, IQueryCollection query,
        out string suffix, out string? contentHash)
    {
        suffix = "";
        contentHash = null;
        string[] keys = route.Action switch
        {
            "authority" or "capabilities" => ["contentHash"],
            "chunks" => ["contentHash", "expectedVersion", "offset", "sha256"],
            "upload" when HttpMethods.IsGet(method) => ["contentHash"],
            "upload" when HttpMethods.IsDelete(method) => ["contentHash", "expectedVersion"],
            _ => []
        };
        if (query.Count != keys.Length) return false;
        var values = new List<string>();
        foreach (var key in keys)
        {
            if (!query.TryGetValue(key, out var entries) || entries.Count != 1) return false;
            var value = entries[0];
            if (key is "contentHash" or "sha256")
            {
                if (!IsDatasetDigest(value)) return false;
            }
            else if (value is null || !DatasetInteger().IsMatch(value) || !long.TryParse(value, out var number) ||
                number < (key == "offset" ? 0 : 1)) return false;
            if (key == "contentHash") contentHash = value;
            values.Add(key + "=" + Uri.EscapeDataString(value!));
        }
        if (values.Count > 0) suffix = "?" + string.Join("&", values);
        return true;
    }

    private static bool IsDatasetBodyTooLarge(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is DatasetImportBodyTooLargeException) return true;
        return false;
    }

    private static bool IsDatasetDigest(string? value)
        => value is { Length: 64 } && value.All(static character => char.IsAsciiHexDigitLower(character));

    // Safe response projections exclude internal provider keys even if an upstream
    // regression returns its persistence record instead of the canonical DTO.
    internal static bool TryProjectDatasetResponse(DatasetImportRoute route, string? contentHash,
        ReadOnlyMemory<byte> requestBody, ReadOnlyMemory<byte> responseBody, out string json)
    {
        json = "";
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var value = document.RootElement;
            if (value.ValueKind != JsonValueKind.Object) return false;
            if (contentHash is null && !requestBody.IsEmpty && route.Action is "upload" or "seal" or "source")
            {
                using var request = JsonDocument.Parse(requestBody);
                contentHash = request.RootElement.GetProperty("taskContentHash").GetString();
                if (!IsDatasetDigest(contentHash)) return false;
            }
            string[] allowed;
            if (route.Action is "upload" or "chunks" or "seal" or "source")
            {
                if (!MatchesTask(value, route, contentHash) || !value.TryGetProperty("uploadId", out var id) ||
                    id.GetString() is not { Length: 32 } uploadId || !uploadId.All(static character => char.IsAsciiHexDigitLower(character))) return false;
                allowed = ["taskId", "taskRevision", "taskContentHash", "uploadId", "version", "state", "acceptedBytes", "totalBytes",
                    "chunkBytes", "chunkCount", "expiresAt", "contentSha256", "sourceRetention"];
            }
            else if (route.Action == "capabilities")
            {
                if (value.GetProperty("chunkBytes").GetInt32() != DatasetImportChunkMaximumBytes ||
                    !value.GetProperty("maximumFileBytes").TryGetInt64(out var maximum) || maximum < 1 ||
                    maximum > (long)DatasetImportChunkMaximumBytes * 65536 || value.GetProperty("importAcceptanceSupported").GetBoolean()) return false;
                allowed = ["chunkBytes", "maximumFileBytes", "importAcceptanceSupported"];
            }
            else if (route.Action == "authority")
            {
                if (!MatchesTask(value, route, contentHash)) return false;
                allowed = ["taskId", "taskRevision", "taskContentHash", "executorActorId", "initiatingHumanActorId", "originatingTurnId",
                    "originalTurnHash", "authorityRevision", "authorityFingerprint", "policyContextFingerprint", "uploadAdmissionDeadline", "preparedOperationId"];
            }
            else if (route.Action == "renew")
            {
                using var request = JsonDocument.Parse(requestBody);
                if (value.GetProperty("renewalId").GetString() != request.RootElement.GetProperty("renewalId").GetString() ||
                    value.GetProperty("previousAuthorityRevision").GetInt64() != request.RootElement.GetProperty("expectedAuthorityRevision").GetInt64()) return false;
                allowed = ["renewalId", "previousAuthorityRevision", "authorityRevision", "authorityFingerprint", "renewedAt"];
            }
            else
            {
                if (value.GetProperty("workspaceId").GetString() != route.WorkspaceId ||
                    !IsDatasetIdentifier(value.GetProperty("taskId").GetString()) ||
                    !IsDatasetDigest(value.GetProperty("contentHash").GetString()) ||
                    value.GetProperty("revision").GetInt64() < 1 ||
                    route.TaskId is not null && (value.GetProperty("taskId").GetString() != route.TaskId || value.GetProperty("revision").GetInt64() != route.Revision)) return false;
                allowed = ["taskId", "revision", "contentHash", "ownerActorId", "tenantId", "targetId", "workspaceId", "destination",
                    "source", "options", "uploadAdmissionDeadline"];
            }
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                foreach (var name in allowed)
                {
                    if (!value.TryGetProperty(name, out var property)) continue;
                    writer.WritePropertyName(name);
                    if (name == "sourceRetention" && property.ValueKind != JsonValueKind.Null)
                    {
                        writer.WriteStartObject();
                        foreach (var field in new[] { "bytes", "sha256", "expiresAt" })
                            if (property.TryGetProperty(field, out var item)) { writer.WritePropertyName(field); item.WriteTo(writer); }
                        writer.WriteEndObject();
                    }
                    else if (name == "destination") WriteFields(writer, property, ["schema", "table"]);
                    else if (name == "options") WriteFields(writer, property, ["loadMode", "targetSrid", "sourceSrid"]);
                    else if (name == "source") WriteFields(writer, property, ["kind", "fileName", "totalBytes", "expectedSha256", "artifact", "browserSelectionId"]);
                    else property.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            json = System.Text.Encoding.UTF8.GetString(output.ToArray());
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            return false;
        }
    }

    private static bool IsDatasetIdentifier(string? value)
        => value is { Length: >= 1 and <= 128 } && value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static void WriteFields(Utf8JsonWriter writer, JsonElement value, string[] names)
    {
        writer.WriteStartObject();
        foreach (var name in names)
        {
            if (!value.TryGetProperty(name, out var property)) continue;
            writer.WritePropertyName(name);
            if (name == "artifact" && property.ValueKind != JsonValueKind.Null)
                WriteFields(writer, property, ["projectId", "snapshotId", "snapshotDigest", "runId", "path"]);
            else property.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    private static bool MatchesTask(JsonElement value, DatasetImportRoute route, string? hash)
        => value.GetProperty("taskId").GetString() == route.TaskId && value.GetProperty("taskRevision").GetInt64() == route.Revision &&
            IsDatasetDigest(hash) && value.GetProperty("taskContentHash").GetString() == hash;

    [GeneratedRegex(@"\A(?<workspace>[A-Za-z0-9_-]{1,128})/(?:dataset-import-task-intents/(?<intent>[A-Za-z0-9_-]{1,128})|dataset-import-tasks(?:/(?<task>[A-Za-z0-9_-]{1,128})/versions/(?<revision>[1-9][0-9]{0,18})(?:/(?:(?<authority>authority)(?<renewal>-renewals)?|(?<upload>upload)(?:/(?<action>capabilities|chunks|seal|source))?))?)?)\z", RegexOptions.CultureInvariant)]
    private static partial Regex DatasetImportPath();
    [GeneratedRegex(@"\A(?:0|[1-9][0-9]{0,18})\z", RegexOptions.CultureInvariant)]
    private static partial Regex DatasetInteger();
}
