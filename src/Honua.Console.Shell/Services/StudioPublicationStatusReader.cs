using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Sdk.Studio.Packages;

namespace Honua.Console.Shell.Services;

/// <summary>Reads proposal status and authoritative pointers without approving or replaying a mutation.</summary>
public interface IStudioPublicationStatusReader
{
    Task<StudioPublicationRefresh> RefreshAsync(StudioPendingPublication pending, CancellationToken cancellationToken = default);
}

public sealed class StudioPublicationStatusReader(
    IStudioPackageLifecycleClient? lifecycle,
    IConsoleProposalsClient proposals) : IStudioPublicationStatusReader
{
    public async Task<StudioPublicationRefresh> RefreshAsync(StudioPendingPublication pending, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pending);
        if (lifecycle is null)
        {
            return new(pending, null, null, "The Studio lifecycle binding is unavailable.");
        }

        var proposal = await proposals.GetAsync(pending.Operation.ProposalId!, cancellationToken).ConfigureAwait(false);
        if (!proposal.IsAllowed || proposal.Value is not { } detail)
        {
            return new(pending, null, null, proposal.Message);
        }

        if (!string.Equals(detail.ProposalId, pending.Operation.ProposalId, StringComparison.Ordinal))
        {
            return new(pending, null, null, "The proposal response did not identify the requested proposal.");
        }

        var pointers = await lifecycle.GetContentItemPointersAsync(pending.ItemId, cancellationToken).ConfigureAwait(false);
        if (!pointers.IsSuccess || pointers.Data is not { } observed || observed.ItemId != pending.ItemId)
        {
            return new(pending, null, null, pointers.Issue?.Detail ?? "The content item is not visible; publication state is unknown.");
        }

        StudioContentVersion? published = null;
        if (observed.PublishedVersionId is { } publishedId)
        {
            var version = await lifecycle.GetContentVersionAsync(pending.ItemId, publishedId, cancellationToken).ConfigureAwait(false);
            if (!version.IsSuccess || version.Data is not { } value || value.ItemId != pending.ItemId || value.VersionId != publishedId)
            {
                return new(pending, null, null, version.Issue?.Detail ?? "The published version could not be verified.");
            }

            published = value;
        }

        return new(pending with { ProposalStatus = detail.Status }, observed, published, null);
    }
}

/// <summary>Observed publication state; a successful proposal alone never determines the published pointer.</summary>
public sealed record StudioPublicationRefresh(
    StudioPendingPublication Submission,
    StudioContentItemPointers? Pointers,
    StudioContentVersion? PublishedVersion,
    string? Issue)
{
    public bool Succeeded => Issue is null && Pointers is not null;

    public string Message => Issue ?? $"Proposal {Submission.Operation.ProposalId}: {Submission.ProposalStatus}. "
        + (PublishedVersion is { } version ? $"Published version: v{version.VersionNumber}." : "No version is published.");

    public void Apply(StudioMapEditorState state)
    {
        if (!Succeeded || state.ItemId != Pointers!.ItemId) return;
        state.Status = state.VersionId == Pointers.PublishedVersionId && state.VersionId is not null
            ? StudioMapStatuses.Published : StudioMapStatuses.Draft;
        if (state.PendingPublication?.Operation.OperationInstanceId == Submission.Operation.OperationInstanceId)
        {
            state.PendingPublication = Submission.IsPending ? Submission : null;
        }
        state.PreviousPublication = Submission;
    }

    public void Apply(StudioDashboardEditorState state)
    {
        if (!Succeeded || state.ItemId != Pointers!.ItemId) return;
        state.PublishedVersion = PublishedVersion?.VersionNumber;
        state.Status = state.CurrentVersionId == Pointers.PublishedVersionId && state.CurrentVersionId is not null
            ? StudioDashboardStatuses.Published : StudioDashboardStatuses.Draft;
        if (state.PendingPublication?.Operation.OperationInstanceId == Submission.Operation.OperationInstanceId)
        {
            state.PendingPublication = Submission.IsPending ? Submission : null;
        }
        state.PreviousPublication = Submission;
    }

    public void Apply(StudioAppEditorState state)
    {
        if (!Succeeded || state.ItemId != Pointers!.ItemId) return;
        state.PublishedVersion = PublishedVersion?.VersionNumber;
        state.PublishedVersionId = Pointers.PublishedVersionId;
        if (state.PendingPublication?.Operation.OperationInstanceId == Submission.Operation.OperationInstanceId)
        {
            state.PendingPublication = Submission.IsPending ? Submission : null;
        }
        state.PreviousPublication = Submission;
    }

    public StudioAuthoringSession Apply(StudioAuthoringSession session)
    {
        if (!Succeeded || session.Draft?.ItemId != Pointers!.ItemId.ToString()) return session;
        var hasSavedVersion = !string.IsNullOrWhiteSpace(session.Draft.CurrentVersionId);
        var currentPublished = hasSavedVersion && Pointers.PublishedVersionId is { } publishedId
            && session.Draft.CurrentVersionId == publishedId.ToString();
        return session with
        {
            ActivePackage = session.ActivePackage with
            {
                LifecycleState = currentPublished ? StudioPackageLifecycleState.Published
                    : hasSavedVersion ? StudioPackageLifecycleState.SavedVersion : StudioPackageLifecycleState.Draft
            },
            PendingPublication = session.PendingPublication?.Operation.OperationInstanceId == Submission.Operation.OperationInstanceId
                ? (Submission.IsPending ? Submission : null) : session.PendingPublication,
            PreviousPublication = Submission,
            StatusMessage = Message
        };
    }
}
