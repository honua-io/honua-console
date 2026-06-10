using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the publication-overrides authoring operation. Reads and writes a publication's
/// overrides on honua-server through <see cref="IHonuaAdminOperateClient"/> and maps the result (or rejection).
/// It never fabricates success — every result reflects what the server read back.
/// </summary>
public sealed class HonuaServerConsolePublicationOverridesOperation : IConsolePublicationOverridesOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerConsolePublicationOverridesOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ConsolePublicationOverrides> GetOverridesAsync(
        string publicationId,
        CancellationToken cancellationToken = default)
    {
        var result = await _client.GetPublicationOverridesAsync(publicationId, cancellationToken).ConfigureAwait(false);
        return MapRead(result);
    }

    public async Task<ConsoleSavePublicationOverridesResult> SaveOverridesAsync(
        string publicationId,
        ConsolePublicationOverrides overrides,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var result = await _client
            .UpdatePublicationOverridesAsync(publicationId, ToUpdate(overrides), cancellationToken)
            .ConfigureAwait(false);
        return MapWrite(result);
    }

    private static ConsolePublicationOverrides MapRead(HonuaAdminEndpointResult<HonuaAdminPublicationOverrides> result)
    {
        if (result.Data is { } data)
        {
            return new ConsolePublicationOverrides
            {
                Bound = true,
                PublicationId = data.PublicationId,
                TitleOverride = data.TitleOverride,
                FieldAliases = (data.FieldAliases ?? new Dictionary<string, string>())
                    .Select(pair => new ConsolePublicationFieldAlias { Field = pair.Key, Alias = pair.Value })
                    .ToArray(),
                Capabilities = data.Capabilities ?? [],
                SupportedFormats = data.SupportedFormats ?? [],
                IsPrimary = data.IsPrimary,
            };
        }

        return ConsolePublicationOverrides.Unbound(
            result.Issue?.Detail ?? "The Honua server did not return overrides for this publication.");
    }

    private static ConsoleSavePublicationOverridesResult MapWrite(
        HonuaAdminEndpointResult<HonuaAdminPublicationOverrides> result)
    {
        if (result.Data is not null)
        {
            return new ConsoleSavePublicationOverridesResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = "Saved publication overrides on honua-server.",
            };
        }

        var issue = result.Issue;
        return new ConsoleSavePublicationOverridesResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the publication-overrides update.",
        };
    }

    // The console form authors the FULL overrides set, so the PUT always carries every list/map (an empty
    // list/map therefore clears it server-side, matching the form's "remove all rows" intent), and an empty
    // title clears the override. A C# null scalar would leave the value unchanged, but the form always supplies
    // a concrete value, so the saved state is exactly what the operator entered.
    private static HonuaAdminPublicationOverridesUpdate ToUpdate(ConsolePublicationOverrides overrides) => new()
    {
        // Empty string clears the title server-side (vs. null = unchanged). The form maps a blank box to "".
        TitleOverride = overrides.TitleOverride ?? string.Empty,
        FieldAliases = (overrides.FieldAliases ?? [])
            .Where(row => !string.IsNullOrWhiteSpace(row.Field))
            .GroupBy(row => row.Field!.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Alias?.Trim() ?? string.Empty, StringComparer.Ordinal),
        Capabilities = (overrides.Capabilities ?? []).ToArray(),
        SupportedFormats = (overrides.SupportedFormats ?? []).ToArray(),
        IsPrimary = overrides.IsPrimary,
    };
}
