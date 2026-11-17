using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class MapNavigationTests
{
    public enum MapRoute { Draft, Saved }

    public static IEnumerable<object[]> Routes =>
        from route in Enum.GetValues<MapRoute>()
        from presentation in Enum.GetValues<ConsolePresentationMode>()
        select new object[] { route, presentation };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task PendingNavigationRemovesPreviousMap(MapRoute route, ConsolePresentationMode presentation)
    {
        using var view = new MapView(route, presentation);
        await view.Authorize();
        await view.Complete("a");
        await view.Navigate("b");
        Assert.Empty(view.Surfaces);
        view.AssertCheckingAccess();
        await view.Authorize();
        await view.Complete("b");
        view.AssertMap("b");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task OldMapCannotReplaceCurrentMap(MapRoute route, ConsolePresentationMode presentation)
    {
        using var view = new MapView(route, presentation);
        await view.Authorize();
        await view.Navigate("b");
        await view.Authorize();
        await view.Complete("b");
        await view.Complete("a");
        view.AssertMap("b");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task OldDenialCannotHideCurrentMap(MapRoute route, ConsolePresentationMode presentation)
    {
        using var view = new MapView(route, presentation);
        await view.Authorize();
        await view.Navigate("b");
        await view.Authorize();
        await view.Complete("b");
        await view.CompleteDenied("a");
        view.AssertMap("b");
        Assert.DoesNotContain("You do not have access", view.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task OldMapCannotReleaseCurrentLoadingState(MapRoute route, ConsolePresentationMode presentation)
    {
        using var view = new MapView(route, presentation);
        await view.Authorize();
        await view.Navigate("b");
        await view.Authorize();
        await view.Complete("a");
        Assert.Empty(view.Surfaces);
        await view.Complete("b");
        view.AssertMap("b");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ReturningToMapUsesNewVisit(MapRoute route, ConsolePresentationMode presentation)
    {
        using var view = new MapView(route, presentation);
        await view.Authorize();
        await view.Navigate("b");
        await view.Authorize();
        await view.Navigate("a");
        await view.Authorize();
        await view.Complete("a", 1);
        await view.Complete("b");
        await view.Complete("a");
        view.AssertMap("a visit 1");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task OldAccessCannotStartReadForNewRoute(MapRoute route, ConsolePresentationMode presentation)
    {
        using var view = new MapView(route, presentation);
        await view.Navigate("b");
        await view.Authorize(0);
        Assert.Empty(view.Data.Reads);
        await view.Authorize(1);
        Assert.Equal("b", Assert.Single(view.Data.Reads).Id);
        await view.Complete("b");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task OldAccessCannotReleaseCurrentAccessCheck(MapRoute route, ConsolePresentationMode presentation)
    {
        using var view = new MapView(route, presentation);
        await view.Navigate("b");
        await view.Authorize(0);
        Assert.Empty(view.Surfaces);
        view.AssertCheckingAccess();
        await view.Authorize(1);
        await view.Complete("b");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task DisposedAccessCannotStartMapRead(MapRoute route, ConsolePresentationMode presentation)
    {
        using var view = new MapView(route, presentation);
        view.DisposePage();
        await view.Authorize();
        Assert.Empty(view.Data.Reads);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task ClearingDraftSourceInvalidatesPendingMap(ConsolePresentationMode presentation)
    {
        using var view = new MapView(MapRoute.Draft, presentation);
        await view.Authorize();
        await view.Navigate(string.Empty);
        await view.Authorize();
        await view.Complete("a");
        Assert.Empty(view.Surfaces);
        Assert.Contains("Choose a catalog item", view.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task AnonymousDraftNeverHydrates(ConsolePresentationMode presentation)
    {
        using var view = new MapView(MapRoute.Draft, presentation);
        await view.AuthorizeAnonymous();
        Assert.Empty(view.Data.Reads);
        Assert.Contains("Sign in to create a map", view.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task ChangedPublicLinkCannotReuseOldAccess(ConsolePresentationMode presentation)
    {
        using var view = new MapView(MapRoute.Saved, presentation);
        await view.Navigate("a", "new-link");
        await view.AuthorizeAnonymous(1);
        await view.Complete("a");
        await view.AuthorizeAnonymous(0);
        Assert.Single(view.Data.Reads);
        Assert.Equal("new-link", view.Data.Reads[0].Context.PublicLinkToken);
        view.AssertMap("a");
    }

    private sealed class MapView : IDisposable
    {
        private readonly BunitContext _context = new();
        private readonly MapRoute _route;
        private readonly MountedNavigationManager _navigation = new("/console/", "maps/new?from=a");
        private readonly Access _access = new();
        private readonly IRenderedComponent<MapDraftPage>? _draft;
        private readonly IRenderedComponent<MapViewerPage>? _saved;

        public MapView(MapRoute route, ConsolePresentationMode presentation)
        {
            _route = route;
            _navigation.NavigateTo(Url("a", "old-link"));
            _context.Services.AddSingleton<NavigationManager>(_navigation);
            _context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
            _context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
            _context.Services.AddSingleton<IConsoleCatalogClient>(Data);
            _context.Services.AddSingleton<IConsoleCatalogReadContextResolver>(_access);
            if (route == MapRoute.Draft)
            {
                _draft = _context.Render<MapDraftPage>();
            }
            else
            {
                _saved = _context.Render<MapViewerPage>(p => p.Add(x => x.MapId, "a"));
            }
        }

        public Catalog Data { get; } = new();
        public string Markup => _draft?.Markup ?? _saved!.Markup;
        public AngleSharp.Dom.IElement[] Surfaces => (_draft?.FindAll(".console-map-surface") ?? _saved!.FindAll(".console-map-surface")).ToArray();

        public async Task Navigate(string id, string token = "old-link")
        {
            await _context.Renderer.Dispatcher.InvokeAsync(() => _navigation.NavigateTo(Url(id, token)));
            if (_draft is not null)
            {
                _draft.Render();
            }
            else
            {
                _saved!.Render(p => p.Add(x => x.MapId, id));
            }
        }

        public Task Authorize(int index = -1) => CompleteAccess(CatalogReadContext.Authenticated, index);
        public Task AuthorizeAnonymous(int index = -1)
        {
            var read = _access.Reads[index < 0 ? _access.Reads.Count - 1 : index];
            return CompleteAccess(CatalogReadContext.AnonymousPublicLink(read.Token), index);
        }

        private Task CompleteAccess(CatalogReadContext context, int index) =>
            _context.Renderer.Dispatcher.InvokeAsync(() => _access.Reads[index < 0 ? _access.Reads.Count - 1 : index].Completion.SetResult(context));

        public Task Complete(string id, int visit = 0) => Finish(id, MapReadResult.Allowed(new ConsoleMap
        {
            Summary = new ConsoleContentSummary { Id = id, Title = visit == 0 ? id : $"{id} visit {visit}", Type = "map" }
        }), visit);

        public Task CompleteDenied(string id) => Finish(id, MapReadResult.Denied(CatalogReadStatus.Forbidden, "Denied"), 0);

        private Task Finish(string id, MapReadResult result, int visit) =>
            _context.Renderer.Dispatcher.InvokeAsync(() => Data.Reads.Where(x => x.Id == id).ElementAt(visit).Completion.SetResult(result));

        public void AssertCheckingAccess() => Assert.Contains(_route == MapRoute.Draft ? "Checking map access" : "Loading map", Markup, StringComparison.Ordinal);

        public void AssertMap(string title) => Assert.Contains(title, Assert.Single(Surfaces).TextContent, StringComparison.Ordinal);

        private string Url(string id, string token) => _route == MapRoute.Draft
            ? $"/console/maps/new?from={Uri.EscapeDataString(id)}"
            : $"/console/maps/{Uri.EscapeDataString(id)}?token={Uri.EscapeDataString(token)}";

        public void DisposePage()
        {
            _draft?.Dispose();
            _saved?.Dispose();
        }

        public void Dispose() => _context.Dispose();
    }

    private sealed class Access : IConsoleCatalogReadContextResolver
    {
        public List<(string? Token, TaskCompletionSource<CatalogReadContext> Completion)> Reads { get; } = [];

        public Task<CatalogReadContext> ResolveAsync(string? publicLinkToken, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<CatalogReadContext>();
            Reads.Add((publicLinkToken, completion));
            return completion.Task;
        }
    }

    private sealed class Catalog : IConsoleCatalogClient
    {
        public List<(string Id, CatalogReadContext Context, TaskCompletionSource<MapReadResult> Completion)> Reads { get; } = [];

        private Task<MapReadResult> Read(string id, CatalogReadContext context)
        {
            var completion = new TaskCompletionSource<MapReadResult>();
            Reads.Add((id, context, completion));
            return completion.Task;
        }

        public Task<MapReadResult> GetMapAsync(string mapId, CatalogReadContext context, CancellationToken cancellationToken = default) => Read(mapId, context);
        public Task<MapReadResult> GetDraftMapAsync(string sourceItemId, CatalogReadContext context, CancellationToken cancellationToken = default) => Read(sourceItemId, context);
        public Task<CatalogSearchResult> SearchAsync(CatalogListRequest request, CatalogReadContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CatalogItemReadResult> GetCatalogItemAsync(string idOrSlug, CatalogReadContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CatalogItemReadResult> GetOpenDataItemAsync(string idOrSlug, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MapReadResult> AuthorizeEmbedAsync(string mapId, EmbedRouteOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
