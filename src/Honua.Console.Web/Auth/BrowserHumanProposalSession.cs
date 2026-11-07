using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.JSInterop;

namespace Honua.Console.Web.Auth;

internal sealed class BrowserHumanProposalSession(IJSRuntime javascript) : IConsoleHumanProposalSession, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public Task<OperateSectionResult<ConsoleHumanProposalReview>> OpenReviewAsync(
        ConsoleEnvironmentProfile profile, ConsoleServerAuthority authority, string proposalId,
        CancellationToken cancellationToken = default) => SendAsync<ConsoleHumanProposalReview>(
            "review", profile.Id, authority, proposalId, null, cancellationToken);

    public Task<OperateSectionResult<ProposalDetailWire>> ApproveAsync(
        ConsoleEnvironmentProfile profile, ConsoleServerAuthority authority, ConsoleProposalDecision decision,
        CancellationToken cancellationToken = default) => SendAsync<ProposalDetailWire>(
            "approve", profile.Id, authority, decision.ProposalId, decision, cancellationToken);

    private async Task<OperateSectionResult<T>> SendAsync<T>(
        string action, string profileId, ConsoleServerAuthority authority, string proposalId,
        ConsoleProposalDecision? decision, CancellationToken cancellationToken)
    {
        try
        {
            _module ??= await javascript.InvokeAsync<IJSObjectReference>(
                "import", cancellationToken, "./interop/human-proposals.js").ConfigureAwait(false);
            return await _module.InvokeAsync<OperateSectionResult<T>>(
                "decide", cancellationToken, action, profileId, authority, proposalId, decision).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or OperationCanceledException)
        {
            return OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable,
                "Connect the approval controls, then reload the proposal to check its status.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try { await _module.DisposeAsync().ConfigureAwait(false); }
        catch (JSDisconnectedException) { }
    }
}
