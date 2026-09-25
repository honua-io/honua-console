using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

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
    private readonly List<Guid> _managedKeyIds = [];

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
            _reviewerKey = await CreateActorAsync(bootstrap, "reviewer", "admin:approve");
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            try
            {
                using var bootstrap = CreateHttpClient(Options.StudioAdminApiKey);
                foreach (var id in _managedKeyIds)
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

    /// <summary>Explicit test action: assert self-approval denial, then have a distinct scoped reviewer act.</summary>
    public async Task<StudioPublicationRefresh> ApprovePublicationAsDistinctActorAsync(
        StudioPendingPublication pending, bool expectValidationFailure = false)
    {
        Assert.NotNull(_container); // Never provision or approve through a configured external deployment.
        Assert.NotNull(_proposerKey);
        Assert.NotNull(_reviewerKey);
        using var lifecycle = (HttpStudioPackageLifecycleClient)CreateClient();
        var before = await lifecycle.GetContentItemPointersAsync(pending.ItemId);
        Assert.True(before.IsSuccess, before.Issue?.Detail);
        Assert.NotNull(before.Data);
        Assert.NotEqual(pending.VersionId, before.Data.PublishedVersionId);

        using var proposerHttp = CreateHttpClient();
        var proposer = CreateProposalsClient(proposerHttp, _proposerKey);
        var selfApproval = await proposer.ApproveAsync(pending.Operation.ProposalId!);
        Assert.Equal(OperateSectionStatus.Forbidden, selfApproval.Status);
        Assert.Contains("separation", selfApproval.Message + " " + selfApproval.Detail, StringComparison.OrdinalIgnoreCase);
        var stillPending = await proposer.GetAsync(pending.Operation.ProposalId!);
        Assert.True(stillPending.IsAllowed, stillPending.Message);
        Assert.Equal(ConsoleProposalStatus.AwaitingApproval, stillPending.Value!.Status);
        var afterSelf = await lifecycle.GetContentItemPointersAsync(pending.ItemId);
        Assert.True(afterSelf.IsSuccess, afterSelf.Issue?.Detail);
        Assert.Equal(before.Data.PublishedVersionId, afterSelf.Data!.PublishedVersionId);

        using var reviewerHttp = CreateHttpClient();
        var reviewer = CreateProposalsClient(reviewerHttp, _reviewerKey);
        var decision = await reviewer.ApproveAsync(pending.Operation.ProposalId!);
        Assert.True(decision.IsAllowed, decision.Message);
        Assert.NotNull(decision.Value);
        Assert.False(string.IsNullOrWhiteSpace(decision.Value.RequestedBy));
        Assert.False(string.IsNullOrWhiteSpace(decision.Value.ResolvedBy));
        Assert.NotEqual(decision.Value.RequestedBy, decision.Value.ResolvedBy);
        Assert.Equal(expectValidationFailure ? ConsoleProposalStatus.Failed : ConsoleProposalStatus.Succeeded, decision.Value.Status);

        var observed = await new StudioPublicationStatusReader(lifecycle, proposer).RefreshAsync(pending);
        Assert.True(observed.Succeeded, observed.Issue);
        if (expectValidationFailure)
        {
            Assert.Equal(before.Data.PublishedVersionId, observed.Pointers!.PublishedVersionId);
            Assert.NotEqual(pending.VersionId, observed.Pointers.PublishedVersionId);
        }
        else
        {
            Assert.Equal(pending.VersionId, observed.Pointers!.PublishedVersionId);
            Assert.Equal(pending.VersionId, observed.PublishedVersion!.VersionId);
        }

        return observed;
    }

    private async Task<string> CreateActorAsync(HttpClient bootstrap, string role, string permission)
    {
        using var response = await bootstrap.PostAsJsonAsync("/api/v1/admin/api-keys", new
        {
            name = $"console-publication-{role}-{Guid.NewGuid():N}",
            permissions = new[] { permission },
            expiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        _managedKeyIds.Add(data.GetProperty("apiKey").GetProperty("id").GetGuid());
        return data.GetProperty("key").GetString()!;
    }

    private HttpConsoleProposalsClient CreateProposalsClient(HttpClient http, string key)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "publication-fixture", DisplayName = "Ephemeral publication fixture", ServerBaseUri = BaseAddress,
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
