using System.Net.Http;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;
using Honua.Console.Shell.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
        string? honuaSupportKbPath = null,
        string? honuaConsoleAdvertisedCapabilities = null,
        bool registryIntentResolutionEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IConsoleHostCapabilities, BrowserConsoleHostCapabilities>();

        // Capability-manifest gate for the deferred "exotic depth" surfaces (first-release cut-line,
        // docs/roadmap/FIRST_RELEASE_STRATEGY_AND_CUT_LINE.md). The advertised set is empty by default,
        // so temporal / disconnected-sync / realtime-alerting / cross-environment-promotion /
        // siem-investigations render the first-class "unsupported" state until the deployment opts them
        // in via Honua:Console:Capabilities. This is the interim source; the honua-server
        // capability-manifest document feeds the same seam once its full-document consumption lands.
        services.TryAddSingleton<IConsoleCapabilityManifest>(
            _ => ConsoleCapabilityManifest.FromConfigurationList(honuaConsoleAdvertisedCapabilities));

        // Shell-owned toast/notification surface. Scoped = one queue per Blazor circuit so a toast a
        // page raises is shown only to that connected user. The single ConsoleNotificationHost in
        // ConsoleLayout subscribes and renders them (success/error/info/warning, role=alert, auto +
        // manual dismiss). Pages surface server failures here through RunGuardedAsync rather than
        // letting an exception vanish silently.
        services.TryAddScoped<IConsoleNotificationService, ConsoleNotificationService>();
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
        AddStudioAppPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioFormPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioQueryPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioMapPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioMapCollaborationDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioAnalysisPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioDashboardPackageDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioReportPublicationDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddStudioWorkflowPackageClient(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddCollectAutomationClient(services);
        AddAiPublishDriver(services, honuaServerBaseUrl);

        // The omni-prompt AI console (honua-console#203) routes one free-text prompt to the right lane —
        // Studio (GIS authoring → the AI publish outcome card) or DevOps (ops → the deploy approval panel).
        // The classifier is a thin, deterministic, server-independent heuristic (no server classify endpoint
        // exists yet); it only chooses the lane and never actuates, so a singleton is safe.
        services.TryAddSingleton<IOmniPromptIntentClassifier, OmniPromptIntentClassifier>();

        // Registry-driven Studio-AI intent resolution (honua-console#266). Behind the
        // Studio:RegistryIntentResolution flag (default OFF): when ON and a server is bound, the Studio
        // generate/validate/preview/publish lifecycle resolves against the live capability-manifest
        // registry and deferred/unavailable capabilities are hidden from Studio AI. OFF preserves current
        // behavior via the no-op resolver (no registry gating).
        AddStudioIntentResolution(services, honuaServerBaseUrl, registryIntentResolutionEnabled);
        AddShareAccessDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddRbacAccessDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddCatalogDiscoveryDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddOperateTransitionDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddTemporalCapabilityClient(services, honuaServerBaseUrl, honuaServerAdminApiKey, honuaServerTemporalSources);
        AddEsriMigrationRunDataSource(services);
        AddPublishingWorkspaceDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey, honuaServerPublicationIds);
        AddConsoleCatalogClient(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddConsoleSensorThingsClient(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddConsoleSceneClient(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddOperateAlertRulesDataSource(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddVersionManagementOperation(services, honuaServerBaseUrl, honuaServerAdminApiKey);
        AddOperateLayerStyleOverrideDataSource(services, honuaServerBaseUrl);
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
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                honuaServerAdminApiKey));

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

        // The human-in-the-loop deploy approval surface binds to the honua-server
        // deploy-control admin endpoints (GET /operations/{id}, POST .../submit,
        // POST .../rollback) via the same thin typed HttpClient. An agent
        // creates-and-pauses an operation on pr-first; an operator reviews and approves
        // (submit) / rejects / rolls back here, all through the server's governed
        // endpoints (the OperatorApprovalGate stays the real gate — never bypassed). No
        // standing in-memory source is registered (Charter section 11); when no
        // environment is connected the surface renders the missing-binding state. The
        // admin API key is sent as X-API-Key.
        services.TryAddSingleton<IConsoleDeployApprovalClient>(serviceProvider =>
            new HttpConsoleDeployApprovalClient(
                CreateOperateObservabilityHttpClient(),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                honuaServerAdminApiKey));

        // The first-class agent-operation approval API (issue #193, honua-server #1694):
        // GET /api/v1/admin/proposals (list/filter), GET .../{id} (plan/diff/dry-run/risk),
        // POST .../{id}/approve, POST .../{id}/reject (reason required). Approve/reject are
        // gated server-side by the RBAC 'approve' grant + separation of duties; a denied
        // decision returns 403 (surfaced as Forbidden — never bypassed). No standing
        // in-memory source is registered (Charter section 11); when no environment is
        // connected every call renders the missing-binding state. The admin API key is sent
        // as X-API-Key.
        services.TryAddSingleton<IConsoleProposalsClient>(serviceProvider =>
            new HttpConsoleProposalsClient(
                CreateOperateObservabilityHttpClient(),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                honuaServerAdminApiKey));

        // Live approval-inbox updates (issue #193, honua-server #1695): the console connects
        // from its server process to the honua-server admin realtime hub's proposals group at
        // {server}/hubs/admin and projects ProposalPending / ProposalResolved events onto the
        // inbox without polling. A connect failure (no environment bound, hub unsupported)
        // degrades to an inert no-op so the inbox stays usable via manual refresh.
        services.TryAddSingleton<IConsoleProposalRealtimeClient>(serviceProvider =>
            new SignalRConsoleProposalRealtimeClient(
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                serviceProvider.GetRequiredService<ILogger<SignalRConsoleProposalRealtimeClient>>(),
                honuaServerAdminApiKey));

        // The approval inbox (#193) aggregates every proposal source into one GIS-department
        // work queue, classified by ticket type. Per honua-server #1690's locked ownership split
        // it merges TWO sources behind the shared IConsoleProposalSource seam: the honua-server
        // proposals API (admin/deploy/metadata/seed) and the honua-devops console-bridge
        // gitops/deliverable proposals. No standing in-memory source is registered (Charter
        // section 11); when no source is reachable the inbox renders the missing-binding state
        // surfaced by the primary (server) source's read.
        //
        // honua-devops does not expose a console-facing proposals endpoint yet (it is a CLI/MCP
        // agent host; see IConsoleDevOpsProposalsClient), so the default devops client degrades
        // gracefully to an empty allowed result — the seam and normalization are in place for the
        // day that endpoint (or honua-server #1692's aggregating projection) lands.
        services.TryAddSingleton<IConsoleDevOpsProposalsClient, UnavailableConsoleDevOpsProposalsClient>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConsoleProposalSource, ServerConsoleProposalSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConsoleProposalSource, DevOpsConsoleProposalSource>());
        services.TryAddSingleton<IConsoleApprovalInboxClient>(serviceProvider =>
            new ConsoleApprovalInboxClient(serviceProvider.GetServices<IConsoleProposalSource>()));

        // Server-version detection for the server-upgrade flow binds to the connected
        // honua-server's capability manifest (GET /api/v1/capabilities/manifest, the
        // `server` block) via the same thin typed HttpClient. It detects the target
        // environment's running Honua version — including when the target is on an OLDER
        // version — so the upgrade path can be shown and a no-op/downgrade gated. No standing
        // in-memory source is registered (Charter section 11); when no environment is
        // connected the read renders the missing-binding state. The manifest is anonymous;
        // the admin key is attached when present for admin-gated edges but is not required.
        services.TryAddSingleton<IConsoleServerVersionClient>(serviceProvider =>
            new HttpConsoleServerVersionClient(
                CreateOperateObservabilityHttpClient(),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                honuaServerAdminApiKey));

        // Operate metrics binds to the connected honua-server's production-monitoring
        // endpoints (group /monitoring, admin-authorized, bare JSON — NO ApiResponse
        // envelope) via a thin typed HttpClient behind Honua.Console.Contracts. No
        // standing in-memory metrics source is registered (Console Patterns Charter
        // section 11); each metric activates against live data when an environment is
        // connected, else the surface renders an honest per-section missing-binding
        // state. The admin API key is sent as X-API-Key. The aggregator loads every
        // metric in parallel into one snapshot, each carrying its own section status.
        services.TryAddSingleton<IConsoleMonitoringMetricsClient>(serviceProvider =>
            new HttpConsoleMonitoringMetricsClient(
                CreateOperateObservabilityHttpClient(),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                honuaServerAdminApiKey));
        services.TryAddSingleton<IOperateMetricsDataSource>(serviceProvider =>
            new OperateMetricsDataSource(
                serviceProvider.GetRequiredService<IConsoleMonitoringMetricsClient>()));

        // Ops Health (ADR-0060 WS4) + Copilot Findings (#193) bind to honua-server's
        // consolidated ops-health snapshot and deterministic ops-findings engine (group
        // /api/v1/admin/observability, admin-authorized, bare JSON — NO ApiResponse
        // envelope) through the Honua.Console.Contracts shim. Findings are deterministic
        // server output proposed by id through the existing approval gateway — no
        // model/LLM anything (ADR-0028). No standing in-memory source is registered
        // (Charter section 11); each read degrades to the shared missing-binding /
        // section-status surface when unbound. The admin API key is sent as X-API-Key.
        services.TryAddSingleton<IConsoleOpsHealthClient>(serviceProvider =>
            new HttpConsoleOpsHealthClient(
                CreateOperateObservabilityHttpClient(),
                serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                honuaServerAdminApiKey));
        services.TryAddSingleton<IOpsHealthDataSource>(serviceProvider =>
            new OpsHealthDataSource(
                serviceProvider.GetRequiredService<IConsoleOpsHealthClient>()));
        services.TryAddSingleton<IConsoleOpsFindingsClient>(serviceProvider =>
            new HttpConsoleOpsFindingsClient(
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
                    CreateSupportAssistantHttpClient(),
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

    /// <summary>
    /// Swaps the Collect automation client for the local in-memory seeded simulator. For explicit demo/local
    /// composition only — never the merged runtime path for server/Collect-owned automation content
    /// (honua-console#219). Mirrors <see cref="AddHonuaConsoleDemoStudioWorkflowPackages"/>.
    /// </summary>
    public static IServiceCollection AddHonuaConsoleDemoCollectAutomations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Singleton<ICollectAutomationClient>(
            _ => InMemoryCollectAutomationClient.CreateSeeded()));

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
            services.TryAddSingleton<IStudioPackageLifecycleClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri);
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
    // data). The from-prompt surface additionally binds the app generation client.
    private static void AddStudioAppPackageDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            // NL->app generation grounds + validates + runs a bounded repair loop on the server, and against
            // a local CPU model a single turn can take minutes. The default HttpClient.Timeout of 100s cancels
            // these legitimately-slow generations client-side (surfacing as a false "endpoint could not be
            // reached"). Match the server's own request timeout (10 min) so the real path works — mirrors the
            // map/dashboard generation clients.
            services.TryAddSingleton<IStudioAppGenerationClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri, TimeSpan.FromMinutes(10));
                return new HttpStudioAppGenerationClient(
                    httpClient,
                    new StudioAppGenerationClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioAppPackageDataSource>(serviceProvider =>
                new HonuaServerStudioAppPackageDataSource(
                    serviceProvider.GetRequiredService<IStudioPackageLifecycleClient>(),
                    serviceProvider.GetRequiredService<IStudioAppGenerationClient>()));
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
            services.TryAddSingleton<IHonuaFormPackageClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri);
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
            services.TryAddSingleton<IHonuaAnalysisContentClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri, TimeSpan.FromMinutes(10));
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
            services.TryAddSingleton<IStudioMapGenerationClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri, TimeSpan.FromMinutes(10));
                return new HttpStudioMapGenerationClient(
                    httpClient,
                    new StudioMapGenerationClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioMapPackageDataSource, HonuaServerStudioMapPackageDataSource>();

            // The per-layer style picker (#161, ADR-0048) binds the layer's styleId to the styles the server
            // advertises on the PUBLIC OGC API - Styles list (GET /ogc/styles). The admin key is forwarded
            // only if configured (harmless on the public read; future-proof if the surface is ever gated).
            // It reads a public surface, so it uses the anonymous-capable client rather than failing closed
            // for an unresolved operator (honua-console#254).
            services.TryAddSingleton<IHonuaOgcStylesClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.CreatePublic(serviceProvider, baseUri);
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
            services.TryAddSingleton<IHonuaStudioMapCollaborationClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri);
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
            services.TryAddSingleton<IHonuaAnalysisContentClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri, TimeSpan.FromMinutes(10));
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
            services.TryAddSingleton<IStudioPackageLifecycleClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri);
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
            services.TryAddSingleton<IHonuaContentPublicationClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri, TimeSpan.FromMinutes(10));
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
            services.TryAddSingleton<IHonuaContentPublicationClient>(serviceProvider =>
            {
                // 10-min timeout: report/dashboard generation share this client and a local model can take
                // minutes. Set it here too rather than relying on TryAdd ordering with the dashboard registration.
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri, TimeSpan.FromMinutes(10));
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
            services.TryAddSingleton<IWorkflowPackageApiClient>(serviceProvider =>
            {
                // NL->workflow generation grounds + validates + runs a bounded repair loop on the
                // server, and against a local CPU model a single turn can take minutes. The default
                // HttpClient.Timeout of 100s cancels these legitimately-slow generations client-side
                // (surfacing as a false "endpoint could not be reached"). Match the server's own
                // request timeout (Limits:Connections:RequestTimeout = 10 min) so the real path works.
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri, TimeSpan.FromMinutes(10));
                return new HttpWorkflowPackageApiClient(
                    httpClient,
                    new WorkflowPackageClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IStudioWorkflowPackageClient, ServerStudioWorkflowPackageClient>();
            return;
        }

        services.TryAddSingleton<IStudioWorkflowPackageClient, UnsupportedStudioWorkflowPackageClient>();
    }

    // Binds the Collect automation authoring + versioning surface (/studio/automations, honua-console#219) to
    // the server/Collect-owned automation content + version projection over the shipped Data Events engine
    // (Honua.Collect.Core, honua-collect PRs #58/#84/#94). That projection is not yet exposed to Console, so
    // the merged runtime registers the missing-binding client and the surface renders the shared blocked
    // state (Console Patterns Charter §11) instead of seeded automation data. The seeded in-memory client
    // (AddHonuaConsoleDemoCollectAutomations) stays test/demo-only — never the DI default. TryAdd keeps an
    // explicit test/demo provider overridable.
    private static void AddCollectAutomationClient(IServiceCollection services)
    {
        services.TryAddSingleton<ICollectAutomationClient, UnsupportedCollectAutomationClient>();
    }

    // Binds the resource-first publish flow's AI driver (redesign Phase 3) to the honua-server AI generation
    // surface. The driver reuses the same IWorkflowPackageApiClient (the Bedrock-capable WorkflowGeneration
    // providers — honua-server #1737) AddStudioWorkflowPackageClient registers when a server is configured, so
    // there is no second HttpClient. With no server bound it falls back to the UnsupportedAiPublishDriver,
    // which surfaces an honest "AI unavailable" state in AI mode (never a fabricated proposal, never a crash —
    // Console Patterns Charter §11). Manual mode is unaffected and drives without an AI binding.
    private static void AddAiPublishDriver(IServiceCollection services, string? honuaServerBaseUrl)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IAiPublishDriver, ServerAiPublishDriver>();
            return;
        }

        services.TryAddSingleton<IAiPublishDriver, UnsupportedAiPublishDriver>();
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
            services.TryAddSingleton<IHonuaConsoleContentClient>(serviceProvider =>
            {
                // The catalog/content client serves BOTH authenticated operator reads AND the
                // legitimately-anonymous /public open-data pages, so it uses the anonymous-capable client:
                // the operator bearer is forwarded when resolved, but an anonymous visitor is tolerated by
                // design (honua-console#254). It must NOT fail closed like the privileged surfaces.
                var httpClient = HonuaServerClientFactory.CreatePublic(serviceProvider, baseUri);
                return new HonuaConsoleContentHttpClient(
                    httpClient,
                    new HonuaConsoleContentClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IConsoleCatalogClient, HonuaServerConsoleCatalogClient>();
            return;
        }

        services.TryAddSingleton<IConsoleCatalogClient, UnsupportedConsoleCatalogClient>();
    }

    // SensorThings (STA v1.1) browse surface binds to a real honua-server through
    // the conformance-shaped /sta/v1.1 read API (honua-server #1842 / #1747) via a
    // thin typed HttpClient. No standing in-memory data source is registered
    // (Console Patterns Charter section 11); when an environment is connected the
    // browse + chart views read live data, else they render the missing-binding
    // state. The InMemory client stays test/demo-only.
    private static void AddConsoleSensorThingsClient(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IConsoleSensorThingsClient>(serviceProvider =>
                new HttpConsoleSensorThingsClient(
                    CreateOperateObservabilityHttpClient(),
                    serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                    serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                    honuaServerAdminApiKey));
            return;
        }

        services.TryAddSingleton<IConsoleSensorThingsClient, UnsupportedConsoleSensorThingsClient>();
    }

    // 3D scene surface binds to a real honua-server: scene discovery
    // (GET /api/scenes), LAS point-cloud ingest
    // (POST /api/v1/admin/scenes/ingest/pointcloud, honua-server #1840 / #1201),
    // and tileset URL resolution (/scenes/{id}/tileset.json) via a thin typed
    // HttpClient. No standing in-memory data source is registered (Console
    // Patterns Charter section 11); when an environment is connected the discover
    // + ingest + view flow reads live data, else it renders missing-binding. The
    // admin API key is sent as X-API-Key (admin-authorized ingest endpoint).
    private static void AddConsoleSceneClient(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IConsoleSceneClient>(serviceProvider =>
                new HttpConsoleSceneClient(
                    CreateSceneIngestHttpClient(),
                    serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                    serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                    honuaServerAdminApiKey));
            return;
        }

        services.TryAddSingleton<IConsoleSceneClient, UnsupportedConsoleSceneClient>();
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
            services.TryAddSingleton<IHonuaConsoleShareClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri);
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
            services.TryAddSingleton<IHonuaConsoleRbacClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri);
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
            services.TryAddSingleton<IHonuaCatalogDiscoveryClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri);
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
            services.TryAddSingleton<IHonuaAdminOperateClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri);
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
            // Layer relationships authoring (GET/PUT /api/v1/admin/metadata/layers/{id}/relationships).
            services.TryAddSingleton<IConsoleLayerRelationshipsOperation, HonuaServerConsoleLayerRelationshipsOperation>();
            // Layer subtypes + attribute-rules authoring
            // (GET/PUT /api/v1/admin/metadata/layers/{id}/subtypes|attribute-rules).
            services.TryAddSingleton<IConsoleLayerSubtypesOperation, HonuaServerConsoleLayerSubtypesOperation>();
            // Layer permanent-filter authoring (GET/PUT /api/v1/admin/metadata/layers/{id}/filter) — the
            // server-enforced query filter applied to every read of the layer.
            services.TryAddSingleton<IConsoleLayerFilterOperation, HonuaServerConsoleLayerFilterOperation>();
            // Layer display / editing / CRS metadata authoring
            // (GET/PUT /api/v1/admin/metadata/layers/{id}/display|editing|spatial).
            services.TryAddSingleton<IConsoleLayerMetadataOperation, HonuaServerConsoleLayerMetadataOperation>();
            // Layer 3D extrusion / symbology + lifecycle status authoring
            // (GET/PUT /api/v1/admin/metadata/layers/{id}/extrusion|status).
            services.TryAddSingleton<IConsoleLayer3DOperation, HonuaServerConsoleLayer3DOperation>();
            // Service time-info authoring (GET settings + PUT /api/v1/admin/services/{svc}/timeinfo).
            services.TryAddSingleton<IConsoleTimeInfoOperation, HonuaServerConsoleTimeInfoOperation>();
            // Discovery / catalog metadata authoring (GET/PUT discovery for a layer or a service) — drives
            // OGC API Records / STAC / DCAT / Esri documentInfo output.
            services.TryAddSingleton<IConsoleDiscoveryMetadataOperation, HonuaServerConsoleDiscoveryMetadataOperation>();
            // Publication-overrides authoring (GET/PUT overrides for a publication — a layer's exposure within a
            // service): titleOverride, per-publication field aliases, capabilities, supported formats, isPrimary.
            services.TryAddSingleton<IConsolePublicationOverridesOperation, HonuaServerConsolePublicationOverridesOperation>();
            return;
        }

        services.TryAddSingleton<IOperateTransitionDataSource, UnsupportedOperateTransitionDataSource>();
        services.TryAddSingleton<IServiceLayerPublishOperation, UnsupportedServiceLayerPublishOperation>();
        services.TryAddSingleton<IServiceConfigurationOperation, UnsupportedServiceConfigurationOperation>();
        services.TryAddSingleton<IConsoleConnectionCreateOperation, UnsupportedConsoleConnectionCreateOperation>();
        services.TryAddSingleton<IConsoleFileImportOperation, UnsupportedConsoleFileImportOperation>();
        services.TryAddSingleton<IConsoleServiceImportOperation, UnsupportedConsoleServiceImportOperation>();
        services.TryAddSingleton<IConsoleLayerFieldsOperation, UnsupportedConsoleLayerFieldsOperation>();
        services.TryAddSingleton<IConsoleLayerRelationshipsOperation, UnsupportedConsoleLayerRelationshipsOperation>();
        services.TryAddSingleton<IConsoleLayerSubtypesOperation, UnsupportedConsoleLayerSubtypesOperation>();
        services.TryAddSingleton<IConsoleLayerFilterOperation, UnsupportedConsoleLayerFilterOperation>();
        services.TryAddSingleton<IConsoleLayerMetadataOperation, UnsupportedConsoleLayerMetadataOperation>();
        services.TryAddSingleton<IConsoleLayer3DOperation, UnsupportedConsoleLayer3DOperation>();
        services.TryAddSingleton<IConsoleTimeInfoOperation, UnsupportedConsoleTimeInfoOperation>();
        services.TryAddSingleton<IConsoleDiscoveryMetadataOperation, UnsupportedConsoleDiscoveryMetadataOperation>();
        services.TryAddSingleton<IConsolePublicationOverridesOperation, UnsupportedConsolePublicationOverridesOperation>();
    }

    // Binds the temporal data viewer + disconnected sync conflict review surface (/operate/temporal) to
    // honua-server's temporal data history API (#1166 slice 1: capability discovery + as-of read) and the
    // disconnected replica management API (#1167 slice 1: replica list + detail; slice 2: conflict review +
    // resolution) through the IHonuaTemporalClient shim when a server base address is configured. The
    // temporal API is per service/layer with no enumeration verb, so — like the publishing workspace keying
    // off configured publication ids — the viewer is keyed by a configured list of candidate sources
    // (Honua:Server:TemporalSources / HONUA_SERVER_TEMPORAL_SOURCES, "serviceId:layerId"). The deferred
    // server slice (diff/timeline/rollback execution #1285) is NOT merged; the live client binds what
    // exists and renders the rest as the established not-yet-available state, never fabricated (Console
    // Patterns Charter section 11). When no base URL is configured the missing-binding client is registered
    // and the viewer renders an explicit capability explanation.
    private static void AddTemporalCapabilityClient(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey,
        string? honuaServerTemporalSources)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IHonuaTemporalClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri);
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

    // Registry-driven Studio-AI intent resolution (honua-console#266), behind the
    // Studio:RegistryIntentResolution flag (default OFF). When the flag is ON AND a valid server base URL is
    // configured, the capability registry binds to the server capability manifest
    // (GET /api/v1/capabilities/manifest) through the shared Honua.Sdk.Studio IHonuaCapabilityManifestClient
    // projection (Console Patterns Charter §11a: binding allowed because the contract lives in the SDK), and
    // the Studio generate/validate/preview/publish lifecycle resolves against it — deferred/unavailable
    // capabilities are hidden from Studio AI. Otherwise (flag OFF, or ON with no server bound) the
    // missing-binding registry client + the no-op resolver are registered, preserving CURRENT behavior:
    // every phase resolves as available without registry gating. TryAdd keeps a test/demo provider overridable.
    private static void AddStudioIntentResolution(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        bool registryIntentResolutionEnabled)
    {
        if (registryIntentResolutionEnabled
            && Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<Honua.Sdk.Studio.Capabilities.IHonuaCapabilityManifestClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri);
                return new Honua.Sdk.Studio.Capabilities.HonuaCapabilityManifestClient(httpClient);
            });
            services.TryAddSingleton<ICapabilityRegistryClient>(serviceProvider =>
                new HonuaServerCapabilityRegistryClient(
                    serviceProvider.GetRequiredService<Honua.Sdk.Studio.Capabilities.IHonuaCapabilityManifestClient>()));
            services.TryAddSingleton<IStudioIntentResolver>(serviceProvider =>
                new StudioIntentResolver(
                    serviceProvider.GetRequiredService<IOmniPromptIntentClassifier>(),
                    serviceProvider.GetRequiredService<ICapabilityRegistryClient>()));
            return;
        }

        services.TryAddSingleton<ICapabilityRegistryClient, UnsupportedCapabilityRegistryClient>();
        services.TryAddSingleton<IStudioIntentResolver>(serviceProvider =>
            new NoopStudioIntentResolver(
                serviceProvider.GetRequiredService<IOmniPromptIntentClassifier>()));
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
            services.TryAddSingleton<IHonuaContentPublicationClient>(serviceProvider =>
            {
                // 10-min timeout to match the generation clients that share IHonuaContentPublicationClient
                // (report/dashboard generation), independent of registration order.
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri, TimeSpan.FromMinutes(10));
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
    // (/api/v1/admin/observability, already registered); the rule detail/condition editor and rule save bind
    // the SHIPPED alert-rule DEFINITION admin contract (honua-server#1169, /api/v{version}/admin/alerts/rules…)
    // through HttpConsoleAlertRulesClient (X-API-Key admin auth, ApiResponse<T> envelope), all behind
    // ServerOperateAlertRulesDataSource when a server base address is configured. With no server configured the
    // unsupported source surfaces the missing-binding state across the list and editor (Console Patterns
    // Charter section 11). TryAdd keeps a test/demo provider overridable.
    private static void AddOperateAlertRulesDataSource(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IConsoleAlertRulesClient>(serviceProvider =>
                new HttpConsoleAlertRulesClient(
                    CreateOperateObservabilityHttpClient(),
                    serviceProvider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
                    honuaServerAdminApiKey));

            services.TryAddSingleton<IOperateAlertRulesDataSource>(serviceProvider =>
                new ServerOperateAlertRulesDataSource(
                    serviceProvider.GetRequiredService<IConsoleOperateObservabilityClient>(),
                    serviceProvider.GetRequiredService<IConsoleAlertRulesClient>()));
            return;
        }

        services.TryAddSingleton<IOperateAlertRulesDataSource, UnsupportedOperateAlertRulesDataSource>();
    }

    // Binds the Operate branch-version manager + conflict-resolution surface (/operate/versions,
    // honua-console#177) to honua-server's GeoServices VersionManagementServer (#371 / PR #1551):
    // list/create/alter/delete versions, reconcile with an auto-resolution policy, inspect the pending 3-way
    // conflict set, submit manual per-feature resolutions, and post to DEFAULT. Live only when a server base
    // URL is configured; otherwise the unsupported source renders an explicit missing-binding state and never
    // fabricates a version operation (Console Patterns Charter section 11).
    private static void AddVersionManagementOperation(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        string? honuaServerAdminApiKey)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            services.TryAddSingleton<IHonuaVersionManagementClient>(serviceProvider =>
            {
                var httpClient = HonuaServerClientFactory.Create(serviceProvider, baseUri);
                return new HonuaVersionManagementHttpClient(
                    httpClient,
                    new HonuaVersionManagementClientOptions(baseUri, honuaServerAdminApiKey));
            });
            services.TryAddSingleton<IVersionManagementOperation, HonuaServerVersionManagementOperation>();
            return;
        }

        services.TryAddSingleton<IVersionManagementOperation, UnsupportedVersionManagementOperation>();
    }

    // Binds the Operate resource-presentation per-layer popup-info + drawing-info (renderer) authoring surface
    // (/operate/layers/{id}/style, honua-console UI-032) to the SHIPPED honua-server admin authoring endpoints
    // (GET/PUT /api/v1/admin/metadata/layers/{id}/popup-info and .../drawing-info). When a server base URL is
    // configured the live ServerOperateLayerStyleOverrideDataSource reads/writes both documents through the
    // already-registered IHonuaAdminOperateClient (resolving the route's canonical resource id to the layer's
    // global id via the live layers projection). With no server configured the unsupported source surfaces an
    // honest missing-binding state and never fabricates a popup/renderer (Console Patterns Charter section 11);
    // the page still reads the REAL /ogc/styles list through IStudioMapStyleCatalogDataSource. TryAdd keeps a
    // test/demo provider overridable.
    private static void AddOperateLayerStyleOverrideDataSource(IServiceCollection services, string? honuaServerBaseUrl)
    {
        if (Uri.TryCreate(honuaServerBaseUrl, UriKind.Absolute, out var baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
        {
            // IOperateTransitionDataSource + IHonuaAdminOperateClient are registered by
            // AddOperateTransitionDataSource under the same base-URL gate.
            services.TryAddSingleton<IOperateLayerStyleOverrideDataSource>(serviceProvider =>
                new ServerOperateLayerStyleOverrideDataSource(
                    serviceProvider.GetRequiredService<IOperateTransitionDataSource>(),
                    serviceProvider.GetRequiredService<IHonuaAdminOperateClient>()));
            return;
        }

        services.TryAddSingleton<IOperateLayerStyleOverrideDataSource, UnsupportedOperateLayerStyleOverrideDataSource>();
    }

    private static HttpClient CreateOperateObservabilityHttpClient() =>
        new(CreateBoundedLifetimeHandler())
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

    // The L0 support assistant drives the SAME slow local-CPU LLM inference path as the Studio
    // generation clients (qwen via NIM/vLLM/llama.cpp/Ollama) with a non-streaming chat completion,
    // where a single turn can take minutes. Give it the same generation-appropriate budget those
    // clients use (10 min) instead of the 30s observability/metrics budget, which cancels a
    // legitimately-slow answer mid-generation and surfaces the false "assistant endpoint unreachable"
    // failure — defeating the deflection goal on exactly the slow-but-valid path.
    private static HttpClient CreateSupportAssistantHttpClient() =>
        new(CreateBoundedLifetimeHandler())
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

    // The scene client uploads LAS point clouds up to 256 MB (OperateScenesPage MaxFileBytes) as a
    // multipart POST. HttpClient.Timeout is a whole-operation deadline (connect + full body upload +
    // response), so the 30s observability/metrics-read budget cancels any realistically-sized upload
    // mid-stream and the client surfaces a false "endpoint unreachable" (OperationCanceledException ->
    // SceneReadStatus.Unavailable). Give the ingest path the same generation-class budget the slow
    // generation/support clients use (10 min) instead of the 30s read budget; the caller's
    // CancellationToken still governs user-initiated cancellation.
    private static HttpClient CreateSceneIngestHttpClient() =>
        new(CreateBoundedLifetimeHandler())
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

    // Family-A server-bound clients are built by HonuaServerClientFactory (profile/session-aware
    // binding over a bounded-lifetime pooled handler). The observability client below is not part of
    // that binding family but shares the same bounded-lifetime handler so a long-lived singleton
    // client does not pin stale DNS for the active environment's server.
    private static SocketsHttpHandler CreateBoundedLifetimeHandler() =>
        new()
        {
            // Refresh pooled connections so a long-lived singleton client does
            // not pin stale DNS for the active environment's server.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
}
