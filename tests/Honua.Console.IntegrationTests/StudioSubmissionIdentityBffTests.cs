using System.Net;

namespace Honua.Console.IntegrationTests;

public sealed partial class StudioPublicationBffTests
{
    [Theory]
    [InlineData(Publication)]
    [InlineData(Rollback)]
    public async Task RetainsOriginalSubmissionIdentityAndPendingApprovalReceipt(string path)
    {
        await using var host = await Host.Start();
        host.Wire.Status = HttpStatusCode.Accepted;
        host.Wire.ResponseBody = """{"data":{"status":4,"operationInstanceId":"operation","proposalId":"proposal"}}""";
        host.Client.DefaultRequestHeaders.Add("Idempotency-Key", "original-command");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await host.Client.PostAsync(path, new StringContent("{}"));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(host.Wire.ResponseBody, await response.Content.ReadAsStringAsync());
            Assert.Equal("original-command", host.Wire.IdempotencyKey);
            Assert.True(host.Wire.HumanOnly);
        }
        Assert.Equal(2, host.Wire.Calls);
    }

    [Theory]
    [InlineData(Publication)]
    [InlineData(Rollback)]
    [InlineData("/api/studio/v1/studio/package-drafts")]
    public async Task DuplicateOrMalformedIdempotencyKeysNeverReachServer(string path)
    {
        await using var host = await Host.Start();
        foreach (var value in new[] { "bad/key", "two,keys", new string('a', 129) })
        {
            host.Client.DefaultRequestHeaders.Remove("Idempotency-Key");
            host.Client.DefaultRequestHeaders.Add("Idempotency-Key", value);
            using var response = await host.Client.PostAsync(path, new StringContent("{}"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        host.Client.DefaultRequestHeaders.Remove("Idempotency-Key");
        host.Client.DefaultRequestHeaders.Add("Idempotency-Key", new[] { "first", "second" });
        using var duplicate = await host.Client.PostAsync(path, new StringContent("{}"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(0, host.Wire.Calls);
    }
}
