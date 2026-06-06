using System.Net.Http;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;
using Honua.Console.Shell.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.Shell.DependencyInjection;

public static class HonuaConsoleShellServiceCollectionExtensions
{
    public static IServiceCollection AddHonuaConsoleShell(
        this IServiceCollection services,
        string? honuaServerBaseUrl = null,
        string? honuaServerAdminApiKey = null,
        string? honuaServerPublicationIds = null,
        string? honuaServerTemporalSources = null,
        string? honuaSupportBaseUrl = null,
        string? honuaLlmBaseUrl = null,
        string? honuaLlmModel = null,
        string? honuaLlmApiKey = null,
        string? honuaSupportKbPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IConsoleHostCapabilities, BrowserConsoleHostCapabilities>();
        // Environment profiles are host-owned local state (Console Patterns Charter §11 local-state
        // carve-out), but they must not ship fabricated demo profiles. The default store starts EMPTY so
        // the first-run experience is "create your first environment", never seeded dev.honua.local /
        // staging.honua.example fakes. The native host replaces this with the persistent
        // JsonConsoleEnvironmentProfileStore (also empty until the operator adds one). The seeded factory
        // (InMemoryConsoleEnvironmentProfileStore.CreateSeeded) stays test/demo-only — never the DI default.
        services.TryAddSingleton<IConsoleEnvironmentProfileStore>(
            _ => new InMemoryConsoleEnvironmentProfileStore([]));
        services.TryAddSingleton<IConsoleAccountSessionStore, InMemoryConsoleAccountSessionStore>();

        // Unsaved-changes dirty tracking (FormDirtyState) is intentionally NOT registered in DI: a
        // Scoped service lives for the entire Blazor Server circuit, so every editor would share one
        // flag. Each editor instead owns a private FormDirtyState instance and passes its IsDirty to
        // the <UnsavedChangesGuard/>. Editors are wired in the per-surface waves.

        AddStudioAuthoringShell(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioAppPackageDataSource(services, honuaServerBaseUrl);
        AddStudioFormPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioQueryPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioMapPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioMapCollaborationDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioAnalysisPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioDashboardPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioReportPublicationDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioWorkflowPackageClient(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddShareAccessDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddRbacAccessDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddCatalogDiscoveryDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddOperateTransitionDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddTemporalCapabilityClient(services, honuaServerBaseUrl, honuaServerAdminApiKey, honuaServerTemporalSources);
        AddEsriMigrationRunDataSource(services);
        AddPublishingWorkspaceDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey, honuaServerPublicationIds);
        AddConsoleCatalogClient(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddOperateAlertRulesDataSource(services, honuaServerBaseUrl);
        AddOperateLayerStyleOverrideDataSource(services);
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

        // GitOps metadata release visualization binds to a real honua-server through
        // the SHIPPED GitOps metadata release contracts (honua-server#1163 release
        // package + GitOps manifest, honua-server#1165 release-operation lifecycle /
        // rollback) via a thin typed HttpClient behind Honua.Console.Contracts. No
        // standing in-memory release data source is registered (Console Patterns
        // Charter section 11); the by-id detail read activates against live data when
        // an environment is connected, else the surface renders the missing-binding
        // state. The admin API key is sent as X-API-Key (admin-authorized endpoints).
        services.TryAddSingleton<IConsoleGitOpsReleaseClient>(serviceProvider =>
            new HttpConsoleGitOpsReleaseClient(
                CreateOperateObservabilityHttpClient(),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                honuaServerAdminApiKey));

        // In-product support loop (#164). The ticket client binds to the
        // honua-support API (POST/GET /api/v1/tickets) through the
        // Honua.Console.Contracts shim, with the bearer token from the active
        // profile's account session. When no support base URL is configured the
        // surface renders the missing-binding state (Console Patterns Charter
        // section 11) rather than mocking a ticket round-trip. The context
        // builder auto-captures session/env/build/route/recent-errors so
        // customers ship context, not log dumps.
        AddSupportTicketClient(services, honuaSupportBaseUrl);
        services.TryAddSingleton<ConsoleSupportContextBuilder>(serviceProvider =>
            new ConsoleSupportContextBuilder(
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                serviceProvider.GetRequiredService<IConsoleOperateObservabilityClient>()));

        // L0 self-service deflection (honua-console#165), shown BEFORE the ticket
        // form on /support. Three self-contained pieces:
        //   - the qwen assistant client, bound to the SAME OpenAI-compatible
        //     /v1/chat/completions endpoint honua-support's L1 triage uses
        //     (Honua:Llm:BaseUrl + model/key); degrades to the unsupported
        //     client (assistant hidden) when no endpoint is configured;
        //   - the lexical KB retriever over the bundled honua-gis-llm support KB
        //     (optional Honua:Support:KbPath override); empty when absent;
        //   - the deflection log (structured ILogger events) so the deflection
        //     rate is measurable.
        AddSupportAssistantClient(services, honuaLlmBaseUrl, honuaLlmModel, honuaLlmApiKey);
        services.TryAddSingleton<IConsoleSupportKnowledgeBase>(
            _ => FileConsoleSupportKnowledgeBase.Load(honuaSupportKbPath));
        services.TryAddSingleton<IConsoleSupportDeflectionLog, ConsoleSupportDeflectionLog>();

        return services;
    }

    private static void AddSupportAssistantClient(
        IServiceCollection services,
        string? honuaLlmBaseUrl,
        string? honuaLlmModel,
        string? honuaLlmApiKey)
    {
        // Require both an absolute endpoint and a model id; missing either means
        // the assistant gracefully degrades (the /support page hides it and keeps
        // KB + form).
        if (!string.IsNullOrWhiteSpace(honuaLlmBaseUrl)
            && !string.IsNullOrWhiteSpace(honuaLlmModel)
            && Uri.TryCreate(honuaLlmBaseUrl.Trim(), UriKind.Absolute, out var llmBaseUri)
            && (llmBaseUri.Scheme == Uri.UriSchemeHttp || llmBaseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IConsoleSupportAssistantClient>(_ =>
                new HttpConsoleSupportAssistantClient(
                    CreateOperateObservabilityHttpClient(),
                    llmBaseUri,
                    honuaLlmModel.Trim(),
                    honuaLlmApiKey));
            return;
        }

        services.TryAddSingleton<IConsoleSupportAssistantClient, UnsupportedConsoleSupportAssistantClient>();
    }

    private static void AddSupportTicketClient(IServiceCollection services, string? honuaSupportBaseUrl)
    {
        if (!string.IsNullOrWhiteSpace(honuaSupportBaseUrl)
            && Uri.TryCreate(honuaSupportBaseUrl.Trim(), UriKind.Absolute, out var supportBaseUri))
        {
            services.TryAddSingleton<IConsoleSupportTicketClient>(serviceProvider =>
                new HttpSupportTicketClient(
                    CreateOperateObservabilityHttpClient(),
                    supportBaseUri,
                    serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                    serviceProvider.GetRequiredService<IConsoleAccountSessionStore>()));
            return;
        }

        services.TryAddSingleton<IConsoleSupportTicketClient, UnsupportedSupportTicketClient>();
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

    // Binds the Studio query-builder surface (/studio/query, honua-console#52) to honua-server's saved
    // query content/artifacts lifecycle (honua-server#1182, AnalysisContentKind.SavedQuery) through the
    // Honua.Console.Contracts shim when a server base address is configured; otherwise the builder renders
    // a missing-binding state (never mock query data, per Console Patterns Charter section 11). The query
    // builder and the analysis builder (#53) share the single /api/v1/analysis/content client, so this
    // registers IHonuaAnalysisContentClient with TryAdd (idempotent with AddStudioAnalysisPackageDataSource).
    private static void AddStudioQueryPackageDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            // The query + analysis builders share this client, and it now carries the NL-generation routes
            // (POST .../queries/generate and .../generate). NL generation grounds + validates + runs a
            // bounded repair loop on the server, and against a local CPU model a single turn can take
            // minutes. The default HttpClient.Timeout of 100s cancels these legitimately-slow generations
            // client-side (surfacing as a false "endpoint could not be reached"). Match the server's own
            // request timeout (10 min) so the real path works — mirrors the workflow/map generation clients.
            services.TryAddSingleton<IHonuaAnalysisContentClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromMinutes(10) };
                return new HonuaAnalysisContentHttpClient(
                    httpClient,
                    new HonuaAnalysisContentClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioQueryPackageDataSource, HonuaServerStudioQueryContentDataSource>();
            return;
        }

        services.TryAddSingleton<IStudioQueryPackageDataSource, UnsupportedStudioQueryPackageDataSource>();
    }

    // Binds the Studio map-builder surface (/studio/map) to honua-server's Studio package lifecycle
    // (#1180, closed) and the content publication registry (#1183, closed) through the
    // IStudioPackageLifecycleClient shim when a server base address is configured, reusing the client
    // already registered by AddStudioAuthoringShell; otherwise the builder renders an explicit
    // missing-binding state (never mock map data — Console Patterns Charter section 11). TryAdd keeps an
    // explicit test/demo provider overridable.
    private static void AddStudioMapPackageDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            // NL->map generation grounds + validates + runs a bounded repair loop on the server, and against
            // a local CPU model a single turn can take minutes. The default HttpClient.Timeout of 100s cancels
            // these legitimately-slow generations client-side (surfacing as a false "endpoint could not be
            // reached"). Match the server's own request timeout (10 min) so the real path works — mirrors the
            // workflow generation client.
            services.TryAddSingleton<IStudioMapGenerationClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromMinutes(10) };
                return new HttpStudioMapGenerationClient(
                    httpClient,
                    new StudioMapGenerationClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioMapPackageDataSource, HonuaServerStudioMapPackageDataSource>();

            // The per-layer style picker (#161, ADR-0048) binds the layer's styleId to the styles the server
            // advertises on the PUBLIC OGC API - Styles list (GET /ogc/styles). The admin key is forwarded
            // only if configured (harmless on the public read; future-proof if the surface is ever gated).
            services.TryAddSingleton<IHonuaOgcStylesClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HonuaOgcStylesHttpClient(
                    httpClient,
                    new HonuaOgcStylesClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioMapStyleCatalogDataSource, HonuaServerStudioMapStyleCatalogDataSource>();
            return;
        }

        services.TryAddSingleton<IStudioMapPackageDataSource, UnsupportedStudioMapPackageDataSource>();
        services.TryAddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();
    }

    // Binds the Studio Map collaboration surface (/studio/map Comments + Activity tabs, honua-console#124)
    // to honua-server's durable collaboration API (honua-server#1278, slice 1: feature-pinned comment
    // threads + activity feed) through the Honua.Console.Contracts shim when a server base address is
    // configured; otherwise the surface renders an explicit missing-binding state (never fabricated
    // comments/activity, Console Patterns Charter section 11). Only the DURABLE slice is lit up: the
    // comment drawer and activity sidebar bind live. The real-time presence/cursors/markup/follow-mode
    // slots stay empty behind the deferred follow-up slice (honua-server#1290) — the live data source
    // returns empty presence/cursor lists so the chrome renders those affordances disabled-pending. With no
    // server configured the unsupported client surfaces the missing-binding state across every slot.
    // TryAdd keeps an explicit test/demo provider overridable.
    private static void AddStudioMapCollaborationDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IHonuaStudioMapCollaborationClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HonuaStudioMapCollaborationHttpClient(
                    httpClient,
                    new HonuaStudioMapCollaborationClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioMapCollaborationDataSource, HonuaServerStudioMapCollaborationDataSource>();
            return;
        }

        services.TryAddSingleton<IStudioMapCollaborationDataSource, UnsupportedStudioMapCollaborationDataSource>();
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
            // Shared with the query builder; the 10-minute timeout covers the NL-generation routes on this
            // client (see AddStudioQueryPackageDataSource for the rationale). TryAdd keeps whichever
            // registration ran first, so both register the identical client.
            services.TryAddSingleton<IHonuaAnalysisContentClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromMinutes(10) };
                return new HonuaAnalysisContentHttpClient(
                    httpClient,
                    new HonuaAnalysisContentClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioAnalysisPackageDataSource, HonuaServerStudioAnalysisContentDataSource>();
            return;
        }

        services.TryAddSingleton<IStudioAnalysisPackageDataSource, UnsupportedStudioAnalysisPackageDataSource>();
    }

    // Binds the Studio dashboard-builder surface (/studio/dashboard, #55) to honua-server's Studio package
    // lifecycle + dashboard publication registry (#1180/#1181/#1183) through the Honua.Console.Contracts
    // shim when a server base address is configured, reusing the IStudioPackageLifecycleClient already
    // registered by AddStudioAuthoringShell. Otherwise the builder renders an explicit missing-binding
    // state (never mock dashboard data — Console Patterns Charter section 11). The lifecycle client is
    // registered defensively here too so the dashboard binding is self-contained even if the authoring
    // shell registration order changes. TryAdd keeps an explicit test/demo provider overridable.
    private static void AddStudioDashboardPackageDataSource(
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
            // Dashboard generation (NL -> dashboard.document) shares the content publications/generate
            // endpoint with reports (dispatched on kind=dashboard), so it speaks the content-publication
            // client rather than the Studio package lifecycle. TryAdd keeps a single client across the
            // report/dashboard/publishing surfaces. Generation grounds + validates + runs a bounded repair
            // loop on the server and against a local CPU model a single turn can take minutes, so match the
            // server's own 10-min request timeout — mirrors the map/analysis/workflow generation clients.
            services.TryAddSingleton<IHonuaContentPublicationClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromMinutes(10) };
                return new HonuaContentPublicationHttpClient(
                    httpClient,
                    new HonuaContentPublicationClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioDashboardPackageDataSource>(serviceProvider =>
                new HonuaServerStudioDashboardPackageDataSource(
                    serviceProvider.GetRequiredService<IStudioPackageLifecycleClient>(),
                    serviceProvider.GetRequiredService<IHonuaContentPublicationClient>()));
            return;
        }

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
                // NL->workflow generation grounds + validates + runs a bounded repair loop on the
                // server, and against a local CPU model a single turn can take minutes. The default
                // HttpClient.Timeout of 100s cancels these legitimately-slow generations client-side
                // (surfacing as a false "endpoint could not be reached"). Match the server's own
                // request timeout (Limits:Connections:RequestTimeout = 10 min) so the real path works.
                var httpClient = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromMinutes(10) };
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

    private static void AddRbacAccessDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IHonuaConsoleRbacClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HonuaConsoleRbacHttpClient(
                    httpClient,
                    new HonuaConsoleRbacClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IRbacAccessDataSource, HonuaServerRbacAccessDataSource>();
            return;
        }

        services.TryAddSingleton<IRbacAccessDataSource, UnsupportedRbacAccessDataSource>();
    }

    // Binds the Operate > Catalogs discovery-endpoints surface (/operate/catalogs, honua-console#125) to
    // honua-server's catalog discovery-endpoints registry (honua-server#1279) through the
    // Honua.Console.Contracts shim when a server base address is configured; otherwise the surface renders an
    // explicit missing-binding state (never mock endpoint/item data — Console Patterns Charter section 11).
    // honua-server#1279 is not yet shipped, so in practice the merged runtime renders the missing-binding
    // state today; the page, data source, and tests already consume the full registry contract so the live
    // binding activates the moment the server endpoint lands.
    private static void AddCatalogDiscoveryDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IHonuaCatalogDiscoveryClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HonuaCatalogDiscoveryHttpClient(
                    httpClient,
                    new HonuaCatalogDiscoveryClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<ICatalogDiscoveryDataSource, HonuaServerCatalogDiscoveryDataSource>();
            return;
        }

        services.TryAddSingleton<ICatalogDiscoveryDataSource, UnsupportedCatalogDiscoveryDataSource>();
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
            // The service-layer-publish OPERATION (issue #144) reuses the same admin client + base-URL gate
            // so the publishing wizard's finish action performs a REAL publish against honua-server.
            services.TryAddSingleton<IServiceLayerPublishOperation, HonuaServerServiceLayerPublishOperation>();
            // The service-configuration OPERATIONS (Wave 5: layer enable/disable + service protocol/
            // access-policy changes) reuse the same admin client + base-URL gate so the Operate layers/
            // service-detail surfaces perform REAL mutations against honua-server.
            services.TryAddSingleton<IServiceConfigurationOperation, HonuaServerServiceConfigurationOperation>();
            // The connection-create OPERATION reuses the same admin client + base-URL gate so the Add
            // Connection form performs a REAL create against honua-server (POST /api/v1/admin/connections).
            services.TryAddSingleton<IConsoleConnectionCreateOperation, HonuaServerConsoleConnectionCreateOperation>();
            // The file-import OPERATION reuses the same admin client so the Import UI uploads a real file to
            // honua-server's streamed import endpoint (POST /api/v1/admin/import/upload).
            services.TryAddSingleton<IConsoleFileImportOperation, HonuaServerConsoleFileImportOperation>();
            // The service-import OPERATION discovers remote Esri/OGC services via honua-server
            // (POST /api/v1/admin/external-services/discover).
            services.TryAddSingleton<IConsoleServiceImportOperation, HonuaServerConsoleServiceImportOperation>();
            // Layer field-domain authoring (GET/PUT /api/v1/admin/metadata/layers/{id}/fields).
            services.TryAddSingleton<IConsoleLayerFieldsOperation, HonuaServerConsoleLayerFieldsOperation>();
            return;
        }

        services.TryAddSingleton<IOperateTransitionDataSource, UnsupportedOperateTransitionDataSource>();
        services.TryAddSingleton<IServiceLayerPublishOperation, UnsupportedServiceLayerPublishOperation>();
        services.TryAddSingleton<IServiceConfigurationOperation, UnsupportedServiceConfigurationOperation>();
        services.TryAddSingleton<IConsoleConnectionCreateOperation, UnsupportedConsoleConnectionCreateOperation>();
        services.TryAddSingleton<IConsoleFileImportOperation, UnsupportedConsoleFileImportOperation>();
        services.TryAddSingleton<IConsoleServiceImportOperation, UnsupportedConsoleServiceImportOperation>();
        services.TryAddSingleton<IConsoleLayerFieldsOperation, UnsupportedConsoleLayerFieldsOperation>();
    }

    // Binds the temporal data viewer + disconnected sync conflict review surface (/operate/temporal) to
    // honua-server's temporal data history API (#1166 slice 1: capability discovery + as-of read) and the
    // disconnected replica management API (#1167 slice 1: replica list + detail) through the
    // IHonuaTemporalClient shim when a server base address is configured. The temporal API is per
    // service/layer with no enumeration verb, so — like the publishing workspace keying off configured
    // publication ids — the viewer is keyed by a configured list of candidate sources
    // (Honua:Server:TemporalSources / HONUA_SERVER_TEMPORAL_SOURCES, "serviceId:layerId"). The deferred
    // server slices (diff/timeline/rollback execution #1285, replica conflict-review #1287) are NOT merged;
    // the live client binds what exists and renders the rest as the established not-yet-available state,
    // never fabricated (Console Patterns Charter section 11). When no base URL is configured the
    // missing-binding client is registered and the viewer renders an explicit capability explanation.
    private static void AddTemporalCapabilityClient(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey,
        string? honuaServerTemporalSources)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IHonuaTemporalClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HonuaTemporalHttpClient(
                    httpClient,
                    new HonuaTemporalClientOptions(baseUri, honuaServerAdminApiKey));
            });

            var options = HonuaServerTemporalOptions.FromConfiguredList(honuaServerTemporalSources);
            services.TryAddSingleton<ITemporalCapabilityClient>(serviceProvider =>
                new HonuaServerTemporalCapabilityClient(
                    serviceProvider.GetRequiredService<IHonuaTemporalClient>(),
                    options));
            return;
        }

        services.TryAddSingleton<ITemporalCapabilityClient, UnsupportedTemporalCapabilityClient>();
    }

    // Binds the "Import from Esri" wizard run engine + parity scorecard (#102, /operate/import/esri Run and
    // Scorecard steps) to the honua-devops migration-run API. The issue-122 handoff flags honua-devops as the
    // migration-run owner; there is no Console-consumable run contract yet, so the merged build registers only
    // the missing-binding client: the wizard's earlier steps (Source/Select/Map) render their deterministic,
    // Console-side parsed conversion preview, but the Run and Scorecard steps stay on an explicit
    // missing-binding state — Console never fabricates run progress, per-item results, or parity numbers
    // (Console Patterns Charter section 11). When honua-devops exposes a Console-bindable run contract, wire
    // the live HTTP-bound client here gated on a configured server base URL exactly like the other bindings;
    // the wizard and tests already consume the full IEsriMigrationRunDataSource. TryAdd keeps a test/demo
    // provider overridable.
    private static void AddEsriMigrationRunDataSource(IServiceCollection services) =>
        services.TryAddSingleton<IEsriMigrationRunDataSource, UnsupportedEsriMigrationRunDataSource>();

    // Binds the Operate publishing workspace (/operate/publishing) matrix + review + republish/rollback
    // lifecycle to the real honua-server content publication registry (honua-server#1183, shipped)
    // through the IHonuaContentPublicationClient shim when a server base address is configured;
    // otherwise the workspace renders a missing-binding state (never mock publishing data — Console
    // Patterns Charter section 11 — no standing in-memory publishing data source). The registry exposes
    // no list endpoint, so the matrix is keyed by the configured publication ids
    // (Honua:Server:PublicationIds / HONUA_SERVER_PUBLICATION_IDS).
    private static void AddPublishingWorkspaceDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey,
        string? honuaServerPublicationIds)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            // Reuse the content publication client already registered by the report-builder binding when
            // present; otherwise register it here (TryAdd keeps a single client across both surfaces).
            services.TryAddSingleton<IHonuaContentPublicationClient>(_ =>
            {
                var httpClient = new HttpClient { BaseAddress = baseUri };
                return new HonuaContentPublicationHttpClient(
                    httpClient,
                    new HonuaContentPublicationClientOptions(baseUri, honuaServerAdminApiKey));
            });

            var options = PublishingWorkspaceOptions.FromConfiguredList(honuaServerPublicationIds);
            services.TryAddSingleton<IPublishingWorkspaceDataSource>(serviceProvider =>
                new HonuaServerPublishingWorkspaceDataSource(
                    serviceProvider.GetRequiredService<IHonuaContentPublicationClient>(),
                    options));
            return;
        }

        services.TryAddSingleton<IPublishingWorkspaceDataSource, UnsupportedPublishingWorkspaceDataSource>();
    }

    // Binds the alert RULE definition surface (/operate/alerts/rules and /operate/alerts/rules/{ruleId},
    // honua-console UI-042) to honua-server. The rule LIST reuses the live observability rules projection
    // (/api/v1/admin/observability, already registered) through ServerOperateAlertRulesDataSource when a
    // server base address is configured; the rule detail/condition editor and rule save await the alert rule
    // DEFINITION contract (honua-server#1169) and render an explicit missing-binding state until it ships
    // (Console Patterns Charter section 11). With no server configured the unsupported source surfaces the
    // missing-binding state across the list and editor. TryAdd keeps a test/demo provider overridable.
    private static void AddOperateAlertRulesDataSource(IServiceCollection services, string? honuaServerBaseUrl)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IOperateAlertRulesDataSource>(serviceProvider =>
                new ServerOperateAlertRulesDataSource(
                    serviceProvider.GetRequiredService<IConsoleOperateObservabilityClient>()));
            return;
        }

        services.TryAddSingleton<IOperateAlertRulesDataSource, UnsupportedOperateAlertRulesDataSource>();
    }

    // Binds the Operate resource-presentation per-slot style/popup OVERRIDE surface
    // (/operate/layers/{id}/style, honua-console UI-032). honua-server does not yet expose a per-slot
    // presentation-override contract, so the merged build registers the unsupported source: the override
    // read/write render an explicit missing-binding state and never fabricate per-slot overrides (Console
    // Patterns Charter section 11). The page still reads the REAL /ogc/styles list through the already
    // registered IStudioMapStyleCatalogDataSource. When the override contract lands, wire a Server*
    // implementation here gated on a configured server base URL exactly like the other bindings. TryAdd keeps
    // a test/demo provider overridable.
    private static void AddOperateLayerStyleOverrideDataSource(IServiceCollection services) =>
        services.TryAddSingleton<IOperateLayerStyleOverrideDataSource, UnsupportedOperateLayerStyleOverrideDataSource>();

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
