using System.Net;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web.Auth;

// honua-server accepts a proposal approval only from the operator's live server session (session
// cookie, CSRF token, review capability, no bearer or API key). Serving approvals use the serving
// session. A separate management endpoint keeps its own session; manager: approvals use that cookie.
// Every other call keeps the bearer-bound client.
internal sealed class ConsoleSessionApprovalProposalsClient(
    IConsoleProposalsClient inner,
    IServiceProvider services) : IConsoleProposalsClient
{
    private const string ManagerPrefix = "manager:";
    private ConsoleEnvironmentProfile? ExpectedProfile { get; init; }

    public IConsoleProposalsClient ForEnvironment(ConsoleEnvironmentProfile profile) =>
        new ConsoleSessionApprovalProposalsClient(inner.ForEnvironment(profile), services) { ExpectedProfile = profile };

    public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null,
        string? kind = null,
        string? requestedBy = null,
        CancellationToken cancellationToken = default) =>
        inner.ListAsync(status, kind, requestedBy, cancellationToken);

    public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(
        string proposalId,
        CancellationToken cancellationToken = default) =>
        inner.GetAsync(proposalId, cancellationToken);

    public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
        string proposalId,
        string reason,
        CancellationToken cancellationToken = default) =>
        inner.RejectAsync(proposalId, reason, cancellationToken);

    public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
        string proposalId,
        CancellationToken cancellationToken = default) =>
        ApproveAsync(proposalId, reviewed: null, cancellationToken);

    public async Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
        string proposalId,
        ConsoleProposalReviewedPlan? reviewed,
        CancellationToken cancellationToken = default)
    {
        if (proposalId.StartsWith(ManagerPrefix, StringComparison.Ordinal))
        {
            // No manager session keeps the bearer client. The serving cookie cannot approve a manager proposal.
            var managerResult = await ApproveManagerAsync(proposalId, reviewed, cancellationToken).ConfigureAwait(false);
            return managerResult
                ?? await inner.ApproveAsync(proposalId, reviewed, cancellationToken).ConfigureAwait(false);
        }

        var target = await ResolveSessionAsync(cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return await inner.ApproveAsync(proposalId, reviewed, cancellationToken).ConfigureAwait(false);
        }

        var (profile, partition) = target.Value;
        using var http = ConsoleServerSessionCookieClient.Create(partition, profile.ServerBaseUri);
        // A server without the approval review predates the human-session rule and keeps the bearer path.
        // Probe before taking the partition lease: the bearer path takes that lease itself.
        var reviewUri = ConsoleServerHttp.BuildUri(profile.ServerBaseUri, ProposalAdminRoutes.ApprovalReview(proposalId.Trim()));
        if (!await OffersApprovalReviewAsync(http, reviewUri, cancellationToken).ConfigureAwait(false))
        {
            return await inner.ApproveAsync(proposalId, reviewed, cancellationToken).ConfigureAwait(false);
        }

        using var authentication = await partition.EnterAuthenticationAsync(cancellationToken).ConfigureAwait(false);
        var bound = await services.GetRequiredService<ConsoleServerSessionIdentityBinding>().EnsureAsync(
            partition, profile, partition.Key.OperatorKey, partition.AuthenticatedProviderKey, cancellationToken).ConfigureAwait(false);
        return bound
            ? await new HttpConsoleProposalsClient(
                    http,
                    new InMemoryConsoleEnvironmentProfileStore([profile]),
                    services.GetRequiredService<IConsoleAccountSessionStore>())
                .ApproveAsync(proposalId, reviewed, cancellationToken).ConfigureAwait(false)
            : OperateSectionResult<ConsoleProposalDetail>.Denied(
                OperateSectionStatus.Forbidden, "The server session changed identity. Sign in again before approving.");
    }

    // Null means there is no separate authenticated manager session, so the caller keeps the inner client.
    private async Task<OperateSectionResult<ConsoleProposalDetail>?> ApproveManagerAsync(
        string proposalId,
        ConsoleProposalReviewedPlan? reviewed,
        CancellationToken cancellationToken)
    {
        var operators = services.GetRequiredService<IConsoleOperatorContext>();
        var profiles = services.GetRequiredService<IConsoleEnvironmentProfileStore>();
        var profile = await profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var endpoint = services.GetService<IConsoleManagerEndpoint>();
        if (profile is null || !MatchesProfile(profile) || endpoint is null || !operators.HasOperator)
        {
            return null;
        }

        var manager = endpoint.Resolve(profile.ServerBaseUri);
        if (manager is null || ConsoleServerSessionBffCoordinator.SameServer(manager, profile.ServerBaseUri))
        {
            return null;
        }

        var store = services.GetRequiredService<ConsoleServerSessionClientStore>();
        if (!store.TryGet(operators.RequireOperatorKey(), profile.Id, manager, out var partition)
            || partition is null
            || string.IsNullOrWhiteSpace(partition.AuthenticatedProviderKey))
        {
            return null;
        }

        var id = proposalId[ManagerPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return OperateSectionResult<ConsoleProposalDetail>.Denied(
                OperateSectionStatus.Unavailable, "The proposal identifier is invalid.");
        }

        using var http = ConsoleServerSessionCookieClient.Create(partition, manager);
        var reviewUri = ConsoleServerHttp.BuildUri(manager, ProposalAdminRoutes.ApprovalReview(id));
        if (!await OffersApprovalReviewAsync(http, reviewUri, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        using var authentication = await partition.EnterAuthenticationAsync(cancellationToken).ConfigureAwait(false);
        var bound = await services.GetRequiredService<ConsoleServerSessionIdentityBinding>().EnsureAsync(
            partition, profile, partition.Key.OperatorKey, partition.AuthenticatedProviderKey, cancellationToken)
            .ConfigureAwait(false);
        if (!bound)
        {
            return OperateSectionResult<ConsoleProposalDetail>.Denied(
                OperateSectionStatus.Forbidden, "The management session changed identity. Sign in again before approving.");
        }

        var client = new HttpConsoleProposalsClient(
            http,
            new InMemoryConsoleEnvironmentProfileStore([profile]),
            services.GetRequiredService<IConsoleAccountSessionStore>());
        client.UseResourceBase(manager);
        var approved = await client.ApproveAsync(id, reviewed, cancellationToken).ConfigureAwait(false);
        return approved.Value is { } detail
            ? approved with { Value = detail with { ProposalId = ManagerPrefix + detail.ProposalId } }
            : approved;
    }

    private bool MatchesProfile(ConsoleEnvironmentProfile active) =>
        ExpectedProfile is not { } expected
        || (active.Id == expected.Id && active.ServerBaseUri == expected.ServerBaseUri);

    private static async Task<bool> OffersApprovalReviewAsync(
        HttpClient http,
        Uri reviewUri,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(reviewUri, cancellationToken).ConfigureAwait(false);
        return response.StatusCode != HttpStatusCode.NotFound;
    }

    private async Task<(ConsoleEnvironmentProfile Profile, ConsoleServerSessionPartition Partition)?> ResolveSessionAsync(
        CancellationToken cancellationToken)
    {
        var operators = services.GetRequiredService<IConsoleOperatorContext>();
        var profile = await services.GetRequiredService<IConsoleEnvironmentProfileStore>()
            .GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null || !MatchesProfile(profile) || !operators.HasOperator)
        {
            return null;
        }

        return services.GetRequiredService<ConsoleServerSessionClientStore>()
            .TryGet(operators.RequireOperatorKey(), profile, out var partition) && partition is not null
            ? (profile, partition)
            : null;
    }
}

// Sends through the partition's own client (its cookie jar, no bearer handler) and adds the server's
// CSRF token to unsafe requests. The outer request is copied because a request cannot be sent twice.
internal static class ConsoleServerSessionCookieClient
{
    public static HttpClient Create(ConsoleServerSessionPartition partition, Uri serverBaseUri) =>
        new(new PartitionHandler(partition.Client, serverBaseUri));

    private sealed class PartitionHandler(HttpClient partition, Uri serverBaseUri) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            using var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Content = request.Content };
            foreach (var header in request.Headers)
            {
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            {
                ConsoleServerCsrf.AddToken(copy, await ConsoleServerCsrf.TryGetTokenAsync(partition, serverBaseUri, cancellationToken)
                    .ConfigureAwait(false));
            }

            return await partition.SendAsync(copy, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
    }
}
