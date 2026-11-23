using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.IntegrationTests;

internal static class ConsolePublicationRenderFixture
{
    internal static IRenderedComponent<ConsoleLayout> RenderLayerLayout(BunitContext context) =>
        context.Render<ConsoleLayout>(parameters => parameters.Add(component => component.Body, builder =>
        {
            var navigation = context.Services.GetRequiredService<NavigationManager>();
            var path = ConsoleNavigation.ContextForPath(navigation.ToBaseRelativePath(navigation.Uri), ConsoleNavigation.Groups);
            if (path.Kind != ConsoleNavigationContextKind.Layer) return;
            var suffix = navigation.ToBaseRelativePath(navigation.Uri).Split('/').ElementAtOrDefault(3);
            var pageType = suffix switch
            {
                "metadata" => typeof(OperateLayerMetadataPage),
                "filter" => typeof(OperateLayerFilterPage),
                "relationships" => typeof(OperateLayerRelationshipsPage),
                "style" => typeof(OperateLayerStylePage),
                _ => typeof(OperateLayerDetailPage)
            };
            builder.OpenComponent(0, pageType);
            builder.AddComponentParameter(1, "ResourceId", Uri.UnescapeDataString(path.Path["/operate/layers/".Length..]));
            builder.CloseComponent();
        }));

    private sealed class LayerPageDataSource(PublicationNavigationSource source) : IOperateTransitionDataSource
    {
        public async Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
        {
            source.LayerPageReads++;
            var read = await (source.PendingLayers?.Task ?? Task.FromResult(source.LayerResult));
            var name = source.ServiceSummaries?.LastOrDefault(service => service.LayerCount > 0)?.ServiceName ?? "service-000";
            var layers = read.IsAllowed ? read.Value!.Select(layer => new OperateServiceLayer(
                7, layer.Name, layer.GeometryType, layer.ResourceId, layer.Name)).ToArray() : [];
            return new([], [], [new(name!, name!, "FeatureServer", "Running", "server", layers, [], [])], [], []);
        }
        public Task<OperateConnectionSummary?> FindConnectionAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<OperateServiceDetail?> FindServiceAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class LayerStyleDataSource(PublicationNavigationSource source) : IOperateLayerStyleOverrideDataSource
    {
        public async Task<OperateLayerStyleOverrideView> GetOverridesAsync(string resourceId, CancellationToken cancellationToken = default)
        {
            source.LayerPageReads++;
            var read = await (source.PendingLayers?.Task ?? Task.FromResult(source.LayerResult));
            var name = source.ServiceSummaries?.LastOrDefault(service => service.LayerCount > 0)?.ServiceName ?? "service-000";
            var layers = read.Value?.Select(layer => new OperateLayerStyleOverride("7", name!, layer.Name)).ToArray() ?? [];
            return new(resourceId, layers, source.StyleBindingState ?? (read.IsAllowed ? null :
                new("Layer styles", OperateLayerStyleBindingState.Forbidden, "layer styles", "Denied")));
        }

        public Task<OperateLayerStyleOverrideSaveResult> SaveOverrideAsync(OperateLayerStyleOverrideEdit edit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    internal static BunitContext PublicationLayoutContext(PublicationNavigationSource source)
    {
        var context = new BunitContext();
        context.AddAuthorization().SetAuthorized("synthetic-operator");
        ConfigurePublicationLayout(context, source);
        return context;
    }

    internal static void ConfigurePublicationLayout(BunitContext context, PublicationNavigationSource source)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.AddConsoleNotifications();
        context.Services.AddSingleton(new CatalogAdminOptions());
        context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        context.Services.AddSingleton<IConsoleProductMode>(new ConfiguredConsoleProductMode(ConsoleProductMode.Full));
        context.Services.AddSingleton<IConsoleCapabilityManifest>(ConsoleCapabilityManifest.FromConfigurationList(null));
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        context.Services.AddSingleton<IOperateTransitionDataSource>(new LayerPageDataSource(source));
        context.Services.AddSingleton<IConsoleLayerFieldsOperation>(new UnsupportedConsoleLayerFieldsOperation());
        context.Services.AddSingleton<IConsoleLayerMetadataOperation, UnsupportedConsoleLayerMetadataOperation>();
        context.Services.AddSingleton<IConsoleLayerFilterOperation, UnsupportedConsoleLayerFilterOperation>();
        context.Services.AddSingleton<IConsoleLayerRelationshipsOperation, UnsupportedConsoleLayerRelationshipsOperation>();
        context.Services.AddSingleton<IOperateLayerStyleOverrideDataSource>(new LayerStyleDataSource(source));
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();
    }

    internal static ConsoleEnvironmentProfile ChangedProfile(ConsoleEnvironmentProfile profile, string field) => field switch
    {
        "profile" => profile with { Id = "second" },
        "server" => profile with { ServerBaseUri = new Uri("https://second.example.test") },
        "account" => profile with { Account = profile.Account with { AccountId = "second-synthetic-account" } },
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

}

internal sealed class PublicationNavigationSource : IConsolePublicationNavigationSource
{
    public bool RefuseLayerReads { get; init; }
    public bool IgnoreNameQuery { get; init; }
    public IReadOnlyList<Honua.Sdk.Admin.Models.ServiceSummary>? ServiceSummaries { get; init; }
    public OperateSectionResult<HonuaAdminServicePage>? FailNext { get; set; }
    public int HoldCount { get; set; }
    public int ServiceReads { get; private set; }
    public Action? OnServiceRead { get; set; }
    private readonly Queue<TaskCompletionSource<OperateSectionResult<HonuaAdminServicePage>?>> _holds = new();
    public HonuaAdminCatalogQuery LastQuery { get; private set; }
    public TaskCompletionSource<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>>? PendingLayers { get; set; }
    public IReadOnlyList<string> ConnectionKeys { get; set; } = ["connection-one"];
    public List<IReadOnlyList<string>> RequestedConnectionSets { get; } = [];
    public int LayerPageReads { get; set; }
    public OperateLayerStyleBindingState? StyleBindingState { get; set; }
    public int ChildReads { get; private set; }
    public int ConnectionReads { get; private set; }
    public List<string> RequestedServices { get; } = [];
    public OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>> LayerResult { get; set; } =
        OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>.Allowed([new("connection-one-layer-7", "Bridges", "LineString")]);
    public async Task<OperateSectionResult<HonuaAdminServicePage>> ReadServicesAsync(
        HonuaAdminCatalogQuery query, CancellationToken cancellationToken)
    {
        ServiceReads++;
        OnServiceRead?.Invoke();
        LastQuery = query;
        if (HoldCount > 0)
        {
            HoldCount--;
            var gate = new TaskCompletionSource<OperateSectionResult<HonuaAdminServicePage>?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _holds.Enqueue(gate);
            var held = await gate.Task;
            if (held is { } heldResult) return heldResult;
        }
        if (FailNext is { } failed)
        {
            FailNext = null;
            return failed;
        }

        var services = ServiceSummaries?.ToArray() ?? Enumerable.Range(0, 500)
            .Select(index => new Honua.Sdk.Admin.Models.ServiceSummary { ServiceName = $"service-{index:D3}", LayerCount = 10 })
            .ToArray();
        if (!IgnoreNameQuery && !string.IsNullOrWhiteSpace(query.NameQuery))
        {
            services = services.Where(service =>
                service.ServiceName?.Contains(query.NameQuery, StringComparison.OrdinalIgnoreCase) == true
                || service.Title?.Contains(query.NameQuery, StringComparison.OrdinalIgnoreCase) == true
                || service.PublicName?.Contains(query.NameQuery, StringComparison.OrdinalIgnoreCase) == true).ToArray();
        }

        var pageSize = query.PageSize <= 0 ? HonuaAdminCatalogQuery.MaxPageSize : query.PageSize;
        var start = Math.Max(0, query.Page - 1) * pageSize;
        var pageItems = services.Skip(start).Take(pageSize).ToArray();
        return OperateSectionResult<HonuaAdminServicePage>.Allowed(new HonuaAdminServicePage
        {
            Items = pageItems,
            Page = query.Page,
            PageSize = pageSize,
            Total = services.Length,
            LayerTotal = services.Sum(service => service.LayerCount)
        });
    }

    public void ReleaseNext(OperateSectionResult<HonuaAdminServicePage>? result = null) =>
        _holds.Dequeue().SetResult(result);
    public TaskCompletionSource<OperateSectionResult<IReadOnlyList<string>>>? HeldConnections { get; set; }
    public Task<OperateSectionResult<IReadOnlyList<string>>> ReadConnectionsAsync(CancellationToken cancellationToken)
    {
        ConnectionReads++;
        return HeldConnections?.Task ?? Task.FromResult(OperateSectionResult<IReadOnlyList<string>>.Allowed(ConnectionKeys));
    }
    public IReadOnlyList<ConsolePublicationLayerMatch> LayerMatches { get; init; } = [];
    public OperateSectionResult<IReadOnlyList<ConsolePublicationLayerMatch>>? LayerSearchResult { get; init; }
    public List<string> LayerQueries { get; } = [];
    public bool HoldLayerSearches { get; init; }
    public List<TaskCompletionSource<OperateSectionResult<IReadOnlyList<ConsolePublicationLayerMatch>>>> HeldLayerSearches { get; } = [];
    public List<IReadOnlyList<string>> LayerSearchConnections { get; } = [];
    public Task<OperateSectionResult<IReadOnlyList<ConsolePublicationLayerMatch>>> SearchLayersAsync(
        string nameQuery, IReadOnlyList<string> connectionKeys, CancellationToken cancellationToken)
    {
        LayerQueries.Add(nameQuery);
        LayerSearchConnections.Add(connectionKeys.ToArray());
        if (HoldLayerSearches)
        {
            var gate = new TaskCompletionSource<OperateSectionResult<IReadOnlyList<ConsolePublicationLayerMatch>>>(TaskCreationOptions.RunContinuationsAsynchronously);
            HeldLayerSearches.Add(gate);
            return gate.Task;
        }
        return Task.FromResult(LayerSearchResult ?? OperateSectionResult<IReadOnlyList<ConsolePublicationLayerMatch>>.Allowed(
            LayerMatches.Where(match => match.Layer.Name.Contains(nameQuery, StringComparison.OrdinalIgnoreCase)).ToArray()));
    }
    public Task<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>> ReadLayersAsync(
        string serviceName, IReadOnlyList<string> connectionKeys, CancellationToken cancellationToken)
    {
        if (RefuseLayerReads) throw new InvalidOperationException("Ancestry must reuse the page read.");
        ChildReads++;
        RequestedServices.Add(serviceName);
        RequestedConnectionSets.Add(connectionKeys.ToArray());
        return PendingLayers?.Task ?? Task.FromResult(LayerResult);
    }
}
