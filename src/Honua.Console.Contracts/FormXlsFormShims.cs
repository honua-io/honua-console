using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// XLSForm import and export for form packages. honua-server converts the workbook; the Console only
// uploads it, lists the blocking cells the server reports, and downloads the exported workbook.
//   POST /api/v1/admin/forms/packages/import?serviceId=&layerId=  multipart "file" -> 201 draft version
//   GET  /api/v1/admin/forms/packages/{formId}/export              -> workbook of the current published version
// A rejected workbook (400) or a published package XLSForm cannot represent (422) answers with a problem
// body whose "diagnostics" list names each blocking cell; those are returned alongside the issue.
public interface IHonuaFormXlsFormClient
{
    Task<HonuaAdminEndpointResult<HonuaFormXlsFormImport>> ImportAsync(
        HonuaFormXlsFormUpload upload,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaFormXlsFormExport>> ExportAsync(
        string formId,
        CancellationToken cancellationToken = default);
}

public sealed record HonuaFormXlsFormUpload(string ServiceId, int LayerId, string FileName, byte[] Content);

public sealed record HonuaFormXlsFormDiagnostic
{
    [JsonPropertyName("sheet")]
    public string Sheet { get; init; } = string.Empty;

    [JsonPropertyName("row")]
    public int Row { get; init; }

    [JsonPropertyName("column")]
    public string Column { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed record HonuaFormXlsFormImport(HonuaFormPackageVersion? Version, HonuaFormXlsFormDiagnostic[] Diagnostics);

public sealed record HonuaFormXlsFormWorkbook(string FileName, string ContentType, byte[] Content);

public sealed record HonuaFormXlsFormExport(HonuaFormXlsFormWorkbook? Workbook, HonuaFormXlsFormDiagnostic[] Diagnostics);

public sealed class HonuaFormXlsFormHttpClient : IHonuaFormXlsFormClient
{
    public const string ImportContract = "POST /api/v1/admin/forms/packages/import";
    public const string ExportContract = "GET /api/v1/admin/forms/packages/{formId}/export";
    public const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string AdminBase = "/api/v1/admin/forms/packages";
    private const string FilePartName = "file";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly AdminBodyFormat BodyFormat = new(
        JsonOptions,
        AdminBodyRead.Streamed,
        "The Honua server XLSForm import response did not match the expected contract");

    private readonly AdminEndpointTransport _transport;

    public HonuaFormXlsFormHttpClient(HttpClient httpClient, HonuaFormPackageClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        httpClient.BaseAddress ??= options.BaseUri;
        _transport = new AdminEndpointTransport(
            httpClient,
            options.ApiKey,
            AdminTransportFailure.Unreachable("The Honua server form endpoint"));
    }

    public Task<HonuaAdminEndpointResult<HonuaFormXlsFormImport>> ImportAsync(
        HonuaFormXlsFormUpload upload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentException.ThrowIfNullOrWhiteSpace(upload.ServiceId);

        var call = new AdminEndpointCall<HonuaFormXlsFormImport>(
            () => ImportRequest(upload),
            ImportContract,
            new(
                ReadDiagnostics(HttpStatusCode.BadRequest, diagnostics => new HonuaFormXlsFormImport(null, diagnostics)),
                AdminResponseBodies.Required<HonuaFormPackageVersion, HonuaFormXlsFormImport>(
                    BodyFormat,
                    "The Honua server XLSForm import response body was empty.",
                    version => new HonuaFormXlsFormImport(version, []))));
        return _transport.SendAsync(call, cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaFormXlsFormExport>> ExportAsync(
        string formId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);

        var call = new AdminEndpointCall<HonuaFormXlsFormExport>(
            () => new HttpRequestMessage(HttpMethod.Get, $"{AdminBase}/{Uri.EscapeDataString(formId)}/export"),
            ExportContract,
            new(
                ReadDiagnostics(HttpStatusCode.UnprocessableEntity, diagnostics => new HonuaFormXlsFormExport(null, diagnostics)),
                (_, response, token) => ReadWorkbookAsync(formId, response, token)));
        return _transport.SendAsync(call, cancellationToken);
    }

    private static HttpRequestMessage ImportRequest(HonuaFormXlsFormUpload upload)
    {
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"?serviceId={Uri.EscapeDataString(upload.ServiceId)}&layerId={upload.LayerId}");
        var file = new ByteArrayContent(upload.Content);
        file.Headers.ContentType = new MediaTypeHeaderValue(WorkbookContentType);
        var form = new MultipartFormDataContent { { file, FilePartName, upload.FileName } };
        return new HttpRequestMessage(HttpMethod.Post, $"{AdminBase}/import{query}") { Content = form };
    }

    private static async Task<HonuaAdminEndpointResult<HonuaFormXlsFormExport>> ReadWorkbookAsync(
        string formId,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var disposition = response.Content.Headers.ContentDisposition;
        var fileName = (disposition?.FileNameStar ?? disposition?.FileName)?.Trim('"');
        var workbook = new HonuaFormXlsFormWorkbook(
            string.IsNullOrWhiteSpace(fileName) ? $"{formId}.xlsx" : fileName,
            response.Content.Headers.ContentType?.MediaType ?? WorkbookContentType,
            content);
        return HonuaAdminEndpointResult<HonuaFormXlsFormExport>.FromData(new HonuaFormXlsFormExport(workbook, []));
    }

    // The diagnostics status carries the blocking cells; any other failure keeps the shared status mapping.
    private static AdminResponseReader<T> ReadDiagnostics<T>(
        HttpStatusCode diagnosticsStatus,
        Func<HonuaFormXlsFormDiagnostic[], T> wrap) =>
        async (contract, response, cancellationToken) =>
        {
            if (response.StatusCode != diagnosticsStatus)
            {
                return await AdminFailureMappings.AsReader<T>(AdminFailureMappings.ServerMessage)(contract, response, cancellationToken)
                    .ConfigureAwait(false);
            }

            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var issue = AdminEndpointIssueFactory.CreateIssue(contract, response, raw);
            if (AdminFailureMappings.ParseServerMessage(raw) is { Length: > 0 } message)
            {
                issue = issue with { Detail = message };
            }

            return new HonuaAdminEndpointResult<T>(wrap(ParseDiagnostics(raw)), issue);
        };

    private static HonuaFormXlsFormDiagnostic[] ParseDiagnostics(string raw)
    {
        try
        {
            return JsonSerializer.Deserialize<DiagnosticsProblem>(raw, JsonOptions)?.Diagnostics ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record DiagnosticsProblem
    {
        [JsonPropertyName("diagnostics")]
        public HonuaFormXlsFormDiagnostic[]? Diagnostics { get; init; }
    }
}
