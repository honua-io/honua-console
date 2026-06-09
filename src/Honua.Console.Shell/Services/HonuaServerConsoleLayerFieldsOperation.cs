using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the layer field-configuration operation. Reads and writes a layer's field
/// configuration (coded-value domains) on honua-server through <see cref="IHonuaAdminOperateClient"/> and maps
/// the result (or rejection). It never fabricates success.
/// </summary>
public sealed class HonuaServerConsoleLayerFieldsOperation : IConsoleLayerFieldsOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerConsoleLayerFieldsOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ConsoleLayerFields> GetFieldsAsync(int layerId, CancellationToken cancellationToken = default)
    {
        var result = await _client.GetLayerFieldsAsync(layerId, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } data)
        {
            return new ConsoleLayerFields
            {
                Bound = true,
                LayerId = data.LayerId,
                Fields = (data.Fields ?? [])
                    .Where(field => !string.IsNullOrWhiteSpace(field.Name))
                    .Select(field => new ConsoleLayerField
                    {
                        Name = field.Name!,
                        Type = field.Type,
                        Alias = field.Alias,
                        DomainName = field.Domain?.Name,
                        CodedValues = (field.Domain?.CodedValues ?? [])
                            .Select(cv => new ConsoleCodedValue(cv.Code ?? string.Empty, cv.Name ?? string.Empty))
                            .ToArray(),
                        Hidden = field.Hidden,
                    })
                    .ToArray(),
            };
        }

        return ConsoleLayerFields.Unbound(
            result.Issue?.Detail ?? "The Honua server did not return field configuration for this layer.");
    }

    public async Task<ConsoleSetDomainResult> SetCodedValueDomainAsync(
        int layerId,
        string fieldName,
        string domainName,
        IReadOnlyList<ConsoleCodedValue> codedValues,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        // An empty coded-value list clears the domain (Domain = null); otherwise set a codedValue domain.
        HonuaAdminFieldDomain? domain = codedValues.Count == 0
            ? null
            : new HonuaAdminFieldDomain
            {
                Name = string.IsNullOrWhiteSpace(domainName) ? fieldName : domainName,
                Type = "codedValue",
                CodedValues = codedValues
                    .Select(cv => new HonuaAdminCodedValue { Code = cv.Code, Name = cv.Label })
                    .ToArray(),
            };

        var request = new HonuaAdminLayerFieldsUpdate
        {
            Fields = [new HonuaAdminLayerFieldUpdate { Name = fieldName, Domain = domain }],
        };

        var result = await _client.UpdateLayerFieldsAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        if (result.Data is not null)
        {
            return new ConsoleSetDomainResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = domain is null
                    ? $"Cleared the domain on '{fieldName}'."
                    : $"Set a coded-value domain with {codedValues.Count} value(s) on '{fieldName}'.",
            };
        }

        var issue = result.Issue;
        return new ConsoleSetDomainResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the field-domain update.",
        };
    }

    public async Task<ConsoleSetDomainResult> SetFieldConfigurationAsync(
        int layerId,
        string fieldName,
        string? alias,
        bool hidden,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        // Carry only alias + hidden; leaving Domain null on the update record preserves the field's existing
        // domain (the server only mutates the properties the request sets). An empty alias is normalized to null
        // so the server clears any prior override instead of persisting an empty string.
        var normalizedAlias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        var request = new HonuaAdminLayerFieldsUpdate
        {
            Fields =
            [
                new HonuaAdminLayerFieldUpdate
                {
                    Name = fieldName,
                    Alias = normalizedAlias,
                    Hidden = hidden,
                }
            ],
        };

        var result = await _client.UpdateLayerFieldsAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        if (result.Data is not null)
        {
            return new ConsoleSetDomainResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = $"Set alias '{normalizedAlias ?? "(none)"}' and {(hidden ? "hidden" : "visible")} on '{fieldName}'.",
            };
        }

        var issue = result.Issue;
        return new ConsoleSetDomainResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the field configuration update.",
        };
    }
}
