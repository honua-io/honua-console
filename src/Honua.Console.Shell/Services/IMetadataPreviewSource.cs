using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IMetadataPreviewSource
{
    Task<MetadataPreviewRead> ReadAsync(MetadataPreviewRequest request, CancellationToken cancellationToken);
}

internal sealed class UnsupportedMetadataPreviewSource(bool serverConfigured) : IMetadataPreviewSource
{
    public Task<MetadataPreviewRead> ReadAsync(MetadataPreviewRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new MetadataPreviewRead(serverConfigured ? "unsupported" : "missing")
        {
            Message = serverConfigured ? "The shared metadata preview contract is not available." : "Configure a server to read metadata."
        });
    }
}
