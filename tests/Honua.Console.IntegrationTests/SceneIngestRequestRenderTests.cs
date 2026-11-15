using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class SceneIngestRequestRenderTests
{
    [Fact]
    public async Task LegacyUploadKeepsSubmittedFileAndMetadataWhileCopying()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var client = new RecordingSceneClient();
        ctx.Services.AddSingleton<IConsoleSceneClient>(client);
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = false });
        var page = ctx.Render<OperateScenesPage>();
        page.Find("[data-pointcloud-display-name]").Change("Original survey");
        page.Find("[data-pointcloud-description]").Change("Original description");
        page.Find("[data-pointcloud-source-crs]").Change("26910");
        var file = new DeferredFile();
        var input = page.FindComponent<InputFile>();
        await input.InvokeAsync(() => input.Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([file])));

        var pending = page.Find("[data-pointcloud-ingest-button]").ClickAsync(new MouseEventArgs());
        await file.CopyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.Find("[data-pointcloud-display-name]").Change("Edited survey");
        page.Find("[data-pointcloud-description]").Change("Edited description");
        page.Find("[data-pointcloud-source-crs]").Change("4326");
        input.UploadFiles(InputFileContent.CreateFromText("replacement bytes", "replacement.laz"));
        file.ContinueCopy.SetResult();
        await pending;

        var request = Assert.IsType<PointCloudIngestRequest>(client.Request);
        Assert.Equal("original.las", request.FileName);
        Assert.Equal("Original survey", request.DisplayName);
        Assert.Equal("Original description", request.Description);
        Assert.Equal(26910, request.SourceCrs);
        Assert.Equal("original bytes"u8.ToArray(), request.Content);
        Assert.Equal("Edited survey", page.Find("[data-pointcloud-display-name]").GetAttribute("value"));
    }

    private sealed class DeferredFile : IBrowserFile
    {
        public TaskCompletionSource CopyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueCopy { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Name => "original.las";
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => 14;
        public string ContentType => "application/octet-stream";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
            => new DeferredStream(this);

        private sealed class DeferredStream(DeferredFile file) : MemoryStream("original bytes"u8.ToArray())
        {
            public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            {
                file.CopyStarted.SetResult();
                await file.ContinueCopy.Task.WaitAsync(cancellationToken);
                await base.CopyToAsync(destination, bufferSize, cancellationToken);
            }
        }
    }

    private sealed class RecordingSceneClient : IConsoleSceneClient
    {
        private readonly InMemoryConsoleSceneClient _inner = new();
        public PointCloudIngestRequest? Request { get; private set; }
        public Task<SceneReadResult<SceneListResponse>> ListScenesAsync(CancellationToken cancellationToken = default)
            => _inner.ListScenesAsync(cancellationToken);
        public Task<string?> ResolveTilesetUrlAsync(string sceneId, CancellationToken cancellationToken = default)
            => _inner.ResolveTilesetUrlAsync(sceneId, cancellationToken);
        public Task<SceneReadResult<PointCloudIngestResult>> IngestPointCloudAsync(
            PointCloudIngestRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return _inner.IngestPointCloudAsync(request, cancellationToken);
        }
    }
}
