using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// #102 "Import from Esri" wizard run engine. The migration RUN is driven by honua-devops (the issue-122
/// handoff "migration-run API owner" open item assumes honua-devops drives the run). Console does not own
/// that contract, and no Console-consumable run API exists yet, so there is no standing in-memory run data
/// source in the merged result (Console Patterns Charter section 11): with no server bound — or until
/// honua-devops exposes a Console-bindable run contract — the Run and Scorecard surfaces render an explicit
/// missing-binding state instead of fabricating run progress, per-item results, or parity numbers.
///
/// The wizard's earlier steps (Source, Select content, Map) consume <see cref="LoadPlanAsync"/>; the parsed
/// conversion preview those steps show is deterministic Console-side work (see
/// <see cref="EsriContentImportParser"/>) and does not require this binding.
/// </summary>
public interface IEsriMigrationRunDataSource
{
    /// <summary>Loads the wizard's Map-step plan (selected items + per-item fidelity), or capability states.</summary>
    Task<MigrationPlanLoad> LoadPlanAsync(string migrationId, CancellationToken cancellationToken = default);

    /// <summary>Loads live run progress for a migration, or capability states when the run engine is unbound.</summary>
    Task<MigrationRunLoad> LoadRunAsync(string migrationId, CancellationToken cancellationToken = default);

    /// <summary>Loads the parity scorecard for a completed migration, or capability states when unbound.</summary>
    Task<MigrationScorecardLoad> LoadScorecardAsync(string migrationId, CancellationToken cancellationToken = default);
}
