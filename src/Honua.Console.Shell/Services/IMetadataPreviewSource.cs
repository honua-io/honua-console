using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IMetadataPreviewSource
{
    Task<MetadataPreviewRead> ReadAsync(MetadataPreviewRequest request, CancellationToken cancellationToken);
}

internal sealed class UnsupportedMetadataPreviewSource(Uri? serverBaseUri) : IMetadataPreviewSource
{
    public Task<MetadataPreviewRead> ReadAsync(MetadataPreviewRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new MetadataPreviewRead(serverBaseUri is not null ? "unsupported" : "missing")
        {
            Message = serverBaseUri is not null ? "The shared metadata preview contract is not available." : "Configure a server to read metadata."
        });
    }
}
