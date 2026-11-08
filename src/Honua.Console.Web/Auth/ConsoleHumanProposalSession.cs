using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web.Auth;

internal sealed class ConsoleHumanProposalSession(
    PartitionedConsoleOperatorBearerExchange sessions,
    IConsoleEnvironmentProfileStore profiles,
    TimeProvider clock,
    IConsoleManagerEndpoint? management = null) : IConsoleHumanProposalSession
{
    private const string CapabilityHeader = "X-Honua-Approval-Capability";

    public async Task<OperateSectionResult<ConsoleHumanProposalReview>> OpenReviewAsync(
        ConsoleHumanProposalReviewRequest reviewRequest,
        CancellationToken cancellationToken = default)
    {
        var (profile, authority, proposalId) = reviewRequest;
        try
        {
            var bound = await BindAsync(profile, authority, cancellationToken).ConfigureAwait(false);
            if (bound is null) return Denied<ConsoleHumanProposalReview>("The original proposal environment is unavailable.");
            using var session = await sessions.EnterPrivateSessionAsync(bound, cancellationToken).ConfigureAwait(false);
            var partition = session is null ? null : await session.VerifyAsync(bound, ConsoleServerSessionUse.HumanDecision, cancellationToken).ConfigureAwait(false);
            if (partition is null) return Denied<ConsoleHumanProposalReview>("Sign in to this server to review and approve the proposal.");

            using var request = new HttpRequestMessage(HttpMethod.Get,
                ConsoleServerHttp.BuildUri(bound.ServerBaseUri, ProposalAdminRoutes.Detail(proposalId) + "/approval-review"));
            using var response = await partition.Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return await ReadWithoutApprovalAsync(partition, reviewRequest, cancellationToken).ConfigureAwait(false);
            var result = await response.Content.ReadFromJsonAsync(
                ConsoleHumanProposalJsonContext.Default.ConsolePrivateProposalReviewResponse, cancellationToken).ConfigureAwait(false);
            var current = await BindAsync(profile, authority, cancellationToken).ConfigureAwait(false);
            if (result is not { Proposal: { } proposal, Review: { } review }
                || proposal.ProposalId != proposalId || review.ProposalId != proposalId
                || proposal.Status != "AwaitingApproval" || proposal.Version != review.ProposalVersion
                || string.IsNullOrEmpty(proposal.PlanHash) || proposal.PlanHash != review.PlanHash
                || string.IsNullOrEmpty(review.Capability) || review.Capability.Length > 16384
                || review.ExpiresAt <= clock.GetUtcNow()
                || review.ExpiresAt > clock.GetUtcNow().AddMinutes(5)
                || response.Headers.CacheControl?.NoStore != true
                || current is null || ConsoleServerProfileBinding.Capture(current) != ConsoleServerProfileBinding.Capture(bound))
                return Unavailable<ConsoleHumanProposalReview>("The server review changed or could not be verified. Reopen the proposal.");

            var environment = ConsoleServerProfileBinding.Capture(bound);
            partition.ProposalReviews.Remember(new ConsolePrivateProposalReview(
                proposalId, proposal.Version, proposal.PlanHash, review.Capability, review.ExpiresAt)
            {
                EnvironmentReference = environment
            });
            return OperateSectionResult<ConsoleHumanProposalReview>.Allowed(new(proposal, environment, review.ExpiresAt));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return Unavailable<ConsoleHumanProposalReview>("The private server session is unavailable. Sign in again and reopen the review.");
        }
    }

    public async Task<OperateSectionResult<ProposalDetailWire>> ApproveAsync(
        ConsoleHumanProposalApprovalRequest approvalRequest,
        CancellationToken cancellationToken = default)
    {
        var (profile, authority, decision) = approvalRequest;
        try
        {
            var bound = await BindAsync(profile, authority, cancellationToken).ConfigureAwait(false);
            if (bound is null || string.IsNullOrEmpty(decision.PlanHash)
                || decision.EnvironmentReference != ConsoleServerProfileBinding.Capture(bound))
                return Denied<ProposalDetailWire>("The environment changed. Reopen the proposal before approving.");
            using var session = await sessions.EnterPrivateSessionAsync(bound, cancellationToken).ConfigureAwait(false);
            var partition = session is null ? null : await session.VerifyAsync(bound, ConsoleServerSessionUse.HumanDecision, cancellationToken).ConfigureAwait(false);
            if (partition is null) return Denied<ProposalDetailWire>("Sign in to this server and reopen the proposal before approving.");
            var currentBeforeSend = await BindAsync(profile, authority, cancellationToken).ConfigureAwait(false);
            if (currentBeforeSend is null || ConsoleServerProfileBinding.Capture(currentBeforeSend) != decision.EnvironmentReference)
                return Denied<ProposalDetailWire>("The environment changed. Reopen the proposal before approving.");
            var review = partition.ProposalReviews.Take(decision.ProposalId, decision.ProposalVersion, decision.PlanHash);
            if (review is null || review.EnvironmentReference != decision.EnvironmentReference)
                return Denied<ProposalDetailWire>("This review expired or was already submitted. Reload the proposal to check its status.");

            using var request = new HttpRequestMessage(HttpMethod.Post,
                ConsoleServerHttp.BuildUri(bound.ServerBaseUri, ProposalAdminRoutes.Approve(decision.ProposalId)));
            request.Headers.Add(CapabilityHeader, review.Capability);
            using var response = await partition.Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return Refused<ProposalDetailWire>(response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync(
                ConsoleHumanProposalJsonContext.Default.ProposalDetailWire, cancellationToken).ConfigureAwait(false);
            var current = await BindAsync(profile, authority, cancellationToken).ConfigureAwait(false);
            if (result?.ProposalId != decision.ProposalId
                || current is null || ConsoleServerProfileBinding.Capture(current) != ConsoleServerProfileBinding.Capture(bound))
                return Unavailable<ProposalDetailWire>("The decision may have completed. Reload the original proposal to check its status.");
            return OperateSectionResult<ProposalDetailWire>.Allowed(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return Unavailable<ProposalDetailWire>("The decision response is unavailable. Reload the proposal to check its status before deciding again.");
        }
    }

    private async Task<OperateSectionResult<ConsoleHumanProposalReview>> ReadWithoutApprovalAsync(
        ConsoleServerSessionPartition partition, ConsoleHumanProposalReviewRequest reviewRequest,
        CancellationToken cancellationToken)
    {
        var (profile, authority, proposalId) = reviewRequest;
        partition.ProposalReviews.Discard(proposalId);
        using var response = await partition.Client.GetAsync(ConsoleServerHttp.BuildUri(
            new Uri(partition.Key.ServerOrigin), ProposalAdminRoutes.Detail(proposalId)), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return Refused<ConsoleHumanProposalReview>(response.StatusCode);
        var proposal = await response.Content.ReadFromJsonAsync(
            ConsoleHumanProposalJsonContext.Default.ProposalDetailWire, cancellationToken).ConfigureAwait(false);
        var current = await BindAsync(profile, authority, cancellationToken).ConfigureAwait(false);
        if (proposal?.ProposalId != proposalId || current is null
            || ConsoleServerSessionClientStore.NormalizeOrigin(current.ServerBaseUri) != new Uri(partition.Key.ServerOrigin))
            return Unavailable<ConsoleHumanProposalReview>("The proposal environment changed. Reopen the original review.");
        return OperateSectionResult<ConsoleHumanProposalReview>.Allowed(new(proposal, string.Empty, DateTimeOffset.MinValue)
        {
            ApprovalMessage = proposal.ReproposalReason ?? "Sign in with approval permission and refresh this review to approve."
        });
    }

    private async Task<ConsoleEnvironmentProfile?> BindAsync(
        ConsoleEnvironmentProfile expected, ConsoleServerAuthority authority, CancellationToken cancellationToken)
    {
        var current = await profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (current is null || ConsoleServerProfileBinding.Capture(current) != ConsoleServerProfileBinding.Capture(expected)) return null;
        var target = authority == ConsoleServerAuthority.Management
            ? management?.Resolve(current.ServerBaseUri) : current.ServerBaseUri;
        return target is null ? null : current with { ServerBaseUri = target };
    }

    private static bool IsUnavailable(Exception exception) => exception is HttpRequestException or JsonException
        or InvalidOperationException or ArgumentException or OperationCanceledException;

    private static OperateSectionResult<T> Denied<T>(string message) =>
        OperateSectionResult<T>.Denied(OperateSectionStatus.Forbidden, message);

    private static OperateSectionResult<T> Unavailable<T>(string message) =>
        OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, message);

    private static OperateSectionResult<T> Refused<T>(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Denied<T>(
            "The server requires an authorized live human session. Sign in again and reopen this review."),
        HttpStatusCode.NotFound or HttpStatusCode.NotImplemented => OperateSectionResult<T>.Denied(
            OperateSectionStatus.Unsupported, "This server does not support private human proposal review."),
        HttpStatusCode.Conflict => Unavailable<T>("This review requires a fresh proposal or has already been resolved. Reload its status."),
        _ => Unavailable<T>("The server could not complete this review. Reload the proposal to check its status.")
    };
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ConsolePrivateProposalReviewResponse))]
[JsonSerializable(typeof(ProposalDetailWire))]
internal sealed partial class ConsoleHumanProposalJsonContext : JsonSerializerContext;
