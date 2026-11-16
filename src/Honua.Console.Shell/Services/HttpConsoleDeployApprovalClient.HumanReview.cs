using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed partial class HttpConsoleDeployApprovalClient
{
    private sealed record ReviewTarget(ConsoleEnvironmentProfile Profile, string OperationId);

    private const string ReviewAgainMessage = "The deployment or session changed, or the review expired. Review the deployment again before approving.";

    public async Task<OperateSectionResult<ConsoleDeployApprovalReview>> OpenSubmitReviewAsync(
        string operationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (!await HasHumanTransportAsync(profile, cancellationToken).ConfigureAwait(false))
        {
            return OperateSectionResult<ConsoleDeployApprovalReview>.Denied(
                OperateSectionStatus.Forbidden, "Sign in through the connected server's browser session to review this deployment.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get,
            BuildUri(profile!.ServerBaseUri, DeployControlAdminRoutes.Operation(operationId.Trim()) + "/approval-review/submit"));
        request.Options.Set(ConsoleAuthorityRequestOptions.Capture, true);
        var result = await ExchangeHumanAsync(request, DeployHumanReviewJsonContext.Default.DeployHumanReviewResponse,
            profile!, cancellationToken).ConfigureAwait(false);
        if (!result.IsAllowed || result.Value is not { } response)
        {
            return OperateSectionResult<ConsoleDeployApprovalReview>.Denied(result.Status, result.Message);
        }

        return BuildReview(response, request, new ReviewTarget(profile!, operationId.Trim()));
    }

    private static OperateSectionResult<ConsoleDeployApprovalReview> BuildReview(
        DeployHumanReviewResponse response, HttpRequestMessage request, ReviewTarget target)
    {
        if (response.Operation is null || response.Review is null || response.Action != "submit"
            || response.Operation.OperationId != target.OperationId || response.Review.ProposalId != target.OperationId
            || response.Review.ExpiresAt <= DateTimeOffset.UtcNow || string.IsNullOrWhiteSpace(response.Review.Capability)
            || !request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Resolved, out var binding)
            || string.IsNullOrWhiteSpace(binding))
        {
            return OperateSectionResult<ConsoleDeployApprovalReview>.Denied(OperateSectionStatus.Unavailable, ReviewAgainMessage);
        }

        return OperateSectionResult<ConsoleDeployApprovalReview>.Allowed(new(
            DeployApprovalMapper.MapProposal(response.Operation), response.Review.ExpiresAt, new ConsoleDeployReviewAuthority
            {
                Capability = response.Review.Capability,
                ProfileId = target.Profile.Id,
                ServerBaseUri = target.Profile.ServerBaseUri,
                Binding = binding
            }));
    }

    public async Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
        ConsoleDeployApprovalReview review, string? reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null || profile.Id != review.ProfileId || profile.ServerBaseUri != review.ServerBaseUri
            || review.ExpiresAt <= DateTimeOffset.UtcNow || string.IsNullOrWhiteSpace(review.AuthorityBinding)
            || !await HasHumanTransportAsync(profile, cancellationToken).ConfigureAwait(false) || !review.TryConsume())
        {
            return OperateSectionResult<DeployOperationProposal>.Denied(OperateSectionStatus.Forbidden, ReviewAgainMessage);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post,
            BuildUri(profile.ServerBaseUri, DeployControlAdminRoutes.Submit(review.Operation.OperationId)));
        request.Options.Set(ConsoleAuthorityRequestOptions.Expected, review.AuthorityBinding);
        request.Headers.Add(ProposalAdminRoutes.ApprovalCapabilityHeader, review.Capability);
        request.Content = new StringContent(JsonSerializer.Serialize(
            new SubmitDeployOperationRequest { Reason = NormalizeReason(reason) },
            DeployControlJsonContext.Default.SubmitDeployOperationRequest), Encoding.UTF8, "application/json");
        var result = await ExchangeHumanAsync(request, MetadataReleaseJsonContext.Default.DeployOperationResponse,
            profile, cancellationToken).ConfigureAwait(false);
        return result.IsAllowed && result.Value is { } operation
            ? OperateSectionResult<DeployOperationProposal>.Allowed(DeployApprovalMapper.MapProposal(operation))
            : OperateSectionResult<DeployOperationProposal>.Denied(result.Status, result.Message);
    }

    private async Task<bool> HasHumanTransportAsync(ConsoleEnvironmentProfile? profile, CancellationToken cancellationToken)
    {
        if (_authority is null || profile is null || _credentialMode != ConsoleServerCredentialMode.Interactive) return false;
        var authority = await _operatorBearerProvider.ResolveAsync(profile, cancellationToken).ConfigureAwait(false);
        return authority.HasInteractiveSession && authority.UsesBrowserTransport;
    }

    private async Task<OperateSectionResult<T>> ExchangeHumanAsync<T>(
        HttpRequestMessage request, JsonTypeInfo<T> type, ConsoleEnvironmentProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return OperateSectionResult<T>.Denied(MapStatus(response.StatusCode), ReviewAgainMessage);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(stream, type, cancellationToken).ConfigureAwait(false);
            var active = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
            var current = active?.Id == profile.Id && active.ServerBaseUri == profile.ServerBaseUri
                && request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Resolved, out var binding)
                && _authority is not null && await _authority.IsCurrentAsync(binding, cancellationToken).ConfigureAwait(false);
            return value is null || !current
                ? OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, ReviewAgainMessage)
                : OperateSectionResult<T>.Allowed(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, ReviewAgainMessage);
        }
    }
}
