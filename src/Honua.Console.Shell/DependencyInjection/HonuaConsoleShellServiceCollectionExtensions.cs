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
        bool registryIntentResolutionEnabled = false,
        string? honuaServerCredentialMode = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IConsoleHostCapabilities, BrowserConsoleHostCapabilities>();

        // Platform-adaptive keyboard-shortcut glyphs (honua-console#313): the primary modifier renders as
        // ⌘ on macOS and Ctrl on Windows/Linux. The default follows the host OS (exact on the native desktop
        // Console; Ctrl on Windows/Linux browser deployments — the overwhelming Console target).
        services.TryAddSingleton<IConsoleShortcutPlatform, RuntimeConsoleShortcutPlatform>();

        // Server-backed gates use the live SDK manifest independently of Studio intent resolution.
        // Local capability configuration can only narrow that truth; studio-builders remains local.
        // Capability snapshots are mutable and belong to the current Blazor circuit. The registry
        // remains request-time/operator-aware, but the snapshot itself must not be shared between circuits.
        services.TryAddScoped<IConsoleCapabilityManifest>(serviceProvider =>
            new ManifestBackedConsoleCapabilityManifest(
                serviceProvider.GetRequiredService<ICapabilityRegistryClient>(),
                ConsoleCapabilityManifest.SplitList(honuaConsoleAdvertisedCapabilities)));

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

        // Human-attributable Operate mutations are bearer-only by default. The exchange
        // itself depends on deployment topology because honua-server authenticates it with
        // an HttpOnly server-origin admin-session cookie. Stock Console leaves this seam
        // unavailable until a per-operator/per-profile BFF exists; a process-wide cookie
        // client would bleed identity. A process-wide API key is available only when
        // HeadlessService is selected and the sessionless profile is ServiceApiKey.
        var serverCredentialMode = ConsoleServerCredentialModeParser.Parse(honuaServerCredentialMode);
        services.TryAddSingleton<IConsoleOperatorBearerExchange, UnavailableConsoleOperatorBearerExchange>();
        services.TryAddSingleton<IConsoleOperatorBearerProvider>(serviceProvider =>
            new ConsoleOperatorBearerProvider(
                serviceProvider.GetRequiredService<IConsoleAccountSessionStore>(),
                serviceProvider.GetRequiredService<IConsoleOperatorBearerExchange>(),
                TimeProvider.System));

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
        // The live SDK registry is used by Console gates whenever a server is configured.
    // Studio:RegistryIntentResolution independently opts Studio AI into registry intent resolution;
    // leaving that flag off does not replace live Console capability truth with an allowlist.
    // TryAdd preserves explicit test/demo overrides.
    private static void AddStudioIntentResolution(
        IServiceCollection services,
        string? honuaServerBaseUrl,
        bool registryIntentResolutionEnabled)
    {
        if (TryGetHonuaServerBaseUri(honuaServerBaseUrl, out var baseUri))
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
                registryIntentResolutionEnabled
                    ? new StudioIntentResolver(
                        serviceProvider.GetRequiredService<IOmniPromptIntentClassifier>(),
                        serviceProvider.GetRequiredService<ICapabilityRegistryClient>())
                    : new NoopStudioIntentResolver(
                        serviceProvider.GetRequiredService<IOmniPromptIntentClassifier>()));
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
        if (TryGetHonuaServerBaseUri(honuaServerBaseUrl, out var baseUri))
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
        if (TryGetHonuaServerBaseUri(honuaServerBaseUrl, out var baseUri))
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
        if (TryGetHonuaServerBaseUri(honuaServerBaseUrl, out var baseUri))
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
        if (TryGetHonuaServerBaseUri(honuaServerBaseUrl, out var baseUri))
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
