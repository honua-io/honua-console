using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web.Auth;

// honua-server accepts a proposal approval only from the operator's live server session (session
// cookie, CSRF token, review capability, no bearer or API key). Serving-endpoint approvals therefore
// go through the operator's server-session cookie jar; every other call keeps the bearer-bound client.
internal sealed class ConsoleSessionApprovalProposalsClient(
    IConsoleProposalsClient inner,
    IServiceProvider services) : IConsoleProposalsClient
{
    private const string ManagerPrefix = "manager:";

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
        var target = proposalId.StartsWith(ManagerPrefix, StringComparison.Ordinal)
            ? null
            : await ResolveSessionAsync(cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return await inner.ApproveAsync(proposalId, reviewed, cancellationToken).ConfigureAwait(false);
        }

        var (profile, partition) = target.Value;
        using var authentication = await partition.EnterAuthenticationAsync(cancellationToken).ConfigureAwait(false);
        var bound = await services.GetRequiredService<ConsoleServerSessionIdentityBinding>().EnsureAsync(
            partition, profile, partition.Key.OperatorKey, partition.AuthenticatedProviderKey, cancellationToken).ConfigureAwait(false);
        if (!bound)
        {
            return OperateSectionResult<ConsoleProposalDetail>.Denied(
                OperateSectionStatus.Forbidden, "The server session changed identity. Sign in again before approving.");
        }

        using var http = ConsoleServerSessionCookieClient.Create(partition, profile.ServerBaseUri);
        var client = new HttpConsoleProposalsClient(
            http,
            services.GetRequiredService<IConsoleEnvironmentProfileStore>(),
            services.GetRequiredService<IConsoleAccountSessionStore>());
        return await client.ApproveAsync(proposalId, reviewed, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(ConsoleEnvironmentProfile Profile, ConsoleServerSessionPartition Partition)?> ResolveSessionAsync(
        CancellationToken cancellationToken)
    {
        var operators = services.GetRequiredService<IConsoleOperatorContext>();
        var profile = await services.GetRequiredService<IConsoleEnvironmentProfileStore>()
            .GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null || !operators.HasOperator)
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
