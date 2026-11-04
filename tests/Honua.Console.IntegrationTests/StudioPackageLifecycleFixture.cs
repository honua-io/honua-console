using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Sdk.Studio.Packages;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StudioPackageLifecycleIntegrationCollection : ICollectionFixture<StudioPackageLifecycleFixture>
{
    public const string Name = "StudioPackageLifecycleIntegration";
}

/// <summary>
/// Boots a real honua-server (with PostgreSQL) via Testcontainers so the server-backed Studio authoring
/// shell can be asserted against the live package draft/lifecycle + validation/preview API (#1180/#1181).
/// Off by default; skips gracefully when Docker, the server image, the opt-in flag, or the admin API key
/// is unavailable (AC: Testcontainers coverage with Docker-unavailable skip). Reuses
/// <see cref="HonuaServerTestcontainer"/> so the container-boot mechanics are not duplicated.
/// </summary>
public sealed class StudioPackageLifecycleFixture : IAsyncLifetime
{
    private HonuaServerTestcontainer? _container;
    private string? _proposerKey;
    private string? _reviewerKey;
    private string? _readOnlyKey;
    private readonly Dictionary<string, Guid> _managedKeyIds = [];

    public ConsoleTrustIntegrationOptions Options { get; } = ConsoleTrustIntegrationOptions.Load();

    public string? SkipReason { get; private set; } = ConsoleTrustIntegrationOptions.GetStudioSkipReason();

    public Uri BaseAddress { get; private set; } = new("https://localhost");

    public async Task InitializeAsync()
    {
        if (SkipReason is not null)
        {
            return;
        }

        if (Options.ExternalBaseUri is not null)
        {
            BaseAddress = Options.ExternalBaseUri;
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            _container = await HonuaServerTestcontainer.StartAsync(Options, timeout.Token).ConfigureAwait(false);
            BaseAddress = _container.BaseAddress;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The lane is opt-in and must skip - never fail - when Docker/Testcontainers cannot start the
            // server. Record the reason so the SkippableFact bodies skip with it, and tear down anything
            // that partially started.
            SkipReason = "The honua-server Studio integration container could not start "
                + $"({ex.GetType().Name}: {ex.Message}). Ensure Docker is running and the configured server "
                + "image is pullable, or set HONUA_CONSOLE_EXTERNAL_BASE_URL.";
            if (_container is not null)
            {
                await _container.DisposeAsync().ConfigureAwait(false);
                _container = null;
            }
        }

        if (SkipReason is null && _container is not null)
        {
            // Provision only inside this ephemeral test server, never an external/operator deployment.
            // Contract/auth failures here fail initialization instead of being converted to a Docker skip.
            using var bootstrap = CreateHttpClient(Options.StudioAdminApiKey);
            _proposerKey = await CreateActorAsync(bootstrap, "proposer", "admin");
            _reviewerKey = await CreateActorAsync(bootstrap, "reviewer", "admin:read", "admin:approve");
            _readOnlyKey = await CreateActorAsync(bootstrap, "reader", "admin:read");
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            try
            {
                using var bootstrap = CreateHttpClient(Options.StudioAdminApiKey);
                foreach (var id in _managedKeyIds.Values)
                {
                    using var response = await bootstrap.PostAsync($"/api/v1/admin/api-keys/{id}/revoke", null);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }
            }
            finally
            {
                await _container.DisposeAsync().ConfigureAwait(false);
                _container = null;
            }
        }
    }

    /// <summary>
    /// Builds the production Studio lifecycle client against the live server. Accepts the dev/self-signed
    /// certificate only when the fixture server is TLS, so the test exercises the real client + admin
    /// API-key path.
    /// </summary>
    public IStudioPackageLifecycleClient CreateClient()
    {
        var httpClient = CreateHttpClient();
        return new HttpStudioPackageLifecycleClient(
            httpClient,
            new StudioPackageLifecycleClientOptions(BaseAddress, _proposerKey ?? Options.StudioAdminApiKey));
    }

    public async Task AssertValidDraftAsync(Guid draftId)
    {
        using var lifecycle = (HttpStudioPackageLifecycleClient)CreateClient();
        var validation = await lifecycle.ValidatePackageDraftAsync(draftId);
        Assert.True(validation.IsSuccess, validation.Issue?.Detail);
        Assert.NotNull(validation.Data);
        Assert.True(validation.Data.Status is StudioPackageValidationStatus.Valid or StudioPackageValidationStatus.Warning,
            JsonSerializer.Serialize(validation.Data));
    }

    /// <summary>Proves the focused read/approve key recipe without granting general administrative writes.</summary>
    public async Task AssertFocusedApprovalPermissionsAsync(StudioPendingPublication pending)
    {
        Assert.NotNull(_container);
        Assert.NotNull(_readOnlyKey);
        Assert.NotNull(_reviewerKey);
        using var readerHttp = CreateHttpClient();
        var reader = CreateProposalsClient(readerHttp, _readOnlyKey);
        var denied = await reader.ApproveAsync(pending.Operation.ProposalId!);
        Assert.Equal(OperateSectionStatus.Forbidden, denied.Status);
        Assert.Contains("admin:approve", denied.Message + " " + denied.Detail, StringComparison.OrdinalIgnoreCase);

        using var reviewer = CreateHttpClient(_reviewerKey);
        foreach (var path in new[] { "/api/v1/admin/proposals", $"/api/v1/admin/proposals/{pending.Operation.ProposalId}" })
        {
            using var response = await reviewer.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var permissionsResponse = await reviewer.GetAsync(
            $"/api/v1/admin/api-keys/{_managedKeyIds["reviewer"]}/effective-permissions");
        Assert.Equal(HttpStatusCode.OK, permissionsResponse.StatusCode);
        using var permissionsDocument = JsonDocument.Parse(await permissionsResponse.Content.ReadAsStringAsync());
        var permissions = permissionsDocument.RootElement.GetProperty("data").GetProperty("permissions")
            .EnumerateArray().Select(value => value.GetString()).OrderBy(value => value).ToArray();
        Assert.Equal(new[] { "admin:approve", "admin:read" }, permissions);

        using var write = await reviewer.PutAsJsonAsync("/api/v1/admin/services/focused-grant-probe/access-policy",
            new { allowAnonymous = true });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        var unchanged = await reader.GetAsync(pending.Operation.ProposalId!);
        Assert.True(unchanged.IsAllowed, unchanged.Message);
        Assert.Equal(ConsoleProposalStatus.AwaitingApproval, unchanged.Value!.Status);
    }

    private async Task<string> CreateActorAsync(HttpClient bootstrap, string role, params string[] permissions)
    {
        using var response = await bootstrap.PostAsJsonAsync("/api/v1/admin/api-keys", new
        {
            name = $"console-publication-{role}-{Guid.NewGuid():N}",
            permissions,
            expiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        _managedKeyIds.Add(role, data.GetProperty("apiKey").GetProperty("id").GetGuid());
        return data.GetProperty("key").GetString()!;
    }

    private HttpConsoleProposalsClient CreateProposalsClient(HttpClient http, string key)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "publication-fixture",
            DisplayName = "Ephemeral publication fixture",
            ServerBaseUri = BaseAddress,
            UpdatedAt = DateTimeOffset.UtcNow,
            Account = new ConsoleAccountBinding { AuthMode = ConsoleAccountAuthMode.ServiceApiKey }
        };
        return new HttpConsoleProposalsClient(http,
            new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id),
            new InMemoryConsoleAccountSessionStore(), key,
            credentialMode: ConsoleServerCredentialMode.HeadlessService);
    }

    private HttpClient CreateHttpClient(string? key = null)
    {
        var handler = new HttpClientHandler();
        if (string.Equals(BaseAddress.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var http = new HttpClient(new LiveContractEvidenceHandler(handler)) { BaseAddress = BaseAddress };
        if (key is not null)
        {
            http.DefaultRequestHeaders.Add("X-API-Key", key);
        }
        return http;
    }

    /// <summary>The independent verification oracle that reads server state back through canonical read APIs.</summary>
    public ServerStateVerifier CreateVerifier() =>
        new(BaseAddress, Options.StudioAdminApiKey);
}
