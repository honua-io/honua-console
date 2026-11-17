using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IStudioFormXlsFormDataSource
{
    Task<StudioFormXlsFormOutcome> ImportAsync(HonuaFormXlsFormUpload upload, CancellationToken cancellationToken = default);

    Task<StudioFormXlsFormOutcome> ExportAsync(string formId, CancellationToken cancellationToken = default);
}

public sealed class HonuaServerStudioFormXlsFormDataSource(IHonuaFormXlsFormClient client) : IStudioFormXlsFormDataSource
{
    private const string Surface = "XLSForm";

    public async Task<StudioFormXlsFormOutcome> ImportAsync(
        HonuaFormXlsFormUpload upload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        var result = await client.ImportAsync(upload with { ServiceId = upload.ServiceId.Trim() }, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return Failure(issue, HonuaFormXlsFormHttpClient.ImportContract, result.Data?.Diagnostics);
        }

        var version = result.Data!.Version!;
        if (string.IsNullOrWhiteSpace(version.FormId))
        {
            return StudioFormXlsFormOutcome.Rejected(
                "The server accepted the workbook but returned no form id, so the draft cannot be opened.",
                [],
                new StudioFormCapabilityState(Surface, "Unavailable", HonuaFormXlsFormHttpClient.ImportContract, "Import response had no formId."));
        }

        return StudioFormXlsFormOutcome.Imported(
            $"Imported {upload.FileName} as draft version {version.Version.ToString(CultureInfo.InvariantCulture)} of {version.FormId}.",
            version.FormId);
    }

    public async Task<StudioFormXlsFormOutcome> ExportAsync(string formId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);

        var result = await client.ExportAsync(formId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return Failure(issue, HonuaFormXlsFormHttpClient.ExportContract, result.Data?.Diagnostics);
        }

        var workbook = result.Data!.Workbook!;
        return StudioFormXlsFormOutcome.Exported($"Exported {workbook.FileName}.", workbook);
    }

    private static StudioFormXlsFormOutcome Failure(
        HonuaAdminEndpointIssue issue,
        string contract,
        HonuaFormXlsFormDiagnostic[]? diagnostics)
    {
        var detail = issue.StatusCode is { } status
            ? $"{issue.Detail} HTTP {status.ToString(CultureInfo.InvariantCulture)}."
            : issue.Detail;
        return StudioFormXlsFormOutcome.Rejected(
            issue.Detail,
            diagnostics ?? [],
            new StudioFormCapabilityState(Surface, issue.State, issue.Contract ?? contract, detail));
    }
}
