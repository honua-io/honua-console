using Honua.Console.Web.Auth;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleProposalReviewCacheTests
{
    [Fact]
    public void Take_ExactDisplayedSnapshot_ConsumesCapabilityOnce()
    {
        var clock = new ReviewClock();
        var cache = new ConsoleProposalReviewCache(clock);
        cache.Remember(Review(clock));

        Assert.Equal("private-capability", cache.Take("proposal", 7, "plan")?.Capability);
        Assert.Null(cache.Take("proposal", 7, "plan"));
    }

    [Theory]
    [InlineData(8, "plan")]
    [InlineData(7, "changed-plan")]
    public void Take_StaleDisplayedSnapshot_RefusesAndDiscardsCapability(long version, string planHash)
    {
        var clock = new ReviewClock();
        var cache = new ConsoleProposalReviewCache(clock);
        cache.Remember(Review(clock));

        Assert.Null(cache.Take("proposal", version, planHash));
        Assert.Null(cache.Take("proposal", 7, "plan"));
    }

    [Fact]
    public void Take_ExpiredReview_DoesNotRefreshExpiry()
    {
        var clock = new ReviewClock();
        var cache = new ConsoleProposalReviewCache(clock);
        cache.Remember(Review(clock));
        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Null(cache.Take("proposal", 7, "plan"));
    }

    [Fact]
    public void Remember_AboveBound_EvictsOldestReview()
    {
        var clock = new ReviewClock();
        var cache = new ConsoleProposalReviewCache(clock);
        for (var index = 0; index <= ConsoleProposalReviewCache.Capacity; index++)
            cache.Remember(Review(clock) with { ProposalId = "proposal-" + index });

        Assert.Null(cache.Take("proposal-0", 7, "plan"));
        Assert.NotNull(cache.Take("proposal-" + ConsoleProposalReviewCache.Capacity, 7, "plan"));
    }

    [Fact]
    public void Clear_SessionEnds_DiscardsAllCapabilities()
    {
        var clock = new ReviewClock();
        var cache = new ConsoleProposalReviewCache(clock);
        cache.Remember(Review(clock));

        cache.Clear();

        Assert.Null(cache.Take("proposal", 7, "plan"));
    }

    private static ConsolePrivateProposalReview Review(ReviewClock clock) => new(
        "proposal", 7, "plan", "private-capability", clock.GetUtcNow().AddMinutes(5));

    private sealed class ReviewClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
