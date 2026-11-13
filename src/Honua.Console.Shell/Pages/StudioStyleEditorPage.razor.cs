using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Pages;

public partial class StudioStyleEditorPage
{
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private bool V4Enabled => ServiceProvider.GetService<ConsoleV4Options>()?.Enabled == true;

    // v4 renders load and save callouts as notices inside the Style panel; legacy keeps them as nested panels.
    private string CalloutClass => V4Enabled ? "console-state" : "console-panel";

    private sealed record Review(string StyleId, HonuaOgcStyleEncoding Mode, string EntityTag, string ContentHash, string ImpactHash, string Authority, long ObservationEpoch);

    private Review? CurrentReview => CanEditShared && _impact is { } impact && _entityTag is { } etag && _authorityBinding is { } authority
        ? new(_loadedStyleId, _loadedMode, etag, impact.ContentHash, impact.ImpactHash, authority, _observationEpoch) : null;
}
