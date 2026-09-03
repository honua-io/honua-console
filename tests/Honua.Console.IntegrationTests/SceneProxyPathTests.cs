using Honua.Console.Web;

namespace Honua.Console.IntegrationTests;

public sealed class SceneProxyPathTests
{
    [Fact]
    public void BuildSceneAssetUri_UsesSelectedEnvironmentBaseUri()
    {
        var uri = MapProxySupport.BuildSceneAssetUri(
            new Uri("https://active.example/honua"),
            "scene-1",
            "tiles/0/mesh.b3dm");

        Assert.Equal(
            "https://active.example/honua/scenes/scene-1/tiles/0/mesh.b3dm",
            uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("tileset.json", "tileset.json")]
    [InlineData("tiles/0/mesh.b3dm", "tiles/0/mesh.b3dm")]
    [InlineData("tiles/a b.glb", "tiles/a%20b.glb")]
    public void NormalizeSceneAssetPath_ProducesEscapedRelativePath(string input, string expected) =>
        Assert.Equal(expected, MapProxySupport.NormalizeSceneAssetPath(input));

    [Theory]
    [InlineData("")]
    [InlineData("../secret")]
    [InlineData("tiles/../secret")]
    [InlineData("tiles\\secret")]
    [InlineData("tiles/%2e%2e/secret")]
    [InlineData("tiles/%2fsecret")]
    public void NormalizeSceneAssetPath_RejectsTraversalOrSeparators(string input) =>
        Assert.Null(MapProxySupport.NormalizeSceneAssetPath(input));

    [Theory]
    [InlineData("scene-1", "scene-1")]
    [InlineData("review scene", "review%20scene")]
    public void NormalizeSceneAssetPath_ProducesSafeSceneId(string input, string expected) =>
        Assert.Equal(expected, MapProxySupport.NormalizeSceneAssetPath(input));

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("%2e%2e")]
    [InlineData("scene/child")]
    public void NormalizeSceneAssetPath_RefusesUnsafeSceneId(string input) =>
        Assert.True(MapProxySupport.NormalizeSceneAssetPath(input) is not { } safe || safe.Contains('/'));
}
