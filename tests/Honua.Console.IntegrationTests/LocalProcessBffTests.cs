using Honua.Console.Web;

namespace Honua.Console.IntegrationTests;

public sealed class LocalProcessBffTests
{
    [Fact]
    public async Task ArtifactTransferPreservesExactVerifiedLength()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        await using var upstream = new MemoryStream(bytes);
        await using var output = new MemoryStream();
        await LocalProcessBff.TransferArtifactAsync(upstream, output, bytes.Length,
            _ => Task.FromResult(true), CancellationToken.None);
        Assert.Equal(bytes, output.ToArray());

        upstream.Position = 0;
        output.SetLength(0);
        await Assert.ThrowsAsync<InvalidDataException>(() => LocalProcessBff.TransferArtifactAsync(
            upstream, output, bytes.Length - 1, _ => Task.FromResult(true), CancellationToken.None));
        Assert.Empty(output.ToArray());

        upstream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => LocalProcessBff.TransferArtifactAsync(
            upstream, output, bytes.Length + 1, _ => Task.FromResult(true), CancellationToken.None));
    }

    [Fact]
    public async Task ArtifactTransferStopsWhenReaderChangesBetweenChunks()
    {
        await using var upstream = new MemoryStream(new byte[1024 * 1024 + 1]);
        await using var output = new MemoryStream();
        var checks = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => LocalProcessBff.TransferArtifactAsync(
            upstream, output, upstream.Length, _ => Task.FromResult(++checks < 3), CancellationToken.None));
        Assert.Equal(1024 * 1024, output.Length);
    }

    [Theory]
    [InlineData("runs", "GET", true)]
    [InlineData("runs", "POST", true)]
    [InlineData("runs", "PUT", false)]
    [InlineData("project/identity", "GET", true)]
    [InlineData("project/bind", "POST", true)]
    [InlineData("project/bind", "GET", false)]
    [InlineData("project/bindings/workspace", "DELETE", true)]
    [InlineData("project/bindings/workspace", "POST", false)]
    [InlineData("sources/model.py", "GET", true)]
    [InlineData("sources/model.py", "PUT", true)]
    [InlineData("sources/model.py", "POST", false)]
    [InlineData("snapshots", "POST", true)]
    [InlineData("snapshots/snapshot/sources/model.py", "GET", true)]
    [InlineData("snapshots/snapshot/sources/model.py", "PUT", false)]
    [InlineData("commands/command", "GET", true)]
    [InlineData("commands/command", "POST", false)]
    [InlineData("sources/../secrets.py", "GET", false)]
    [InlineData("sources/%2fsecret.py", "GET", false)]
    [InlineData("sources/.env", "GET", false)]
    [InlineData("arbitrary/path", "GET", false)]
    public void CanonicalProcessProxyRestrictsRoutesAndMutations(string path, string method, bool accepted) =>
        Assert.Equal(accepted, LocalProcessBff.AllowedPath().IsMatch(path) && LocalProcessBff.AllowsMethod(path, method));
}
