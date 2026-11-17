using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IStudioFormXlsFormDataSource
{
    Task<StudioFormXlsFormOutcome> ImportAsync(StudioFormXlsFormRequest request, CancellationToken cancellationToken = default);

    Task<StudioFormXlsFormOutcome> ExportAsync(string formId, CancellationToken cancellationToken = default);
}

public sealed class HonuaServerStudioFormXlsFormDataSource(IHonuaFormXlsFormClient client) : IStudioFormXlsFormDataSource
{
    private const string Surface = "XLSForm";

    public async Task<StudioFormXlsFormOutcome> ImportAsync(
        StudioFormXlsFormRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var upload = new HonuaFormXlsFormUpload(request.ServiceId.Trim(), request.LayerId, request.FileName, request.Content);
        var result = await client.ImportAsync(upload, cancellationToken).ConfigureAwait(false);
        var diagnostics = Diagnostics(result.Data?.Diagnostics);
        if (result.Issue is { } issue)
        {
            return Failure(issue, HonuaFormXlsFormHttpClient.ImportContract, diagnostics);
        }

        var version = result.Data!.Version!;
        return new StudioFormXlsFormOutcome(
            true,
            $"Imported {request.FileName} as draft version {version.Version.ToString(CultureInfo.InvariantCulture)} of {version.FormId}.",
            [])
        {
            FormId = version.FormId
        };
    }

    public async Task<StudioFormXlsFormOutcome> ExportAsync(string formId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);

        var result = await client.ExportAsync(formId, cancellationToken).ConfigureAwait(false);
        var diagnostics = Diagnostics(result.Data?.Diagnostics);
        if (result.Issue is { } issue)
        {
            return Failure(issue, HonuaFormXlsFormHttpClient.ExportContract, diagnostics);
        }

        var workbook = result.Data!.Workbook!;
        return new StudioFormXlsFormOutcome(true, $"Exported {workbook.FileName}.", [])
        {
            Workbook = workbook
        };
    }

    private static StudioFormXlsFormDiagnostic[] Diagnostics(HonuaFormXlsFormDiagnostic[]? diagnostics) =>
        (diagnostics ?? []).Select(StudioFormXlsFormDiagnostic.From).ToArray();

    private static StudioFormXlsFormOutcome Failure(
        HonuaAdminEndpointIssue issue,
        string contract,
        IReadOnlyList<StudioFormXlsFormDiagnostic> diagnostics)
    {
        var detail = issue.StatusCode is { } status
            ? $"{issue.Detail} HTTP {status.ToString(CultureInfo.InvariantCulture)}."
            : issue.Detail;
        return new StudioFormXlsFormOutcome(
            false,
            issue.Detail,
            diagnostics,
            new StudioFormCapabilityState(Surface, issue.State, issue.Contract ?? contract, detail));
    }
}
