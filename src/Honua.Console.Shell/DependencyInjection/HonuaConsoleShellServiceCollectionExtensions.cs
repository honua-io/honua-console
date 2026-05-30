using System.Net.Http;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.Shell.DependencyInjection;

public static class HonuaConsoleShellServiceCollectionExtensions
{
    public static IServiceCollection AddHonuaConsoleShell(
        this IServiceCollection services,
        string? honuaServerBaseUrl = null,
        string? honuaServerAdminApiKey = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IConsoleHostCapabilities, BrowserConsoleHostCapabilities>();
        services.TryAddSingleton<IConsoleEnvironmentProfileStore>(
            _ => InMemoryConsoleEnvironmentProfileStore.CreateSeeded());
        services.TryAddSingleton<IConsoleAccountSessionStore, InMemoryConsoleAccountSessionStore>();
        AddStudioAuthoringShell(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioAppPackageDataSource(services, honuaServerBaseUrl);
        AddStudioFormPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioQueryPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioMapPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioAnalysisPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioDashboardPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioReportPublicationDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioWorkflowPackageClient(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddShareAccessDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddOperateTransitionDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddTemporalCapabilityClient(services);
        AddPublishingWorkspaceDataSource(services);
        AddConsoleCatalogClient(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        services.TryAddScoped<IConsoleCatalogReadContextResolver, ConsoleCatalogReadContextResolver>();

        // Operate observability binds to a real honua-server through a thin
        // typed HttpClient behind Honua.Console.Contracts (the sanctioned interim
        // until honua-sdk-dotnet projects the Operate contracts). Server-owned
        // telemetry/events/alerts/rules/jobs/investigations are no longer wired
        // to OperateObservabilityFixture.Default at runtime. TryAdd keeps an
        // explicit test/demo provider overridable.
        services.TryAddSingleton<IConsoleOperateObservabilityClient>(serviceProvider =>
            new HttpConsoleOperateObservabilityClient(
                CreateOperateObservabilityHttpClient(),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>()));

        // GitOps metadata release visualization binds to a real honua-server
        // (honua-server#1163/#1164) through the shim boundary; no standing in-memory
        // release data source is registered (Console Patterns Charter section 11).
        services.TryAddSingleton<IConsoleGitOpsReleaseClient>(serviceProvider =>
            new HttpConsoleGitOpsReleaseClient(
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>()));

        return services;
    }

    public static IServiceCollection AddHonuaConsoleDemoOperateTransitionData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Singleton<IOperateTransitionDataSource>(
            _ => InMemoryOperateTransitionDataSource.CreateSeeded()));

        return services;
    }

    /// <summary>
    /// Swaps the catalog/content client for the local in-memory seeded source. For explicit demo/local
    /// composition or unit tests only — never the merged runtime path for server-owned catalog/content
    /// metadata (issue #7). The merged runtime renders a missing-binding state until honua-server's
    /// metadata/content projection is bound (honua-server#1162).
    /// </summary>
    public static IServiceCollection AddHonuaConsoleDemoCatalogContent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Singleton<IConsoleCatalogClient, InMemoryConsoleCatalogClient>());

        return services;
    }

    /// <summary>
    /// Swaps the Studio authoring shell for the local in-memory simulator. For explicit demo/local
    /// composition only — never the merged runtime path for server-owned Studio package data.
    /// </summary>
    public static IServiceCollection AddHonuaConsoleDemoStudioAuthoringShell(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Singleton<IStudioAuthoringShell, InMemoryStudioAuthoringShell>());

        return services;
    }

    /// <summary>
    /// Swaps the Studio workflow package client for the local in-memory seeded simulator. For explicit
    /// demo/local composition only — never the merged runtime path for server-owned workflow package data
    /// (#1185). Mirrors <see cref="AddHonuaConsoleDemoStudioAuthoringShell"/>.
    /// </summary>
    public static IServiceCollection AddHonuaConsoleDemoStudioWorkflowPackages(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Singleton<IStudioWorkflowPackageClient>(
            _ => InMemoryStudioWorkflowPackageClient.CreateSeeded()));

        return services;
    }

    // Binds the Studio package draft/lifecycle/validation/preview-plan surface to honua-server
    // (#1180/#1181) through the Honua.Console.Contracts shim when a server base address is configured;
    // otherwise the shell renders a missing-binding state (never mock package data).
    private static void AddStudioAuthoringShell(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IStudioPackageLifecycleClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HttpStudioPackageLifecycleClient(
                    httpClient,
                    new StudioPackageLifecycleClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioAuthoringShell, ServerStudioAuthoringShell>();
            return;
        }

        services.TryAddSingleton<IStudioAuthoringShell, UnsupportedStudioAuthoringShell>();
    }

    // Binds the Studio app-builder surface (/studio/app, #58) to honua-server's Studio package
    // lifecycle + app publication registry (#1180/#1181/#1183) when a server base address is
    // configured, reusing the IStudioPackageLifecycleClient already registered by
    // AddStudioAuthoringShell; otherwise the builder renders a missing-binding state (never mock app
    // data).
    private static void AddStudioAppPackageDataSource(IServiceCollection services, string? honuaServerBaseUrl)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IStudioAppPackageDataSource, HonuaServerStudioAppPackageDataSource>();
            return;
        }

        services.TryAddSingleton<IStudioAppPackageDataSource, UnsupportedStudioAppPackageDataSource>();
    }

    // Binds the Studio form-builder surface (/studio/form) to honua-server's form package lifecycle
    // (#1184) through the Honua.Console.Contracts shim when a server base address is configured;
    // otherwise the builder renders a missing-binding state (never mock form data).
    private static void AddStudioFormPackageDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IHonuaFormPackageClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HonuaFormPackageHttpClient(
                    httpClient,
                    new HonuaFormPackageClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioFormPackageDataSource, HonuaServerStudioFormPackageDataSource>();
            return;
        }

        services.TryAddSingleton<IStudioFormPackageDataSource, UnsupportedStudioFormPackageDataSource>();
    }

    // Binds the Studio query-builder surface (/studio/query) to honua-server's saved query content
    // lifecycle (honua-server#1182) when a server base address is configured; otherwise the builder
    // renders a missing-binding state (never mock query data, per Console Patterns Charter section 11).
    // The server-bound data source ships once honua-server#1182's wire shape is projected into the
    // Honua.Console.Contracts shim; until then both branches resolve the missing-binding source so the
    // surface stays blocked rather than fabricating query packages.
    private static void AddStudioQueryPackageDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        _ = honuaServerBaseUrl;
        _ = honuaServerAdminApiKey;
        services.TryAddSingleton<IStudioQueryPackageDataSource, UnsupportedStudioQueryPackageDataSource>();
    }

    // Binds the Studio map-builder surface (/studio/map) to honua-server's map package lifecycle
    // (#1180, closed) and publication registry (#1183, closed) through the Honua.Console.Contracts shim.
    // The server-bound HTTP data source lands with the publication-wiring slice; until then no live map
    // client resolves, so the builder renders an explicit missing-binding state (never mock map data —
    // Console Patterns Charter section 11). The unsupported source is registered with TryAdd so the
    // server-bound implementation can replace it without churn in the next slice and so tests/demo
    // composition can override it.
    private static void AddStudioMapPackageDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        _ = honuaServerBaseUrl;
        _ = honuaServerAdminApiKey;
        services.TryAddSingleton<IStudioMapPackageDataSource, UnsupportedStudioMapPackageDataSource>();
    }

    // Binds the Studio analysis-builder surface (/studio/analysis, honua-console#53) to honua-server's
    // analysis content/artifacts contract (#1182) and the closed execution engine (#681/#721/#724) through
    // the Honua.Console.Contracts shim when a server base address is configured; otherwise the builder
    // renders a missing-binding state (never mock analysis data, Console Patterns Charter section 11).
    private static void AddStudioAnalysisPackageDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IHonuaAnalysisContentClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HonuaAnalysisContentHttpClient(
                    httpClient,
                    new HonuaAnalysisContentClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioAnalysisPackageDataSource, HonuaServerStudioAnalysisContentDataSource>();
            return;
        }

        services.TryAddSingleton<IStudioAnalysisPackageDataSource, UnsupportedStudioAnalysisPackageDataSource>();
    }

    // Binds the Studio dashboard-builder surface (/studio/dashboard) to honua-server's dashboard package
    // lifecycle on the publication registry (#1183). The live HttpClient binding behind the
    // Honua.Console.Contracts shim follows in a subsequent slice once the dashboard publication wire
    // contract is projected into Console; until then this registers the missing-binding data source so the
    // builder renders an explicit not-bound surface (never mock dashboard data — Console Patterns Charter
    // section 11). TryAdd keeps an explicit test/demo provider overridable.
    private static void AddStudioDashboardPackageDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        _ = honuaServerBaseUrl;
        _ = honuaServerAdminApiKey;

        services.TryAddSingleton<IStudioDashboardPackageDataSource, UnsupportedStudioDashboardPackageDataSource>();
    }

    // Binds the Studio report-builder surface (/studio/report) read path to honua-server's content
    // publication registry (#1183) through the Honua.Console.Contracts shim when a server base address
    // is configured; otherwise the builder renders a missing-binding state (never mock publication data).
    private static void AddStudioReportPublicationDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IHonuaContentPublicationClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HonuaContentPublicationHttpClient(
                    httpClient,
                    new HonuaContentPublicationClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioReportPublicationDataSource, HonuaServerStudioReportPublicationDataSource>();
            return;
        }

        services.TryAddSingleton<IStudioReportPublicationDataSource, UnsupportedStudioReportPublicationDataSource>();
    }

    // Binds the Studio GP/ETL workflow editor (node registry, package drafts, versions, dry-run, publish)
    // to honua-server (#1185) through the Honua.Console.Contracts shim when a server base address is
    // configured; otherwise the editor renders a missing-binding state (never seeded workflow data).
    private static void AddStudioWorkflowPackageClient(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IWorkflowPackageApiClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HttpWorkflowPackageApiClient(
                    httpClient,
                    new WorkflowPackageClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioWorkflowPackageClient, ServerStudioWorkflowPackageClient>();
            return;
        }

        services.TryAddSingleton<IStudioWorkflowPackageClient, UnsupportedStudioWorkflowPackageClient>();
    }

    // Binds the server-owned catalog/content metadata surfaces (Catalog list/search/detail, Share map
    // package + embed reads, Studio draft hydration, open-data reads) to honua-server's Console metadata
    // v2 content + RBAC API (honua-server#1162, CLOSED) through the Honua.Console.Contracts shim when a
    // server base address is configured. Server-authored RBAC verbs drive the resolved role and viewer
    // support, so route/item actions reflect entitlement checks. With no server configured the shell
    // keeps the UnsupportedConsoleCatalogClient missing-binding state across Catalog/Studio/Share/Operate
    // reads instead of fabricating content (issue #7, Console Patterns Charter section 11). The seeded
    // InMemoryConsoleCatalogClient is never the runtime source; AddHonuaConsoleDemoCatalogContent restores
    // it for explicit demo/local composition only.
    private static void AddConsoleCatalogClient(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IHonuaConsoleContentClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HonuaConsoleContentHttpClient(
                    httpClient,
                    new HonuaConsoleContentClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IConsoleCatalogClient, HonuaServerConsoleCatalogClient>();
            return;
        }

        services.TryAddSingleton<IConsoleCatalogClient, UnsupportedConsoleCatalogClient>();
    }

    // Binds the Share management surface (/share/manage, honua-console#35) to honua-server's Console Share
    // access API (honua-server#1215: share projection read, access-tier change, dependency-closure preview,
    // public-link mint/revoke, embed enablement/token mint) through the Honua.Console.Contracts shim when a
    // server base address is configured; otherwise the surface renders a missing-binding state (never mock
    // share data, Console Patterns Charter section 11).
    private static void AddShareAccessDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IHonuaConsoleShareClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HonuaConsoleShareHttpClient(
                    httpClient,
                    new HonuaConsoleShareClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IShareAccessDataSource, HonuaServerShareAccessDataSource>();
            return;
        }

        services.TryAddSingleton<IShareAccessDataSource, UnsupportedShareAccessDataSource>();
    }

    private static void AddOperateTransitionDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IHonuaAdminOperateClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HonuaAdminOperateHttpClient(
                    httpClient,
                    new HonuaAdminOperateClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IOperateTransitionDataSource, HonuaServerOperateTransitionDataSource>();
            return;
        }

        services.TryAddSingleton<IOperateTransitionDataSource, UnsupportedOperateTransitionDataSource>();
    }

    // Binds the temporal data viewer + disconnected sync conflict review surface (/operate/temporal)
    // to honua-server's temporal data history API (#1166: as-of query, diff, attribution, rollback) and
    // the disconnected replica conflict review API (#1167: named replica metadata + conflict reads /
    // resolution writes). Both server contracts are still open/unlanded, so the merged build registers
    // only the missing-binding client: every temporal operation (capabilities, checkpoints, diff, feature
    // timeline, rollback plan/execute, replica queue/review, conflict resolution) returns a
    // missing-binding state, the viewer renders an explicit capability explanation, and Console never
    // fabricates temporal history or sync conflicts from a standing mock (Console Patterns Charter
    // section 11). When #1166/#1167 land, wire the live HTTP-bound client here behind the
    // Honua.Console.Contracts shim, gated on a configured server base URL exactly like the Studio /
    // Operate bindings above; the page and tests already consume the full ITemporalCapabilityClient.
    private static void AddTemporalCapabilityClient(IServiceCollection services) =>
        services.TryAddSingleton<ITemporalCapabilityClient, UnsupportedTemporalCapabilityClient>();

    // Registers the Operate publishing workspace data source (/operate/publishing). The merged build
    // binds the publication matrix and review surface to honua-server (service/layer publish today,
    // the full publication registry behind honua-server#1183) via honua-sdk-dotnet or the
    // Honua.Console.Contracts shim once those projections land; until then this stays the
    // missing-binding source so the workspace renders an explicit unbound state rather than mock data
    // (Console Patterns Charter section 11 — no standing in-memory publishing data source).
    private static void AddPublishingWorkspaceDataSource(IServiceCollection services)
    {
        services.TryAddSingleton<IPublishingWorkspaceDataSource, UnsupportedPublishingWorkspaceDataSource>();
    }

    private static HttpClient CreateOperateObservabilityHttpClient() =>
        new(new SocketsHttpHandler
        {
            // Refresh pooled connections so a long-lived singleton client does
            // not pin stale DNS for the active environment's server.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
}
