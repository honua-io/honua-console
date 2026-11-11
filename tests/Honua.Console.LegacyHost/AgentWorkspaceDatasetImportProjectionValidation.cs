using System.Text.Json;

namespace Honua.Console.Web;

internal static partial class AgentWorkspaceBff
{
    private static bool DatasetText(JsonElement value, int maximum = 256)
        => value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text && text.Length <= maximum &&
            text.Trim() == text && !text.Any(char.IsControl);
    private static bool DatasetTextField(JsonElement value, string name, int maximum = 256)
        => DatasetText(value.GetProperty(name), maximum);
    // Actor/tenant/target limits belong to the canonical authority, not this relay.
    // The entire response is already bounded to 64 KiB before projection.
    private static bool DatasetAuthorityString(JsonElement value)
        => value.ValueKind == JsonValueKind.String;
    private static bool DatasetDate(JsonElement value)
        => value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out _);
    private static bool DatasetHash(JsonElement value)
        => value.ValueKind == JsonValueKind.String && IsDatasetDigest(value.GetString());
    private static bool DatasetLong(JsonElement value, long minimum = 1, long maximum = long.MaxValue)
        => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= minimum && number <= maximum;
    private static bool DatasetOptional(JsonElement value, string name, Func<JsonElement, bool> validate)
        => !value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null || validate(field);
    private static bool DatasetOperation(JsonElement value)
        => value.ValueKind == JsonValueKind.String && value.GetString() is { Length: 32 } text &&
            text.All(static character => char.IsAsciiHexDigitLower(character));

    private static bool ValidDatasetUpload(JsonElement value)
    {
        var state = value.GetProperty("state").GetString();
        if (state is not ("receiving" or "sealed" or "cancelled") ||
            !DatasetLong(value.GetProperty("version")) || !DatasetLong(value.GetProperty("totalBytes")) ||
            !DatasetLong(value.GetProperty("acceptedBytes"), 0, value.GetProperty("totalBytes").GetInt64()) ||
            !DatasetLong(value.GetProperty("chunkBytes"), DatasetImportChunkMaximumBytes, DatasetImportChunkMaximumBytes) ||
            !DatasetLong(value.GetProperty("chunkCount"), 0, 65536) || !DatasetDate(value.GetProperty("expiresAt")) ||
            !DatasetOptional(value, "contentSha256", DatasetHash)) return false;
        if (state == "sealed" && (!value.TryGetProperty("contentSha256", out var content) || !DatasetHash(content))) return false;
        return DatasetOptional(value, "sourceRetention", retained => state == "sealed" && retained.ValueKind == JsonValueKind.Object &&
            DatasetLong(retained.GetProperty("bytes"), value.GetProperty("totalBytes").GetInt64(), value.GetProperty("totalBytes").GetInt64()) &&
            DatasetHash(retained.GetProperty("sha256")) && retained.GetProperty("sha256").GetString() == value.GetProperty("contentSha256").GetString() &&
            DatasetOptional(retained, "expiresAt", DatasetDate));
    }

    private static bool ValidDatasetAuthority(JsonElement value)
        => DatasetAuthorityString(value.GetProperty("executorActorId")) && DatasetTextField(value, "originatingTurnId") &&
            DatasetOptional(value, "initiatingHumanActorId", DatasetAuthorityString) && DatasetHash(value.GetProperty("originalTurnHash")) &&
            DatasetLong(value.GetProperty("authorityRevision")) && DatasetHash(value.GetProperty("authorityFingerprint")) &&
            DatasetHash(value.GetProperty("policyContextFingerprint")) && DatasetDate(value.GetProperty("uploadAdmissionDeadline")) &&
            DatasetOptional(value, "preparedOperationId", DatasetOperation);

    private static bool ValidDatasetRenewal(JsonElement value)
        => value.GetProperty("renewalId").ValueKind == JsonValueKind.String && IsDatasetIdentifier(value.GetProperty("renewalId").GetString()) &&
            DatasetLong(value.GetProperty("previousAuthorityRevision"), 1, long.MaxValue - 1) &&
            DatasetLong(value.GetProperty("authorityRevision")) &&
            value.GetProperty("authorityRevision").GetInt64() == value.GetProperty("previousAuthorityRevision").GetInt64() + 1 &&
            DatasetHash(value.GetProperty("authorityFingerprint")) && DatasetDate(value.GetProperty("renewedAt"));

    private static bool DatasetDatabaseIdentifier(JsonElement value, bool underscoreFirst)
        => value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 63 } text &&
            (char.IsAsciiLetter(text[0]) || underscoreFirst && text[0] == '_') &&
            text.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static bool ValidDatasetTask(JsonElement value)
    {
        var destination = value.GetProperty("destination");
        var source = value.GetProperty("source");
        var options = value.GetProperty("options");
        return DatasetAuthorityString(value.GetProperty("ownerActorId")) && DatasetAuthorityString(value.GetProperty("targetId")) &&
            DatasetOptional(value, "tenantId", DatasetAuthorityString) && DatasetDate(value.GetProperty("uploadAdmissionDeadline")) &&
            destination.ValueKind == JsonValueKind.Object && DatasetDatabaseIdentifier(destination.GetProperty("schema"), true) &&
            DatasetDatabaseIdentifier(destination.GetProperty("table"), false) && ValidDatasetSource(source) &&
            options.ValueKind == JsonValueKind.Object && DatasetLong(options.GetProperty("loadMode"), 0, 1) &&
            DatasetLong(options.GetProperty("targetSrid"), 1, int.MaxValue) &&
            DatasetOptional(options, "sourceSrid", field => DatasetLong(field, 1, int.MaxValue));
    }

    private static bool ValidDatasetSource(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object || source.GetProperty("kind").GetString() is not ("browser-file" or "local-process-artifact") ||
            !DatasetTextField(source, "fileName", 255) || source.GetProperty("fileName").GetString()!.Any(character => character is '/' or '\\') ||
            !DatasetLong(source.GetProperty("totalBytes")) || !DatasetOptional(source, "expectedSha256", DatasetHash) ||
            !DatasetOptional(source, "browserSelectionId", field => field.ValueKind == JsonValueKind.String && IsDatasetIdentifier(field.GetString()))) return false;
        if (source.GetProperty("kind").GetString() == "browser-file")
            return !source.TryGetProperty("artifact", out var empty) || empty.ValueKind == JsonValueKind.Null;
        if (source.TryGetProperty("browserSelectionId", out var selected) && selected.ValueKind != JsonValueKind.Null) return false;
        var artifact = source.GetProperty("artifact");
        return artifact.ValueKind == JsonValueKind.Object && source.TryGetProperty("expectedSha256", out var hash) && DatasetHash(hash) &&
            new[] { "projectId", "snapshotId", "runId" }.All(name => artifact.GetProperty(name).ValueKind == JsonValueKind.String && IsDatasetIdentifier(artifact.GetProperty(name).GetString())) &&
            DatasetHash(artifact.GetProperty("snapshotDigest")) && artifact.GetProperty("path").ValueKind == JsonValueKind.String &&
            artifact.GetProperty("path").GetString() is { Length: > 0 and <= 1024 } path && !path.Any(char.IsControl) && !path.Contains('\\') && !path.StartsWith('/') &&
            path.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));
    }
}
