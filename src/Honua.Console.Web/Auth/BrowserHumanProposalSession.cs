using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.JSInterop;

namespace Honua.Console.Web.Auth;

internal sealed class BrowserHumanProposalSession(IJSRuntime javascript) : IConsoleHumanProposalSession, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public Task<OperateSectionResult<ConsoleHumanProposalReview>> OpenReviewAsync(
        ConsoleHumanProposalTarget request,
        CancellationToken cancellationToken = default) => SendAsync<ConsoleHumanProposalReview>(
            new BrowserHumanProposalCommand
            {
                Action = "review",
                ProfileId = request.Profile.Id,
                Authority = request.Authority,
                ProposalId = request.ProposalId,
            }, cancellationToken);

    public Task<OperateSectionResult<ProposalDetailWire>> ApproveAsync(
        ConsoleHumanProposalApprovalRequest request,
        CancellationToken cancellationToken = default) => SendAsync<ProposalDetailWire>(
            new BrowserHumanProposalCommand
            {
                Action = "approve",
                ProfileId = request.Profile.Id,
                Authority = request.Authority,
                ProposalId = request.Decision.ProposalId,
                Decision = request.Decision,
            }, cancellationToken);

    public Task<OperateSectionResult<ProposalDetailWire>> ReadStatusAsync(
        ConsoleHumanProposalTarget request,
        CancellationToken cancellationToken = default) => SendAsync<ProposalDetailWire>(new BrowserHumanProposalCommand
        {
            Action = "status",
            ProfileId = request.Profile.Id,
            Authority = request.Authority,
            ProposalId = request.ProposalId
        }, cancellationToken);

    private async Task<OperateSectionResult<T>> SendAsync<T>(
        BrowserHumanProposalCommand command, CancellationToken cancellationToken)
    {
        try
        {
            _module ??= await javascript.InvokeAsync<IJSObjectReference>(
                "import", cancellationToken, "./interop/human-proposals.js").ConfigureAwait(false);
            return await _module.InvokeAsync<OperateSectionResult<T>>(
                "decide", cancellationToken, command).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or InvalidOperationException or OperationCanceledException)
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

internal sealed record BrowserHumanProposalCommand
{
    public required string Action { get; init; }
    public required string ProfileId { get; init; }
    public required ConsoleServerAuthority Authority { get; init; }
    public required string ProposalId { get; init; }
    public ConsoleProposalDecision? Decision { get; init; }
}
