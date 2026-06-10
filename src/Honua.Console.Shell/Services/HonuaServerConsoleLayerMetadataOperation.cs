using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the layer-metadata authoring operation. Reads and writes a layer's display hints,
/// editor-tracking / edit-capability metadata, and spatial/CRS metadata on honua-server through
/// <see cref="IHonuaAdminOperateClient"/> and maps the result (or rejection). It never fabricates success —
/// every result reflects what the server read back.
/// </summary>
public sealed class HonuaServerConsoleLayerMetadataOperation : IConsoleLayerMetadataOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerConsoleLayerMetadataOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ConsoleLayerDisplay> GetDisplayAsync(int layerId, CancellationToken cancellationToken = default)
    {
        var result = await _client.GetLayerDisplayAsync(layerId, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } data)
        {
            return new ConsoleLayerDisplay
            {
                Bound = true,
                LayerId = data.LayerId,
                MinScale = data.MinScale,
                MaxScale = data.MaxScale,
                DefaultVisibility = data.DefaultVisibility,
                DisplayField = data.DisplayField,
                Queryable = data.Queryable,
                HasZ = data.HasZ,
                HasM = data.HasM,
            };
        }

        return ConsoleLayerDisplay.Unbound(
            result.Issue?.Detail ?? "The Honua server did not return display metadata for this layer.");
    }

    public async Task<ConsoleSetLayerMetadataResult> SetDisplayAsync(
        int layerId,
        ConsoleLayerDisplay display,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);

        var request = new HonuaAdminLayerDisplayUpdate
        {
            MinScale = display.MinScale,
            MaxScale = display.MaxScale,
            DefaultVisibility = display.DefaultVisibility,
            DisplayField = display.DisplayField,
            Queryable = display.Queryable,
            HasZ = display.HasZ,
            HasM = display.HasM,
        };

        var result = await _client.UpdateLayerDisplayAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        return MapResult(result.Data is not null, result.Issue, "Saved the layer's display hints on honua-server.");
    }

    public async Task<ConsoleLayerEditing> GetEditingAsync(int layerId, CancellationToken cancellationToken = default)
    {
        var result = await _client.GetLayerEditingAsync(layerId, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } data)
        {
            return new ConsoleLayerEditing
            {
                Bound = true,
                LayerId = data.LayerId,
                GlobalIdField = data.GlobalIdField,
                CreatorField = data.CreatorField,
                CreatedAtField = data.CreatedAtField,
                EditorField = data.EditorField,
                UpdatedAtField = data.UpdatedAtField,
                CanModify = data.CanModify,
                SupportsAttachments = data.SupportsAttachments,
                SupportsRelatedRecords = data.SupportsRelatedRecords,
            };
        }

        return ConsoleLayerEditing.Unbound(
            result.Issue?.Detail ?? "The Honua server did not return editing metadata for this layer.");
    }

    public async Task<ConsoleSetLayerMetadataResult> SetEditingAsync(
        int layerId,
        ConsoleLayerEditing editing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(editing);

        var request = new HonuaAdminLayerEditingUpdate
        {
            GlobalIdField = editing.GlobalIdField,
            CreatorField = editing.CreatorField,
            CreatedAtField = editing.CreatedAtField,
            EditorField = editing.EditorField,
            UpdatedAtField = editing.UpdatedAtField,
            CanModify = editing.CanModify,
            SupportsAttachments = editing.SupportsAttachments,
            SupportsRelatedRecords = editing.SupportsRelatedRecords,
        };

        var result = await _client.UpdateLayerEditingAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        return MapResult(result.Data is not null, result.Issue, "Saved the layer's editing metadata on honua-server.");
    }

    public async Task<ConsoleLayerSpatial> GetSpatialAsync(int layerId, CancellationToken cancellationToken = default)
    {
        var result = await _client.GetLayerSpatialAsync(layerId, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } data)
        {
            return new ConsoleLayerSpatial
            {
                Bound = true,
                LayerId = data.LayerId,
                Srid = data.Srid,
                GeometryType = data.GeometryType,
                SupportedCrs = data.SupportedCrs ?? [],
                StorageCrs = data.StorageCrs,
                StorageCrsCoordinateEpoch = data.StorageCrsCoordinateEpoch,
            };
        }

        return ConsoleLayerSpatial.Unbound(
            result.Issue?.Detail ?? "The Honua server did not return spatial metadata for this layer.");
    }

    public async Task<ConsoleSetLayerMetadataResult> SetSpatialAsync(
        int layerId,
        IReadOnlyList<string>? supportedCrs,
        string? storageCrs,
        double? storageCrsCoordinateEpoch,
        bool clearStorageCrs,
        bool clearStorageCrsCoordinateEpoch,
        CancellationToken cancellationToken = default)
    {
        var request = new HonuaAdminLayerSpatialUpdate
        {
            SupportedCrs = supportedCrs,
            StorageCrs = clearStorageCrs ? null : storageCrs,
            StorageCrsCoordinateEpoch = clearStorageCrsCoordinateEpoch ? null : storageCrsCoordinateEpoch,
            ClearStorageCrs = clearStorageCrs,
            ClearStorageCrsCoordinateEpoch = clearStorageCrsCoordinateEpoch,
        };

        var result = await _client.UpdateLayerSpatialAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        return MapResult(result.Data is not null, result.Issue, "Saved the layer's CRS / spatial metadata on honua-server.");
    }

    private static ConsoleSetLayerMetadataResult MapResult(bool succeeded, HonuaAdminEndpointIssue? issue, string successDetail)
    {
        if (succeeded)
        {
            return new ConsoleSetLayerMetadataResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = successDetail,
            };
        }

        return new ConsoleSetLayerMetadataResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the metadata update.",
        };
    }
}
