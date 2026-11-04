using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IEsriMigrationRunDataSource
{
    Task<MigrationPlanLoad> LoadPlanAsync(string migrationId, CancellationToken cancellationToken = default);

    Task<MigrationRunLoad> LoadRunAsync(string migrationId, CancellationToken cancellationToken = default);

    Task<MigrationScorecardLoad> LoadScorecardAsync(string migrationId, CancellationToken cancellationToken = default);
}
