using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the discovery-metadata authoring operation. Reads and writes a layer's or service's
/// discovery / catalog metadata on honua-server through <see cref="IHonuaAdminOperateClient"/> and maps the
/// result (or rejection). It never fabricates success — every result reflects what the server read back.
/// </summary>
public sealed class HonuaServerConsoleDiscoveryMetadataOperation : IConsoleDiscoveryMetadataOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerConsoleDiscoveryMetadataOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ConsoleDiscoveryMetadata> GetLayerDiscoveryAsync(
        int layerId,
        CancellationToken cancellationToken = default)
    {
        var result = await _client.GetLayerDiscoveryAsync(layerId, cancellationToken).ConfigureAwait(false);
        return MapRead(result, "The Honua server did not return discovery metadata for this layer.");
    }

    public async Task<ConsoleSaveDiscoveryResult> SaveLayerDiscoveryAsync(
        int layerId,
        ConsoleDiscoveryMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var result = await _client
            .UpdateLayerDiscoveryAsync(layerId, ToUpdate(metadata), cancellationToken)
            .ConfigureAwait(false);
        return MapWrite(result);
    }

    public async Task<ConsoleDiscoveryMetadata> GetServiceDiscoveryAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var result = await _client.GetServiceDiscoveryAsync(serviceName, cancellationToken).ConfigureAwait(false);
        return MapRead(result, "The Honua server did not return discovery metadata for this service.");
    }

    public async Task<ConsoleSaveDiscoveryResult> SaveServiceDiscoveryAsync(
        string serviceName,
        ConsoleDiscoveryMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var result = await _client
            .UpdateServiceDiscoveryAsync(serviceName, ToUpdate(metadata), cancellationToken)
            .ConfigureAwait(false);
        return MapWrite(result);
    }

    private static ConsoleDiscoveryMetadata MapRead(
        HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata> result,
        string fallbackDetail)
    {
        if (result.Data is { } data)
        {
            return new ConsoleDiscoveryMetadata
            {
                Bound = true,
                Title = data.Title,
                Description = data.Description,
                Keywords = data.Keywords ?? [],
                Themes = data.Themes ?? [],
                Language = data.Language,
                License = data.License,
                Attribution = data.Attribution,
                Publisher = data.Publisher,
                ContactPoint = data.ContactPoint is { } c
                    ? new ConsoleDiscoveryContactPoint { Name = c.Name, Email = c.Email, Url = c.Url }
                    : null,
                Links = (data.Links ?? [])
                    .Select(l => new ConsoleDiscoveryLink
                    {
                        Href = l.Href,
                        Rel = l.Rel,
                        Type = l.Type,
                        Title = l.Title,
                        Hreflang = l.Hreflang,
                    })
                    .ToArray(),
            };
        }

        return ConsoleDiscoveryMetadata.Unbound(result.Issue?.Detail ?? fallbackDetail);
    }

    private static ConsoleSaveDiscoveryResult MapWrite(HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata> result)
    {
        if (result.Data is not null)
        {
            return new ConsoleSaveDiscoveryResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = "Saved discovery metadata on honua-server.",
            };
        }

        var issue = result.Issue;
        return new ConsoleSaveDiscoveryResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the discovery-metadata update.",
        };
    }

    // The console form authors the FULL metadata set, so the PUT always carries every list (an empty list
    // therefore clears it server-side, matching the form's "remove all rows" intent).
    private static HonuaAdminDiscoveryMetadataUpdate ToUpdate(ConsoleDiscoveryMetadata metadata) => new()
    {
        Title = metadata.Title,
        Description = metadata.Description,
        Keywords = metadata.Keywords?.ToArray() ?? [],
        Themes = metadata.Themes?.ToArray() ?? [],
        Language = metadata.Language,
        License = metadata.License,
        Attribution = metadata.Attribution,
        Publisher = metadata.Publisher,
        ContactPoint = metadata.ContactPoint is { } c
            ? new HonuaAdminDiscoveryContactPoint { Name = c.Name, Email = c.Email, Url = c.Url }
            : null,
        Links = (metadata.Links ?? [])
            .Select(l => new HonuaAdminDiscoveryLink
            {
                Href = l.Href,
                Rel = l.Rel,
                Type = l.Type,
                Title = l.Title,
                Hreflang = l.Hreflang,
            })
            .ToArray(),
    };
}
