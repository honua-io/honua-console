using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface ICatalogAdminSource
{
    Task<CatalogAdminRead> ReadAsync(string? catalogId, CancellationToken cancellationToken);
}

internal sealed class UnsupportedCatalogAdminSource(bool serverConfigured) : ICatalogAdminSource
{
    public Task<CatalogAdminRead> ReadAsync(string? catalogId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CatalogAdminRead([], null,
            serverConfigured ? "unsupported" : "missing",
            serverConfigured
                ? "This server does not support catalog administration yet."
                : "Configure a server to manage catalogs."));
    }
}
