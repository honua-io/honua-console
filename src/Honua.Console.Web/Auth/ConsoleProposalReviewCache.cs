namespace Honua.Console.Web.Auth;

internal sealed record ConsolePrivateProposalReview(
    string ProposalId,
    long ProposalVersion,
    string PlanHash,
    string Capability,
    DateTimeOffset ExpiresAt)
{
    public string EnvironmentReference { get; init; } = string.Empty;
}

internal sealed class ConsoleProposalReviewCache(TimeProvider clock)
{
    internal const int Capacity = 8;
    private readonly List<ConsolePrivateProposalReview> _reviews = [];
    private readonly object _gate = new();

    public void Remember(ConsolePrivateProposalReview review)
    {
        lock (_gate)
        {
            Prune();
            if (review.ExpiresAt <= clock.GetUtcNow()) return;
            _reviews.RemoveAll(entry => entry.ProposalId == review.ProposalId);
            if (_reviews.Count == Capacity) _reviews.RemoveAt(0);
            _reviews.Add(review);
        }
    }

    public ConsolePrivateProposalReview? Take(string proposalId, long version, string planHash)
    {
        lock (_gate)
        {
            Prune();
            var index = _reviews.FindIndex(entry => entry.ProposalId == proposalId);
            if (index < 0) return null;
            var review = _reviews[index];
            _reviews.RemoveAt(index);
            return review.ProposalVersion == version
                && string.Equals(review.PlanHash, planHash, StringComparison.Ordinal) ? review : null;
        }
    }

    public void Clear()
    {
        lock (_gate) _reviews.Clear();
    }

    private void Prune() => _reviews.RemoveAll(entry => entry.ExpiresAt <= clock.GetUtcNow());
}
