using System.IO.Pipelines;
using Honua.Console.Web;

namespace Honua.Console.IntegrationTests;

public sealed class LocalProcessBffTests
{
    [Fact]
    public async Task ArtifactTransferStreamsBeyondTheInlineMcpBudget()
    {
        var bytes = new byte[5 * 1024 * 1024 + 17];
        new Random(12345).NextBytes(bytes);
        await using var upstream = new MemoryStream(bytes);
        await using var output = new MemoryStream();
        await LocalProcessBff.TransferArtifactAsync(upstream, output, bytes.Length,
            _ => Task.FromResult(true), CancellationToken.None);
        Assert.Equal(bytes, output.ToArray());
    }

    [Fact]
    public async Task ArtifactTransferCancelsAStalledResponseBody()
    {
        var pipe = new Pipe();
        await using var upstream = pipe.Reader.AsStream();
        await using var output = new MemoryStream();
        using var deadline = new CancellationTokenSource();
        var transfer = LocalProcessBff.TransferArtifactAsync(upstream, output, 1,
            _ => Task.FromResult(true), deadline.Token);
        Assert.False(transfer.IsCompleted);
        deadline.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transfer.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(output.ToArray());
        await pipe.Writer.CompleteAsync();
    }

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
