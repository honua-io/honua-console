using System.Text.Json;
using System.Globalization;

namespace Honua.Console.Contracts;

public sealed record HonuaAdminCoordinateSystem
{
    public int Code { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Unit { get; init; }
    public string? Area { get; init; }
}

public sealed record HonuaAdminCoordinateSystemSearch
{
    public IReadOnlyList<HonuaAdminCoordinateSystem> Items { get; init; } = [];
    public bool HasMore { get; init; }
}

public sealed record HonuaAdminImportCoordinateSystem
{
    public int? DetectedSrid { get; init; }
    public bool SourceCrsDeclared { get; init; }
    public string? DetectionMethod { get; init; }
    public HonuaAdminCoordinateSystem? DetectedCrs { get; init; }
    public int? EffectiveSourceSrid { get; init; }
    public int? TargetSrid { get; init; }
    public int? StoredSrid { get; init; }
    public bool Reprojected { get; init; }
    public bool RequiresSourceOverride { get; init; }
}

public sealed record HonuaAdminImportPreview
{
    public HonuaAdminImportCoordinateSystem? CoordinateSystem { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record HonuaAdminFileImportRequest(byte[] FileContent, string FileName, string TableName,
    string? TargetSchema, int? SourceSrid, int? TargetSrid);

public sealed partial class HonuaAdminOperateHttpClient
{
    public Task<HonuaAdminEndpointResult<HonuaAdminImportPreview>> PreviewImportFileAsync(
        byte[] fileContent, string fileName, string authorityBinding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return SendImportAsync(new ImportSend<HonuaAdminImportPreview>(
            HttpMethod.Post, "/api/v1/admin/import/preview", () => ImportFileForm(fileContent, fileName)),
            authorityBinding, cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminCoordinateSystemSearch>> SearchCoordinateSystemsAsync(
        string query, string authorityBinding, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (query.Trim().Length > 128) throw new ArgumentException("Use at most 128 characters.", nameof(query));
        return SendImportAsync(new ImportSend<HonuaAdminCoordinateSystemSearch>(
            HttpMethod.Get, $"/api/v1/admin/coordinate-systems?q={Uri.EscapeDataString(query.Trim())}&limit=20", () => null),
            authorityBinding, cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminImportResult>> ImportFileBoundAsync(
        HonuaAdminFileImportRequest request, string authorityBinding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TableName);
        var readOutcome = AdminResponseBodies.Json<JsonElement, HonuaAdminImportResult>(
            BareBody(AdminBodyRead.Buffered, "import outcome"),
            (body, context) => body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("success", out var success)
                && success.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? HonuaAdminEndpointResult<HonuaAdminImportResult>.FromData(body.Deserialize<HonuaAdminImportResult>(JsonOptions)!)
                    : context.Unsupported<HonuaAdminImportResult>("The server did not confirm the import outcome."));
        return SendImportAsync(new ImportSend<HonuaAdminImportResult>(HttpMethod.Post, "/api/v1/admin/import/upload", () =>
        {
            var form = ImportUploadForm(new(request.FileContent, request.FileName, request.TableName, request.TargetSchema));
            if (request.SourceSrid is { } source) form.Add(new StringContent(source.ToString(CultureInfo.InvariantCulture)), "SourceSrid");
            if (request.TargetSrid is { } target) form.Add(new StringContent(target.ToString(CultureInfo.InvariantCulture)), "TargetSrid");
            return form;
        }, readOutcome), authorityBinding, cancellationToken);
    }

    private sealed record ImportSend<T>(HttpMethod Method, string Path, Func<HttpContent?> Content, AdminResponseReader<T>? ReadBody = null)
        where T : class;

    private Task<HonuaAdminEndpointResult<T>> SendImportAsync<T>(
        ImportSend<T> send, string authorityBinding, CancellationToken cancellationToken) where T : class
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorityBinding);
        return _transport.SendAsync(new AdminEndpointCall<T>(() =>
        {
            var message = Request(send.Method, send.Path, send.Content());
            message.Options.Set(ConsoleAuthorityRequestOptions.Expected, authorityBinding);
            return message;
        }, $"{send.Method} {send.Path.Split('?')[0]}", new(AdminFailureMappings.AnyNonEmptyServerMessage,
            send.ReadBody ?? AdminResponseBodies.Json<T, T>(BareBody(AdminBodyRead.Buffered, "import"),
                (body, context) => body is null ? context.Unsupported<T>(MissingData) : HonuaAdminEndpointResult<T>.FromData(body)))), cancellationToken);
    }

    private static MultipartFormDataContent ImportFileForm(byte[] bytes, string name)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
        form.Add(file, "file", name);
        return form;
    }
}
