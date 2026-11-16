namespace Honua.Console.Shell.Models;

// An ephemeral review, deliberately not a record (its capability must not appear in ToString).
public sealed class ConsoleDeployApprovalReview
{
    internal ConsoleDeployApprovalReview(DeployOperationProposal operation, DateTimeOffset expiresAt,
        ConsoleDeployReviewAuthority authority)
    {
        Operation = operation;
        ExpiresAt = expiresAt;
        Capability = authority.Capability;
        ProfileId = authority.ProfileId;
        ServerBaseUri = authority.ServerBaseUri;
        AuthorityBinding = authority.Binding;
    }

    public DeployOperationProposal Operation { get; }
    public DateTimeOffset ExpiresAt { get; }
    private int _consumed;
    internal bool TryConsume() => Interlocked.Exchange(ref _consumed, 1) == 0;
    internal string Capability { get; }
    internal string ProfileId { get; }
    internal Uri? ServerBaseUri { get; }
    internal string? AuthorityBinding { get; }
}

internal sealed class ConsoleDeployReviewAuthority
{
    public string Capability { get; init; } = "";
    public string ProfileId { get; init; } = "";
    public Uri? ServerBaseUri { get; init; }
    public string? Binding { get; init; }
}
