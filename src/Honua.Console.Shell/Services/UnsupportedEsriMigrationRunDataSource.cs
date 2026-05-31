using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// #102 migration-run data source used when no honua-server / honua-devops run contract is bound. It renders
/// an explicit missing-binding state for the wizard plan, the run progress, and the parity scorecard rather
/// than fabricating run results, keeping the merged runtime free of a standing in-memory run engine (Console
/// Patterns Charter section 11). honua-devops owns the migration-run API (issue-122 handoff open item); until
/// it exposes a Console-bindable contract, the Run/Scorecard surfaces stay on this state and the wizard names
/// the honua-devops dependency.
/// </summary>
public sealed class UnsupportedEsriMigrationRunDataSource : IEsriMigrationRunDataSource
{
    private const string Surface = "Import from Esri · migration run";

    private static readonly MigrationRunCapabilityState MissingBinding = new(
        Surface,
        "Missing binding",
        "honua-devops migration-run API",
        "The Esri import wizard hands the run to honua-devops, which drives the migration against a "
            + "honua-server in the selected environment. No honua-server is configured (Honua:Server:BaseUrl / "
            + "HONUA_SERVER_BASE_URL) and honua-devops does not yet expose a Console-consumable run contract, so "
            + "there is nothing to run against. Console never shows mocked run results here — connect a "
            + "honua-server in Operate → Environments, or switch to an environment that already has one bound, "
            + "then re-open Import from Esri.");

    public Task<MigrationPlanLoad> LoadPlanAsync(string migrationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MigrationPlanLoad(null, [MissingBinding]));

    public Task<MigrationRunLoad> LoadRunAsync(string migrationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MigrationRunLoad(null, [MissingBinding]));

    public Task<MigrationScorecardLoad> LoadScorecardAsync(string migrationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MigrationScorecardLoad(null, [MissingBinding]));
}
