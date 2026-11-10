using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class ServingProtocolRouteTests
{
    [Theory]
    [InlineData("FeatureServer", "/rest/services/Public%20Folder/Parcels/FeatureServer")]
    [InlineData("MapServer", "/rest/services/Public%20Folder/Parcels/MapServer")]
    [InlineData("ImageServer", "/rest/services/Public%20Folder/Parcels/ImageServer")]
    [InlineData("GPServer", "/rest/services/Public%20Folder/Parcels/GPServer")]
    [InlineData("Wms", "/rest/services/Public%20Folder/Parcels/MapServer/WMS")]
    [InlineData("Wmts", "/rest/services/Public%20Folder/Parcels/MapServer/WMTS")]
    [InlineData("Wcs", "/rest/services/Public%20Folder/Parcels/ImageServer/WCS")]
    [InlineData("Wfs20", "/wfs")]
    [InlineData("OgcFeatures", "/ogc/features")]
    [InlineData("OgcApiTiles", "/ogc/tiles")]
    [InlineData("OgcApiMaps", "/ogc/maps")]
    [InlineData("OgcApiCoverages", "/ogc/coverages")]
    [InlineData("OData", "/odata")]
    [InlineData("Stac", "/stac")]
    public void ProtocolUrl_FolderPublication_UsesTheRegisteredPublicEndpoint(string protocol, string path)
    {
        var projection = new OperatePublicationProjection(new Uri("https://server.example/arcgis/"));

        Assert.Equal(path, HonuaServedRoutes.PathFor("Parcels", protocol, "Public Folder"));
        Assert.Equal("https://server.example/arcgis" + path, projection.ProtocolUrl("Parcels", protocol, "Public Folder"));
    }
}
