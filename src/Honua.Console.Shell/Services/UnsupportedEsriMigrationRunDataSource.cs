using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedEsriMigrationRunDataSource : IEsriMigrationRunDataSource
{
    private const string Surface = "Import source content · migration run";

    private static readonly MigrationRunCapabilityState MissingBinding = new(
        Surface,
        "Missing binding",
        "honua-devops migration-run API",
        "The source content import wizard hands the run to honua-devops, which drives the migration against a "
            + "honua-server in the selected environment. No honua-server is configured (Honua:Server:BaseUrl / "
            + "HONUA_SERVER_BASE_URL) and honua-devops does not yet expose a Console-consumable run contract, so "
            + "there is nothing to run against. Console never shows mocked run results here — connect a "
            + "honua-server in Operate → Environments, or switch to an environment that already has one bound, "
            + "then re-open Import source content.");

    public Task<MigrationPlanLoad> LoadPlanAsync(string migrationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MigrationPlanLoad(null, [MissingBinding]));

    public Task<MigrationRunLoad> LoadRunAsync(string migrationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MigrationRunLoad(null, [MissingBinding]));

    public Task<MigrationScorecardLoad> LoadScorecardAsync(string migrationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MigrationScorecardLoad(null, [MissingBinding]));
}
