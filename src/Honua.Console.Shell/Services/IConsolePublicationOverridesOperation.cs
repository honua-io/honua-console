using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's publication-overrides authoring operation: reads and writes a publication's overrides
/// (titleOverride, per-publication field aliases, capabilities, supported formats, isPrimary) on honua-server
/// (<c>GET/PUT /api/v1/admin/metadata/publications/{publicationId}/overrides</c>). A "publication" is a layer's
/// exposure within a service. The live implementation is DI-gated on a configured server base URL; otherwise
/// the surface binds to <see cref="UnsupportedConsolePublicationOverridesOperation"/> (missing-binding, no
/// network call). It never fabricates overrides (Console Patterns Charter section 11).
/// </summary>
public interface IConsolePublicationOverridesOperation
{
    /// <summary>Reads the overrides for a publication (by its metadata id).</summary>
    Task<ConsolePublicationOverrides> GetOverridesAsync(
        string publicationId,
        CancellationToken cancellationToken = default);

    /// <summary>Writes the overrides for a publication (by its metadata id).</summary>
    Task<ConsoleSavePublicationOverridesResult> SaveOverridesAsync(
        string publicationId,
        ConsolePublicationOverrides overrides,
        CancellationToken cancellationToken = default);
}
