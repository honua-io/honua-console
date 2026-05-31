using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the GitOps metadata release visualization surface to a real honua-server
/// through the SHIPPED GitOps metadata release contracts: the release package and
/// GitOps-safe manifest export (honua-server#1163, <c>GET /api/v1/admin/metadata/
/// release-packages/{packageId}</c> and <c>.../gitops-manifest</c>) and the release-
/// operation lifecycle / rollback plan (honua-server#1165, <c>GET /api/v1/admin/
/// metadata/releases/{packageId}/operation</c>).
///
/// Base address comes from the active <see cref="ConsoleEnvironmentProfile"/>; the
/// admin API key is sent as <c>X-API-Key</c> (the metadata release endpoints are
/// admin-authorized). A single shared <see cref="HttpClient"/> is reused; each
/// request builds an absolute URI and attaches its own key. Deserialization is
/// source-generated for trim/AOT safety.
///
/// Charter section 11 (no standing mock for server-owned data) is preserved: this
/// client never returns seeded release data. When no environment profile is bound,
/// every read returns a missing-binding result. The server exposes NO release-package
/// list endpoint (packages are addressed by id), so <see cref="GetReleaseProposalsAsync"/>
/// honestly reports the list surface as unsupported by the connected server rather
/// than fabricating a list; the by-id detail read is fully bound to live data.
/// </summary>
public sealed class HttpConsoleGitOpsReleaseClient : IConsoleGitOpsReleaseClient
{
    private const string NoListEndpointMessage =
        "The connected honua-server addresses GitOps metadata release packages by id and does " +
        "not expose a release-package list endpoint. Open a release by its package id to view " +
        "its proposal, environment matrix, CI/GitOps timeline, and rollback readiness.";

    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to load GitOps releases.";

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly string? _adminApiKey;

    public HttpConsoleGitOpsReleaseClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profileStore,
        string? adminApiKey = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
    }

    public async Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Denied(
                OperateSectionStatus.Unavailable,
                NoProfileMessage);
        }

        // The shipped server contract has no release-package list endpoint; surface
        // that honestly rather than returning a standing mock list (charter §11).
        return OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Denied(
            OperateSectionStatus.Unsupported,
            NoListEndpointMessage);
    }

    public async Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(
        string releasePackageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releasePackageId);

        var detail = await GetReleaseDetailAsync(releasePackageId, cancellationToken).ConfigureAwait(false);
        return detail.IsAllowed
            ? OperateSectionResult<GitOpsReleaseProposal>.Allowed(detail.Value!.Proposal)
            : OperateSectionResult<GitOpsReleaseProposal>.Denied(detail.Status, detail.Message);
    }

    public async Task<OperateSectionResult<GitOpsReleaseDetail>> GetReleaseDetailAsync(
        string releasePackageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releasePackageId);

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<GitOpsReleaseDetail>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        var trimmed = releasePackageId.Trim();
        if (!Guid.TryParse(trimmed, out var packageId))
        {
            return OperateSectionResult<GitOpsReleaseDetail>.Denied(
                OperateSectionStatus.Missing,
                $"Release '{trimmed}' is not a valid release package id.");
        }

        // The release package (proposal + semantic diff) is the primary read; without
        // it there is nothing to visualize, so its failure fails the whole detail read.
        var packageFetch = await FetchAsync(
            profile.ServerBaseUri,
            MetadataReleaseAdminRoutes.ReleasePackage(packageId),
            MetadataReleaseJsonContext.Default.MetadataReleasePackageResponse,
            cancellationToken).ConfigureAwait(false);
        if (!packageFetch.Ok)
        {
            return OperateSectionResult<GitOpsReleaseDetail>.Denied(packageFetch.Status, packageFetch.Message);
        }

        // The GitOps manifest and the operation lifecycle are independent sub-reads;
        // load them in parallel and degrade gracefully if either is absent.
        var manifestTask = FetchAsync(
            profile.ServerBaseUri,
            MetadataReleaseAdminRoutes.GitOpsManifest(packageId),
            MetadataReleaseJsonContext.Default.GitOpsMetadataReleaseManifestResponse,
            cancellationToken);
        var operationTask = FetchAsync(
            profile.ServerBaseUri,
            MetadataReleaseAdminRoutes.ReleaseOperation(trimmed),
            MetadataReleaseJsonContext.Default.DeployOperationResponse,
            cancellationToken);
        await Task.WhenAll(manifestTask, operationTask).ConfigureAwait(false);

        var manifestFetch = await manifestTask.ConfigureAwait(false);
        var operationFetch = await operationTask.ConfigureAwait(false);

        var package = packageFetch.Value!;
        var manifest = manifestFetch.Ok ? manifestFetch.Value : null;

        var proposal = GitOpsReleaseMapper.MapProposal(package, manifest);
        var matrix = GitOpsReleaseMapper.MapEnvironmentMatrix(package);
        var dataScripts = GitOpsReleaseMapper.MapDataScripts(package);

        GitOpsReleaseOperation? operation = null;
        if (operationFetch.Ok)
        {
            operation = GitOpsReleaseMapper.MapOperation(operationFetch.Value!);

            // The rollback classification lives on the operation's rollback plan, so
            // promote it onto the proposal once the lifecycle is bound, and let the
            // operation lifecycle's own blockers also gate the PR/deploy action.
            if (operation.RollbackPlan is { } plan)
            {
                proposal = proposal with { RollbackClassification = plan.Classification };
            }

            if (operation.HasBlockers)
            {
                proposal = proposal with { HasBlockingFindings = true };
            }
        }

        var detail = new GitOpsReleaseDetail(
            Proposal: proposal,
            EnvironmentMatrix: matrix,
            Operation: operation,
            OperationStatus: operationFetch.Status,
            OperationMessage: operationFetch.Ok
                ? string.Empty
                : OperationMessage(operationFetch.Status, operationFetch.Message),
            DataScripts: dataScripts);

        return OperateSectionResult<GitOpsReleaseDetail>.Allowed(detail);
    }

    private static string OperationMessage(OperateSectionStatus status, string message) => status switch
    {
        OperateSectionStatus.Missing =>
            "No release operation has been started for this package yet. The CI/GitOps timeline " +
            "and rollback plan appear once a release operation is created (honua-server#1165).",
        OperateSectionStatus.Forbidden =>
            "The active environment profile is not permitted to read the release operation lifecycle.",
        OperateSectionStatus.Unsupported =>
            "The connected server does not expose the release-operation lifecycle (honua-server#1165).",
        _ => string.IsNullOrWhiteSpace(message)
            ? "The release operation lifecycle is temporarily unavailable."
            : message
    };

    private async Task<FetchResult<T>> FetchAsync<T>(
        Uri baseUri,
        string relativePath,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(baseUri, relativePath));
        if (!string.IsNullOrWhiteSpace(_adminApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _adminApiKey);
        }

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return FetchResult<T>.Failed(
                    MapStatus(response.StatusCode),
                    $"The honua-server admin API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
            return value is null
                ? FetchResult<T>.Failed(OperateSectionStatus.Unavailable, "The honua-server admin API returned an empty response.")
                : FetchResult<T>.Succeeded(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return FetchResult<T>.Failed(
                OperateSectionStatus.Unavailable,
                "The honua-server admin API is unreachable or returned an unreadable response.");
        }
    }

    private static Uri BuildUri(Uri baseUri, string relativePath)
    {
        var normalizedBase = baseUri.AbsoluteUri.EndsWith('/')
            ? baseUri
            : new Uri(baseUri.AbsoluteUri + "/", UriKind.Absolute);
        return new Uri(normalizedBase, relativePath);
    }

    private static OperateSectionStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound => OperateSectionStatus.Missing,
        HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable
    };

    private sealed class FetchResult<T>
        where T : class
    {
        public OperateSectionStatus Status { get; private init; }

        public T? Value { get; private init; }

        public string Message { get; private init; } = string.Empty;

        public bool Ok => Status == OperateSectionStatus.Allowed && Value is not null;

        public static FetchResult<T> Succeeded(T value) =>
            new() { Status = OperateSectionStatus.Allowed, Value = value };

        public static FetchResult<T> Failed(OperateSectionStatus status, string message) =>
            new() { Status = status, Message = message };
    }
}
