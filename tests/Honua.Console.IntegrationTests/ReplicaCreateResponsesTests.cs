namespace Honua.Console.IntegrationTests;

public sealed class ReplicaCreateResponsesTests
{
    [Theory]
    [InlineData(402)]
    [InlineData(404)]
    [InlineData(405)]
    [InlineData(501)]
    public void MissingVerbOrEntitlementSkips(int statusCode)
    {
        var decision = ReplicaCreateResponses.Classify(statusCode, string.Empty);

        Assert.True(decision.Skip);
        Assert.Null(decision.Failure);
    }

    [Fact]
    public void ClientRejectionWithoutSyncStillSkips()
    {
        var decision = ReplicaCreateResponses.Classify(400, """{"error":{"message":"Offline sync is not enabled"}}""");

        Assert.True(decision.Skip);
        Assert.Null(decision.Failure);
    }

    [Fact]
    public void ServerErrorIsNotASkip()
    {
        const string secret = "secret-token-should-not-leak";
        var decision = ReplicaCreateResponses.Classify(500, "{\"error\":{\"message\":\"" + secret + "\"}}");

        Assert.False(decision.Skip);
        Assert.Null(decision.ReplicaId);
        Assert.Equal("createReplica returned 500.", decision.Failure);
        Assert.DoesNotContain(secret, decision.Failure);
    }

    [Fact]
    public void UnauthorizedIsNotASkip()
    {
        const string secret = "secret-token-should-not-leak";
        var decision = ReplicaCreateResponses.Classify(401, secret);

        Assert.False(decision.Skip);
        Assert.Equal("createReplica returned 401.", decision.Failure);
        Assert.DoesNotContain(secret, decision.Failure);
    }

    [Fact]
    public void SuccessWithoutReplicaIdIsNotASkip()
    {
        const string secret = "secret-token-should-not-leak";
        var decision = ReplicaCreateResponses.Classify(200, "{\"note\":\"" + secret + "\"}");

        Assert.False(decision.Skip);
        Assert.Null(decision.ReplicaId);
        Assert.Equal("createReplica returned 200 without replicaID.", decision.Failure);
        Assert.DoesNotContain(secret, decision.Failure);
    }

    [Fact]
    public void SuccessReturnsTheReplicaId()
    {
        var decision = ReplicaCreateResponses.Classify(200, """{"replicaID":"rep-1"}""");

        Assert.False(decision.Skip);
        Assert.Equal("rep-1", decision.ReplicaId);
        Assert.Null(decision.Failure);
    }
}
