using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[Collection(ServiceLayerPublishIntegrationCollection.Name)]
public sealed class ApiKeyManagementLiveServerTests(ServiceLayerPublishFixture fixture)
{
    [SkippableFact]
    public async Task CreateListAndRevokeRoundTripAgainstTheLiveServer()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason ?? "");
        using var http = fixture.CreateHttpClient();
        using var client = new HonuaAdminOperateHttpClient(http,
            new(fixture.BaseAddress, fixture.Options.StudioAdminApiKey));
        const string binding = "fixture-admin";
        var created = await client.CreateApiKeyAsync(new(binding,
            new($"console-712-{Guid.NewGuid():N}", ["admin:read"])));
        Assert.Null(created.Issue);
        Assert.NotNull(created.Data);
        try
        {
            Assert.False(string.IsNullOrWhiteSpace(created.Data.Key));
            var listed = await client.ListApiKeysBoundAsync(binding);
            Assert.Null(listed.Issue);
            Assert.Contains(listed.Data!, key => key.Id == created.Data.ApiKey.Id);
            using var anonymousHttp = fixture.CreateHttpClient();
            using var anonymous = new HonuaAdminOperateHttpClient(anonymousHttp, new(fixture.BaseAddress));
            var denied = await anonymous.RevokeApiKeyAsync(new(binding, created.Data.ApiKey.Id));
            Assert.NotNull(denied.Issue);
            Assert.Contains(denied.Issue.StatusCode, new int?[] { 401, 403 });
        }
        finally
        {
            var revoked = await client.RevokeApiKeyAsync(new(binding, created.Data.ApiKey.Id));
            Assert.Null(revoked.Issue);
            Assert.NotNull(revoked.Data!.RevokedAt);
        }
    }
}
