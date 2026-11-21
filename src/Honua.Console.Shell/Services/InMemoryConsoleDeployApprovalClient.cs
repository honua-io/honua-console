using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// Test-only. This is not the DI default. A data-affecting rollback requires the caller to confirm first; this simulator does not enforce that confirmation.
public sealed class InMemoryConsoleDeployApprovalClient : IConsoleDeployApprovalClient
{
    private readonly Dictionary<string, DeployOperationProposal> _operations;

    public InMemoryConsoleDeployApprovalClient(IEnumerable<DeployOperationProposal>? seed = null)
    {
        _operations = (seed ?? [])
            .ToDictionary(op => op.OperationId, StringComparer.Ordinal);
    }

    public Task<OperateSectionResult<DeployOperationProposal>> GetOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            _operations.TryGetValue(operationId.Trim(), out var op)
                ? OperateSectionResult<DeployOperationProposal>.Allowed(op)
                : OperateSectionResult<DeployOperationProposal>.Denied(
                    OperateSectionStatus.Missing,
                    $"No deploy-control operation '{operationId}' is known to this in-memory client."));
    }

    public Task<OperateSectionResult<IReadOnlyList<DeployOperationProposal>>> ListPendingAsync(
        IReadOnlyList<string> operationIds,
        CancellationToken cancellationToken = default)
    {
        var pending = operationIds
            .Select(id => id.Trim())
            .Where(id => _operations.ContainsKey(id))
            .Select(id => _operations[id])
            .Where(op => !op.IsTerminal)
            .OrderByDescending(op => op.IsAwaitingApproval)
            .ThenByDescending(op => op.UpdatedAt)
            .ToArray();

        return Task.FromResult(
            OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Allowed(pending));
    }

    public async Task<OperateSectionResult<ConsoleDeployApprovalReview>> OpenSubmitReviewAsync(
        string operationId, CancellationToken cancellationToken = default)
    {
        var result = await GetOperationAsync(operationId, cancellationToken);
        return result.IsAllowed && result.Value is { } op
            ? OperateSectionResult<ConsoleDeployApprovalReview>.Allowed(new(op, DateTimeOffset.UtcNow.AddMinutes(5), new ConsoleDeployReviewAuthority()))
            : OperateSectionResult<ConsoleDeployApprovalReview>.Denied(result.Status, result.Message);
    }

    public Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
        ConsoleDeployApprovalReview review,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var operationId = review.Operation.OperationId;
        if (!review.TryConsume() || review.ExpiresAt <= DateTimeOffset.UtcNow || !_operations.TryGetValue(operationId, out var current)
            || current != review.Operation)
        {
            return Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(
                OperateSectionStatus.Unavailable, "The deployment changed or the review expired. Review it again."));
        }

        if (!_operations.TryGetValue(operationId.Trim(), out var op))
        {
            return Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(
                OperateSectionStatus.Missing,
                $"No deploy-control operation '{operationId}' is known to this in-memory client."));
        }

        if (op.Lifecycle != DeployOperationLifecycle.AwaitingApproval)
        {
            return Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(
                OperateSectionStatus.Unavailable,
                $"Operation '{operationId}' is '{op.RawStatus}', not AwaitingApproval; it cannot be submitted."));
        }

        var submitted = op with
        {
            Lifecycle = DeployOperationLifecycle.Submitted,
            RawStatus = "Submitted",
            Reason = string.IsNullOrWhiteSpace(reason) ? op.Reason : reason,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _operations[op.OperationId] = submitted;
        return Task.FromResult(OperateSectionResult<DeployOperationProposal>.Allowed(submitted));
    }

    public Task<OperateSectionResult<DeployOperationProposal>> PromoteAsync(
        string operationId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (!_operations.TryGetValue(operationId.Trim(), out var op))
        {
            return Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(
                OperateSectionStatus.Missing,
                $"No deploy-control operation '{operationId}' is known to this in-memory client."));
        }

        if (op.Lifecycle is not (DeployOperationLifecycle.Submitted or DeployOperationLifecycle.Reconciling))
        {
            return Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(
                OperateSectionStatus.Unavailable,
                $"Operation '{operationId}' is '{op.RawStatus}' and cannot be promoted."));
        }

        var promoted = op with
        {
            Lifecycle = DeployOperationLifecycle.Succeeded,
            RawStatus = "Succeeded",
            Reason = string.IsNullOrWhiteSpace(reason) ? op.Reason : reason,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _operations[op.OperationId] = promoted;
        return Task.FromResult(OperateSectionResult<DeployOperationProposal>.Allowed(promoted));
    }

    public Task<OperateSectionResult<DeployOperationProposal>> RollbackAsync(
        string operationId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (!_operations.TryGetValue(operationId.Trim(), out var op))
        {
            return Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(
                OperateSectionStatus.Missing,
                $"No deploy-control operation '{operationId}' is known to this in-memory client."));
        }

        if (!op.IsRollbackEligible)
        {
            return Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(
                OperateSectionStatus.Unavailable,
                $"Operation '{operationId}' is '{op.RawStatus}' and is not rollback-eligible."));
        }

        var rolledBack = op with
        {
            Lifecycle = DeployOperationLifecycle.RollbackRequested,
            RawStatus = "RollbackRequested",
            Action = "rollback",
            Reason = string.IsNullOrWhiteSpace(reason) ? op.Reason : reason,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _operations[op.OperationId] = rolledBack;
        return Task.FromResult(OperateSectionResult<DeployOperationProposal>.Allowed(rolledBack));
    }
}
