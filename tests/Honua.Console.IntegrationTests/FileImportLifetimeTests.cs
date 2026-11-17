using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class FileImportLifetimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PreviewStartsOnlyWhileThePageOwnsTheFileRead(bool enabled, bool navigateAway)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        var operation = new ImportOperation();
        context.Services.AddSingleton<IConsoleFileImportOperation>(operation);
        var page = context.Render<OperateImportFilePage>();
        using var file = new HeldFile();
        var input = page.FindComponent<InputFile>().Instance;
        var pending = page.InvokeAsync(() => input.OnChange.InvokeAsync(new InputFileChangeEventArgs([file])));
        await file.Stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, operation.PreviewCalls);

        if (navigateAway) await context.DisposeComponentsAsync();
        file.Stream.Release.SetResult();
        await pending;

        Assert.Equal(navigateAway ? 0 : 1, operation.PreviewCalls);
        if (!navigateAway) Assert.Equal(new byte[] { 1, 2, 3 }, operation.PreviewBytes);
    }

    private sealed class HeldFile : IBrowserFile, IDisposable
    {
        public HeldStream Stream { get; } = new();
        public string Name => "projected.gpkg";
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => 3;
        public string ContentType => "application/geopackage+sqlite3";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => Stream;
        public void Dispose() => Stream.Dispose();
    }

    private sealed class HeldStream : MemoryStream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HeldStream() : base([1, 2, 3]) { }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task;
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class ImportOperation : IConsoleFileImportOperation
    {
        public int PreviewCalls { get; private set; }
        public byte[]? PreviewBytes { get; private set; }
        public Task<IReadOnlyList<string>> GetSupportedExtensionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([".gpkg"]);

        public Task<ConsoleFileImportPreview> PreviewAsync(byte[] bytes, string fileName, CancellationToken cancellationToken = default)
        {
            PreviewCalls++;
            PreviewBytes = bytes;
            return Task.FromResult(new ConsoleFileImportPreview { Succeeded = true, State = "Unchecked" });
        }

        public Task<OperateSectionResult<HonuaAdminCoordinateSystemSearch>> SearchCoordinateSystemsAsync(
            string query, string authorityBinding, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConsoleFileImportResult> ImportAsync(ConsoleFileImportCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
