using System.Net;
using System.Text.Json;

namespace Honua.Console.IntegrationTests;

public sealed partial class StudioPublicationBffTests
{
    [Fact]
    public async Task LegacyMapDraftCreateAndUpdateCanonicalizeToHonuaMapV1AndMapId()
    {
        await using var host = await Host.Start();
        host.Wire.ResponseBody = """{"data":{"draftId":"12345678-1234-1234-1234-123456789abc"}}""";

        const string createPayload = """{"packageKey":"customer-map","envelope":{"family":"map","schemaVersion":"1.0","format":"honua_map_package.v1","body":{"mapPackageId":"customer-map","format":"honua_map_package.v1","layers":[{"id":"l1"}]}}}""";
        using var created = await host.Client.PostAsync("/api/studio/v1/studio/package-drafts", new StringContent(createPayload));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdDoc = JsonDocument.Parse(host.Wire.Body);
        var createdEnvelope = createdDoc.RootElement.GetProperty("envelope");
        Assert.Equal("honua_map.v1", createdEnvelope.GetProperty("format").GetString());
        var createdBody = createdEnvelope.GetProperty("body");
        Assert.Equal("honua_map.v1", createdBody.GetProperty("format").GetString());
        Assert.Equal("customer-map", createdBody.GetProperty("mapId").GetString());
        Assert.False(createdBody.TryGetProperty("mapPackageId", out _));

        const string updatePayload = """{"packageKey":"customer-map","generation":2,"envelope":{"family":"map","schemaVersion":"1.0","format":"honua_map.v1","body":{"mapPackageId":"customer-map","mapId":"customer-map","format":"honua_map.v1","layers":[{"id":"l1"}]}}}""";
        using var updated = await host.Client.PutAsync("/api/studio/v1/studio/package-drafts/12345678-1234-1234-1234-123456789abc", new StringContent(updatePayload));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var updatedDoc = JsonDocument.Parse(host.Wire.Body);
        var updatedBody = updatedDoc.RootElement.GetProperty("envelope").GetProperty("body");
        Assert.Equal("customer-map", updatedBody.GetProperty("mapId").GetString());
        Assert.False(updatedBody.TryGetProperty("mapPackageId", out _));
    }

    [Fact]
    public async Task MapDraftCanonicalizerLeavesConflictingIdentitiesAndNonMapFamiliesUntouched()
    {
        await using var host = await Host.Start();
        host.Wire.ResponseBody = """{"data":{}}""";

        const string conflictPayload = """{"packageKey":"customer-map","envelope":{"family":"map","format":"honua_map_package.v1","body":{"mapPackageId":"old-key","mapId":"new-key","format":"honua_map_package.v1"}}}""";
        using var conflictResponse = await host.Client.PostAsync("/api/studio/v1/studio/package-drafts", new StringContent(conflictPayload));
        Assert.Equal(HttpStatusCode.OK, conflictResponse.StatusCode);
        Assert.Equal(conflictPayload, host.Wire.Body);

        const string nonMapPayload = """{"packageKey":"report-1","envelope":{"family":"report","format":"honua_map_package.v1","body":{"mapPackageId":"report-1"}}}""";
        using var nonMapResponse = await host.Client.PostAsync("/api/studio/v1/studio/package-drafts", new StringContent(nonMapPayload));
        Assert.Equal(HttpStatusCode.OK, nonMapResponse.StatusCode);
        Assert.Equal(nonMapPayload, host.Wire.Body);
    }
}
