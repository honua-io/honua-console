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
    IConsoleManagerEndpoint? management = null,
    ConsoleApprovalBrowserChannel? browserChannel = null) : IConsoleHumanProposalSession
{
    private const string CapabilityHeader = "X-Honua-Approval-Capability";
    private const int MaximumCapabilityLength = 16384;
    private static readonly TimeSpan ReviewLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromSeconds(30);

    public async Task<OperateSectionResult<ConsoleHumanProposalReview>> OpenReviewAsync(
        ConsoleHumanProposalTarget reviewRequest,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await OpenBoundReviewAsync(reviewRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JsonException)
        {
            return Unavailable<ConsoleHumanProposalReview>("The server returned an incomplete or invalid approval review. Check the server's approval contract before reopening it.");
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return Unavailable<ConsoleHumanProposalReview>("The private server session is unavailable. Sign in again and reopen the review.");
        }
    }

    private async Task<OperateSectionResult<ConsoleHumanProposalReview>> OpenBoundReviewAsync(
        ConsoleHumanProposalTarget target, CancellationToken cancellationToken)
    {
        var bound = await BindAsync(target.Profile, target.Authority, cancellationToken).ConfigureAwait(false);
        if (bound is null) return Denied<ConsoleHumanProposalReview>("The original proposal environment is unavailable.");
        using var session = await sessions.EnterPrivateSessionAsync(bound, target.Authority, cancellationToken).ConfigureAwait(false);
        var partition = session is null ? null : await session.VerifyBrowserAsync(
            new(target, bound, browserChannel), cancellationToken).ConfigureAwait(false);
        if (partition is null) return Denied<ConsoleHumanProposalReview>("Sign in to this server to review and approve the proposal.");
        partition.ProposalReviews.Discard(target.ProposalId);
        var request = new BoundReviewRequest(target, bound);
        using var response = await partition.Client.GetAsync(ConsoleServerHttp.BuildUri(
            bound.ServerBaseUri, ProposalAdminRoutes.Review(target.ProposalId)), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var refusal = Refused<ConsoleHumanProposalReview>(response.StatusCode);
            return refusal.Status == OperateSectionStatus.Unsupported ? refusal
                : await ReadWithoutApprovalAsync(partition, request with { Failure = refusal }, cancellationToken).ConfigureAwait(false);
        }
        var result = await response.Content.ReadFromJsonAsync(
            ConsoleHumanProposalJsonContext.Default.ConsolePrivateProposalReviewResponse, cancellationToken).ConfigureAwait(false);
        var current = await BindAsync(target.Profile, target.Authority, cancellationToken).ConfigureAwait(false);
        var verified = CreateVerifiedReview(result, request);
        if (verified is null || response.Headers.CacheControl?.NoStore != true || current is null
            || ConsoleServerProfileBinding.Capture(current) != verified.EnvironmentReference)
            return Unavailable<ConsoleHumanProposalReview>("The server review changed or could not be verified. Reopen the proposal.");
        partition.ProposalReviews.Remember(verified);
        return OperateSectionResult<ConsoleHumanProposalReview>.Allowed(new(
            result!.Proposal!, verified.EnvironmentReference, verified.ExpiresAt));
    }

    private ConsolePrivateProposalReview? CreateVerifiedReview(
        ConsolePrivateProposalReviewResponse? response, BoundReviewRequest request)
    {
        var now = clock.GetUtcNow();
        if (response is not { Proposal: { } proposal, Review: { } review }
            || proposal.ProposalId != request.Target.ProposalId || review.ProposalId != request.Target.ProposalId
            || proposal.Status != "AwaitingApproval" || proposal.Version != review.ProposalVersion
            || review.TenantId != (request.Profile.IsTenantScoped() ? request.Profile.EffectiveTenantId() : null)
            || string.IsNullOrEmpty(proposal.PlanHash) || proposal.PlanHash != review.PlanHash
            || string.IsNullOrEmpty(review.Capability) || review.Capability.Length > MaximumCapabilityLength
            || review.ExpiresAt <= now || review.ExpiresAt > now + ReviewLifetime + MaximumClockSkew)
            return null;
        var expiresAt = review.ExpiresAt < now + ReviewLifetime ? review.ExpiresAt : now + ReviewLifetime;
        return new ConsolePrivateProposalReview(proposal.ProposalId!, proposal.Version, proposal.PlanHash, review.Capability, expiresAt)
        {
            EnvironmentReference = ConsoleServerProfileBinding.Capture(request.Profile)
        };
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
            using var session = await sessions.EnterPrivateSessionAsync(bound, authority, cancellationToken).ConfigureAwait(false);
            var partition = session is null ? null : await session.VerifyBrowserAsync(
                new(new(profile, authority, decision.ProposalId), bound, browserChannel), cancellationToken).ConfigureAwait(false);
            if (partition is null) return Denied<ProposalDetailWire>("Sign in to this server and reopen the proposal before approving.");
            var currentBeforeSend = await BindAsync(profile, authority, cancellationToken).ConfigureAwait(false);
            if (currentBeforeSend is null || ConsoleServerProfileBinding.Capture(currentBeforeSend) != decision.EnvironmentReference)
                return Denied<ProposalDetailWire>("The environment changed. Reopen the proposal before approving.");
            var review = partition.ProposalReviews.Take(decision.ProposalId, decision.ProposalVersion, decision.PlanHash);
            if (review is null || review.EnvironmentReference != decision.EnvironmentReference)
                return Denied<ProposalDetailWire>("This review expired or was already submitted. Reload the proposal to check its status.");

            return await SendBoundApprovalAsync(partition, new BoundApprovalRequest(approvalRequest, bound, review), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return Unavailable<ProposalDetailWire>("The decision response is unavailable. Reload the proposal to check its status before deciding again.");
        }
    }

    private async Task<OperateSectionResult<ProposalDetailWire>> SendBoundApprovalAsync(
        ConsoleServerSessionPartition partition, BoundApprovalRequest approval, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            ConsoleServerHttp.BuildUri(approval.Profile.ServerBaseUri, ProposalAdminRoutes.Approve(approval.Target.Decision.ProposalId)));
        request.Headers.Add(CapabilityHeader, approval.Review.Capability);
        using var response = await partition.Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return Refused<ProposalDetailWire>(response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync(
            ConsoleHumanProposalJsonContext.Default.ProposalDetailWire, cancellationToken).ConfigureAwait(false);
        var current = await BindAsync(approval.Target.Profile, approval.Target.Authority, cancellationToken).ConfigureAwait(false);
        if (result?.ProposalId != approval.Target.Decision.ProposalId || current is null
            || ConsoleServerProfileBinding.Capture(current) != ConsoleServerProfileBinding.Capture(approval.Profile))
            return Unavailable<ProposalDetailWire>("The decision may have completed. Reload the original proposal to check its status.");
        return OperateSectionResult<ProposalDetailWire>.Allowed(result);
    }

    public async Task<OperateSectionResult<ProposalDetailWire>> ReadStatusAsync(
        ConsoleHumanProposalTarget target, CancellationToken cancellationToken = default)
    {
        try
        {
            var bound = await BindAsync(target.Profile, target.Authority, cancellationToken).ConfigureAwait(false);
            if (bound is null) return Denied<ProposalDetailWire>("The original proposal environment is unavailable.");
            using var session = await sessions.EnterPrivateSessionAsync(bound, target.Authority, cancellationToken).ConfigureAwait(false);
            var partition = session is null ? null : await session.VerifyBrowserAsync(
                new(target, bound, browserChannel), cancellationToken).ConfigureAwait(false);
            if (partition is null) return Denied<ProposalDetailWire>("Sign in to this server to read the original proposal status.");
            partition.ProposalReviews.Discard(target.ProposalId);
            var result = await ReadWithoutApprovalAsync(partition, new BoundReviewRequest(target, bound), cancellationToken).ConfigureAwait(false);
            return result.IsAllowed && result.Value is { } value
                ? OperateSectionResult<ProposalDetailWire>.Allowed(value.Proposal)
                : OperateSectionResult<ProposalDetailWire>.Denied(result.Status, result.Message, result.Detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return Unavailable<ProposalDetailWire>("The original proposal status is unavailable. Recheck it before deciding again.");
        }
    }

    private async Task<OperateSectionResult<ConsoleHumanProposalReview>> ReadWithoutApprovalAsync(
        ConsoleServerSessionPartition partition, BoundReviewRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await partition.Client.GetAsync(ConsoleServerHttp.BuildUri(
            request.Profile.ServerBaseUri, ProposalAdminRoutes.Detail(request.Target.ProposalId)), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return Refused<ConsoleHumanProposalReview>(response.StatusCode);
        var proposal = await response.Content.ReadFromJsonAsync(
            ConsoleHumanProposalJsonContext.Default.ProposalDetailWire, cancellationToken).ConfigureAwait(false);
        var current = await BindAsync(request.Target.Profile, request.Target.Authority, cancellationToken).ConfigureAwait(false);
        if (proposal?.ProposalId != request.Target.ProposalId || current is null
            || ConsoleServerProfileBinding.Capture(current) != ConsoleServerProfileBinding.Capture(request.Profile))
            return Unavailable<ConsoleHumanProposalReview>("The proposal environment changed. Reopen the original review.");
        return OperateSectionResult<ConsoleHumanProposalReview>.Allowed(new(proposal, string.Empty, DateTimeOffset.MinValue)
        {
            ApprovalMessage = proposal.ApprovalRemediation ?? request.Failure?.Message
                ?? "Refresh this review explicitly before approving."
        });
    }

    private sealed record BoundReviewRequest(ConsoleHumanProposalTarget Target, ConsoleEnvironmentProfile Profile)
    {
        public OperateSectionResult<ConsoleHumanProposalReview>? Failure { get; init; }
    }

    private sealed record BoundApprovalRequest(
        ConsoleHumanProposalApprovalRequest Target, ConsoleEnvironmentProfile Profile, ConsolePrivateProposalReview Review);

    private async Task<ConsoleEnvironmentProfile?> BindAsync(
        ConsoleEnvironmentProfile expected, ConsoleServerAuthority authority, CancellationToken cancellationToken)
    {
        var current = await profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (current is null || ConsoleServerProfileBinding.Capture(current) != ConsoleServerProfileBinding.Capture(expected)) return null;
        return ConsoleManagementBinding.Resolve(management, current.ServerBaseUri).ForAuthority(current, authority);
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
