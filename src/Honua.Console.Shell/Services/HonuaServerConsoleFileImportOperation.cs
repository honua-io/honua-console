using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class HonuaServerConsoleFileImportOperation(
    IHonuaAdminOperateClient client, IConsoleAuthorityBindingValidator? authority = null) : IConsoleFileImportOperation
{
    private const string ReviewFile = "Your user or environment changed. Choose the file again in the intended environment.";

    public async Task<IReadOnlyList<string>> GetSupportedExtensionsAsync(CancellationToken cancellationToken = default)
    {
        var result = await client.GetImportFormatsAsync(cancellationToken).ConfigureAwait(false);
        return result.Data?.SupportedExtensions ?? [];
    }

    public async Task<ConsoleFileImportPreview> PreviewAsync(byte[] bytes, string fileName, CancellationToken cancellationToken = default)
    {
        var binding = authority is null ? null : await authority.CaptureAsync(cancellationToken);
        if (binding is null) return new() { State = "Sign-in required", Detail = "Sign in to the selected environment before checking the file." };
        var result = await client.PreviewImportFileAsync(bytes, fileName, binding, cancellationToken).ConfigureAwait(false);
        if (!await authority!.IsCurrentAsync(binding, cancellationToken)) return new() { State = "Target changed", Detail = ReviewFile };
        if (result.Data?.CoordinateSystem is { } coordinateSystem && IsUsable(coordinateSystem))
            return new()
            {
                Succeeded = true,
                State = "Checked",
                CoordinateSystem = coordinateSystem,
                AuthorityBinding = binding,
                Warnings = result.Data.Warnings ?? []
            };
        return new()
        {
            State = result.Issue?.State ?? "Unavailable",
            Detail = result.Issue?.Detail ?? "This server cannot report the file’s coordinate system. Check its import capability before continuing."
        };
    }

    public async Task<OperateSectionResult<HonuaAdminCoordinateSystemSearch>> SearchCoordinateSystemsAsync(
        string query, string binding, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length > 128)
            return OperateSectionResult<HonuaAdminCoordinateSystemSearch>.Denied(OperateSectionStatus.Unavailable, "Enter an EPSG code or name using at most 128 characters.");
        if (authority is null || !await authority.IsCurrentAsync(binding, cancellationToken))
            return OperateSectionResult<HonuaAdminCoordinateSystemSearch>.Denied(OperateSectionStatus.Unavailable, ReviewFile);
        var result = await client.SearchCoordinateSystemsAsync(query, binding, cancellationToken).ConfigureAwait(false);
        if (!await authority.IsCurrentAsync(binding, cancellationToken))
            return OperateSectionResult<HonuaAdminCoordinateSystemSearch>.Denied(OperateSectionStatus.Unavailable, ReviewFile);
        if (result.Data is { } data && data.Items is { Count: <= 20 } items
            && items.All(item => item is not null && item.Code > 0 && !string.IsNullOrWhiteSpace(item.Name))
            && items.DistinctBy(item => item.Code).Count() == items.Count)
            return OperateSectionResult<HonuaAdminCoordinateSystemSearch>.Allowed(data);
        return OperateSectionResult<HonuaAdminCoordinateSystemSearch>.Denied(OperateSectionStatus.Unavailable,
            result.Issue?.Detail ?? "Coordinate systems could not be read. Search again before choosing one.");
    }

    public async Task<ConsoleFileImportResult> ImportAsync(ConsoleFileImportCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.AuthorityBinding) || authority is not { } bindingValidator)
            return new() { State = "Target unverified", Detail = "Check the file’s coordinate system before importing." };
        if (!await bindingValidator.IsCurrentAsync(command.AuthorityBinding, cancellationToken))
            return new() { State = "Target changed", Detail = ReviewFile };
        if (command.SourceSrid is <= 0 || command.TargetSrid is <= 0)
            return new() { State = "Invalid coordinate system", Detail = "Choose a supported coordinate system before importing." };

        var expectedSource = command.SourceSrid ?? command.ExpectedSourceSrid;
        if (expectedSource is not > 0)
            return new() { State = "Source unverified", Detail = "Check the file and choose its source coordinate system before importing." };
        var expectedStored = command.TargetSrid ?? expectedSource.GetValueOrDefault();
        var result = await ConsoleBoundWrite.SendAsync(
            () => client.ImportFileBoundAsync(new(command.FileContent, command.FileName, command.TableName,
                command.TargetSchema, command.SourceSrid, command.TargetSrid), command.AuthorityBinding, cancellationToken),
            () => bindingValidator.IsCurrentAsync(command.AuthorityBinding, CancellationToken.None)).ConfigureAwait(false);
        if (result.Issue is { State: ConsoleWriteOutcome.UnverifiedState } issue)
            return new() { State = issue.State, Detail = issue.Detail };
        if (result.Data is { Success: true } imported && (imported.SourceSrid is not > 0 || imported.StoredSrid is not > 0 || imported.Reprojected is null))
            return new() { State = ConsoleWriteOutcome.UnverifiedState, Detail = ConsoleWriteOutcome.UnverifiedDetail };
        // The endpoint returns HTTP 200 with success=false on a failed import; surface its reason.
        if (result.Data is { } data)
        {
            var mismatch = data.Success && (data.SourceSrid != expectedSource || data.StoredSrid != expectedStored
                || data.Reprojected != (expectedStored != expectedSource));
            return new()
            {
                Succeeded = data.Success && !mismatch,
                State = mismatch ? ConsoleFileImportResult.CoordinateSystemMismatchState : data.Success ? "Imported" : "Rejected",
                Detail = mismatch ? $"The server reported source EPSG:{data.SourceSrid}, stored EPSG:{data.StoredSrid} and reprojected={data.Reprojected}; expected source EPSG:{expectedSource} and stored EPSG:{expectedStored}. Read the original environment to check the imported table before making another change."
                    : data.Success ? $"Imported {data.FeatureCount} feature(s) into the new table. Publish it to make it a live layer."
                    : data.ErrorMessage ?? "The Honua server could not import the file.",
                TableName = data.TableName,
                FeatureCount = data.FeatureCount,
                SourceSrid = data.SourceSrid,
                StoredSrid = data.StoredSrid,
                Reprojected = data.Reprojected,
                ValidationErrors = data.ValidationErrors ?? []
            };
        }
        return new() { State = result.Issue?.State ?? "Unavailable", Detail = result.Issue?.Detail ?? "The server did not accept the file-import request." };
    }

    private static bool IsUsable(HonuaAdminImportCoordinateSystem system) =>
        system.DetectionMethod is "file-metadata" or "format-defined" or "unknown"
        && (system.DetectedSrid is null or > 0)
        && (system.RequiresSourceOverride ? system.EffectiveSourceSrid is null
            : system.DetectedSrid is > 0 && system.EffectiveSourceSrid == system.DetectedSrid)
        && (system.DetectedCrs is null || system.DetectedCrs.Code == system.DetectedSrid && !string.IsNullOrWhiteSpace(system.DetectedCrs.Name))
        && system.StoredSrid == system.EffectiveSourceSrid && system.TargetSrid is null && !system.Reprojected;
}
