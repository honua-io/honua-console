using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Missing-binding implementation of the publication-overrides authoring operation. Used when no honua-server
/// base URL is configured: it performs no network call and returns explicit missing-binding results.
/// </summary>
public sealed class UnsupportedConsolePublicationOverridesOperation : IConsolePublicationOverridesOperation
{
    private const string BindingDetail =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can read and author publication overrides on honua-server.";

    public Task<ConsolePublicationOverrides> GetOverridesAsync(
        string publicationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsolePublicationOverrides.Unbound(BindingDetail));

    public Task<ConsoleSavePublicationOverridesResult> SaveOverridesAsync(
        string publicationId,
        ConsolePublicationOverrides overrides,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSavePublicationOverridesResult.MissingBinding(BindingDetail));
}
