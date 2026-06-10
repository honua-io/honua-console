using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the layer permanent-filter operation. Reads and authors a layer's server-enforced
/// query filter on honua-server through <see cref="IHonuaAdminOperateClient"/> and maps the result (or
/// rejection). It never fabricates success — every result reflects what the server read back, and a 400
/// validation rejection surfaces the server's reason verbatim.
/// </summary>
public sealed class HonuaServerConsoleLayerFilterOperation : IConsoleLayerFilterOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerConsoleLayerFilterOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ConsoleLayerFilter> GetFilterAsync(int layerId, CancellationToken cancellationToken = default)
    {
        var result = await _client.GetLayerFilterAsync(layerId, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } data)
        {
            var filter = data.PermanentFilter;
            return new ConsoleLayerFilter
            {
                Bound = true,
                LayerId = data.LayerId,
                HasFilter = filter is not null,
                Expression = filter?.Expression ?? string.Empty,
                Language = string.IsNullOrWhiteSpace(filter?.Language) ? "arcgis-sql" : filter!.Language!,
            };
        }

        return ConsoleLayerFilter.Unbound(
            result.Issue?.Detail ?? "The Honua server did not return a permanent filter for this layer.");
    }

    public async Task<ConsoleSetLayerFilterResult> SaveFilterAsync(
        int layerId,
        string expression,
        string language,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return new ConsoleSetLayerFilterResult
            {
                Succeeded = false,
                State = "Invalid",
                Detail = "Enter a filter expression, or use Clear filter to remove the saved filter.",
            };
        }

        var request = new HonuaAdminLayerFilterUpdate
        {
            PermanentFilter = new HonuaAdminPermanentFilter
            {
                Expression = expression,
                Language = string.IsNullOrWhiteSpace(language) ? "arcgis-sql" : language,
            },
        };

        var result = await _client.UpdateLayerFilterAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        if (result.Data is not null)
        {
            return new ConsoleSetLayerFilterResult
            {
                Succeeded = true,
                State = "Saved",
                Detail = "Saved the permanent filter; honua-server now enforces it on every read of this layer.",
            };
        }

        var issue = result.Issue;
        return new ConsoleSetLayerFilterResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the permanent filter.",
        };
    }

    public async Task<ConsoleSetLayerFilterResult> ClearFilterAsync(
        int layerId,
        CancellationToken cancellationToken = default)
    {
        // Sending PermanentFilter = null serializes { "permanentFilter": null }, which clears the saved filter.
        var request = new HonuaAdminLayerFilterUpdate { PermanentFilter = null };

        var result = await _client.UpdateLayerFilterAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        if (result.Data is not null)
        {
            return new ConsoleSetLayerFilterResult
            {
                Succeeded = true,
                State = "Cleared",
                Detail = "Cleared the permanent filter; honua-server no longer constrains reads of this layer.",
            };
        }

        var issue = result.Issue;
        return new ConsoleSetLayerFilterResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the filter change.",
        };
    }
}
