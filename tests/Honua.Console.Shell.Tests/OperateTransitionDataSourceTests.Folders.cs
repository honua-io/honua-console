using System.Text.Json;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Tests;

public sealed partial class OperateTransitionDataSourceTests
{
    [Theory]
    [InlineData("Survey", "https://server.example/rest/services/Survey/Public%20parcels/FeatureServer")]
    [InlineData(null, null)]
    public async Task ServicesUsePublicFolderPathsAndPreserveReconciliationReasons(string? folderName, string? expectedUrl)
    {
        var handler = new JsonFixtureHandler(new Dictionary<string, string>
        {
            ["/api/v1/admin/connections/"] = "{\"success\":true,\"data\":[]}",
            ["/api/v1/admin/services/?page=1&pageSize=100"] = JsonSerializer.Serialize(new
            {
                success = true,
                data = new
                {
                    items = new[] { new { serviceName = "logical-id", folderId = "folder", folderName,
                    publicName = "Public parcels", folderMutationIssues = new[] { "Publication path needs reconciliation." },
                    enabledProtocols = new[] { "FeatureServer" } } },
                    page = 1,
                    pageSize = 100,
                    total = 1,
                    layerTotal = 0
                }
            })
        });
        var source = CreateServerDataSource(handler);
        var service = Assert.Single((await source.GetServicesViewAsync()).Services);
        Assert.Equal("logical-id", service.Name);
        Assert.Equal(expectedUrl, Assert.Single(service.Endpoints).Url);
        Assert.Equal("Publication path needs reconciliation.", Assert.Single(service.FolderMutationIssues));
    }
}
