using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Components;

public partial class OperateDeploymentApprovalPanel
{
    private ConsoleDeployApprovalReview? _review;
    private int _reviewGeneration;

    private void CancelReview()
    {
        _review = null;
        _reviewGeneration++;
    }

    private async Task ApproveAsync()
    {
        if (_busy) return;
        if (_review is { } review)
        {
            _review = null;
            await RunActionAsync(() => ApprovalClient.SubmitAsync(review, _reason),
                "Approved — operation submitted for execution.");
            return;
        }

        _busy = true;
        _actionMessage = null;
        var generation = ++_reviewGeneration;
        var operationGeneration = _operationGeneration;
        try
        {
            var result = await ApprovalClient.OpenSubmitReviewAsync(OperationId);
            if (!IsCurrentOperation(operationGeneration) || generation != _reviewGeneration) return;
            if (result.IsAllowed && result.Value is { } opened)
            {
                _review = opened;
                await ApplyAsync(OperateSectionResult<DeployOperationProposal>.Allowed(opened.Operation));
            }
            else
            {
                _actionMessage = result.Message;
                _actionMessageClass = "console-state-danger";
            }
        }
        finally
        {
            if (IsCurrentOperation(operationGeneration)) _busy = false;
        }
    }
}
