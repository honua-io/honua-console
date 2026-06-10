using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the layer relationships operation. Reads and replaces a layer's relationships on
/// honua-server through <see cref="IHonuaAdminOperateClient"/> and maps the result (or rejection). It never
/// fabricates success — every result reflects what the server read back.
/// </summary>
public sealed class HonuaServerConsoleLayerRelationshipsOperation : IConsoleLayerRelationshipsOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerConsoleLayerRelationshipsOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ConsoleLayerRelationships> GetRelationshipsAsync(
        int layerId,
        CancellationToken cancellationToken = default)
    {
        var result = await _client.GetLayerRelationshipsAsync(layerId, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } data)
        {
            return new ConsoleLayerRelationships
            {
                Bound = true,
                LayerId = data.LayerId,
                Relationships = (data.Relationships ?? [])
                    .Select(Map)
                    .ToArray(),
            };
        }

        return ConsoleLayerRelationships.Unbound(
            result.Issue?.Detail ?? "The Honua server did not return relationships for this layer.");
    }

    public async Task<ConsoleSetRelationshipsResult> SetRelationshipsAsync(
        int layerId,
        IReadOnlyList<ConsoleLayerRelationship> relationships,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relationships);

        var request = new HonuaAdminLayerRelationshipsUpdate
        {
            Relationships = relationships
                .Select(rel => new HonuaAdminLayerRelationship
                {
                    Id = string.IsNullOrWhiteSpace(rel.Id) ? null : rel.Id,
                    Name = rel.Name,
                    RelatedLayerId = rel.RelatedLayerId,
                    Role = rel.Role,
                    Cardinality = rel.Cardinality,
                    OriginField = rel.OriginField,
                    DestinationField = rel.DestinationField,
                    EsriRelationshipId = rel.EsriRelationshipId,
                })
                .ToArray(),
        };

        var result = await _client.UpdateLayerRelationshipsAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        if (result.Data is not null)
        {
            return new ConsoleSetRelationshipsResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = relationships.Count == 0
                    ? "Cleared all relationships on this layer."
                    : $"Saved {relationships.Count} relationship(s) on this layer.",
            };
        }

        var issue = result.Issue;
        return new ConsoleSetRelationshipsResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the relationships update.",
        };
    }

    private static ConsoleLayerRelationship Map(HonuaAdminLayerRelationship rel) => new()
    {
        Id = rel.Id,
        Name = rel.Name,
        RelatedLayerId = rel.RelatedLayerId,
        Role = rel.Role,
        Cardinality = rel.Cardinality,
        OriginField = rel.OriginField,
        DestinationField = rel.DestinationField,
        EsriRelationshipId = rel.EsriRelationshipId,
    };
}
