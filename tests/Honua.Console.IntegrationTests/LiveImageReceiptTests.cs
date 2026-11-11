namespace Honua.Console.IntegrationTests;

public sealed class LiveImageReceiptTests
{
    [Theory]
    [InlineData("Unavailable", null)]
    [InlineData("unavailable", "connection reset")]
    [InlineData("Rejected", "upstream HTTP 503")]
    public void LayerPublishServerErrorFailsTheReceipt(string? state, string? detail)
    {
        Assert.True(LiveImageReceipt.LayerPublishUnavailable(state, detail));
    }

    [Theory]
    [InlineData("Rejected", "primary key 500 is missing")]
    [InlineData("Unsupported", null)]
    [InlineData("Missing permission", "HTTP 403")]
    public void LayerPublishClientResultsAreNotImageFailures(string? state, string? detail)
    {
        Assert.False(LiveImageReceipt.LayerPublishUnavailable(state, detail));
    }

    [Theory]
    [InlineData(500, "Unavailable")]
    [InlineData(200, "Unsupported")]
    [InlineData(null, "Unavailable")]
    public void ContentPublishServerErrorFailsTheReceipt(int? statusCode, string? state)
    {
        Assert.True(LiveImageReceipt.ContentIssueIsImageFailure(statusCode, state));
    }

    [Theory]
    [InlineData(400, "Rejected")]
    [InlineData(403, "Missing permission")]
    [InlineData(409, "Conflict")]
    public void ContentPublishDecisionsAreNotImageFailures(int? statusCode, string? state)
    {
        Assert.False(LiveImageReceipt.ContentIssueIsImageFailure(statusCode, state));
    }
}
