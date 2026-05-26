using System.Globalization;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Server-backed <see cref="ISavedQueryEditorService"/>. Binds the saved-query content lifecycle and
/// preview to honua-server's Analysis Content API (#1182) through the
/// <see cref="IAnalysisContentClient"/> shim. Maps editor drafts to/from the server-owned
/// <see cref="SavedQueryContent"/> and converts endpoint issues into a
/// <see cref="SavedQueryBindingState"/> for the editor's missing-binding / forbidden / conflict surfaces
/// rather than fabricating success. No saved-query data is mocked.
/// </summary>
public sealed class HonuaServerSavedQueryEditorService : ISavedQueryEditorService
{
    private readonly IAnalysisContentClient _client;

    public HonuaServerSavedQueryEditorService(IAnalysisContentClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public SavedQueryBindingState? GetBindingStatus() => null;

    public async Task<SavedQuerySaveResult> CreateAsync(
        SavedQueryDefinitionDraft definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var request = new CreateAnalysisContentItemRequest
        {
            Kind = AnalysisContentKind.SavedQuery,
            Name = ResolveName(definition),
            Title = string.IsNullOrWhiteSpace(definition.Title) ? null : definition.Title.Trim(),
            SavedQuery = SavedQueryContentMapper.ToContent(definition)
        };

        var result = await _client.CreateSavedQueryItemAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess && result.Data is not null
            ? SavedQuerySaveResult.Success(ToHandle(result.Data.Item))
            : SavedQuerySaveResult.Blocked(ToBindingState(result.Issue));
    }

    public async Task<SavedQueryOpenResult> OpenAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return SavedQueryOpenResult.Blocked(new SavedQueryBindingState(
                "Unavailable",
                "GET /api/v1/analysis/content/items/{itemId}",
                "Provide a saved-query item id to reopen."));
        }

        var result = await _client.GetItemAsync(itemId.Trim(), cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return SavedQueryOpenResult.Blocked(ToBindingState(result.Issue));
        }

        var version = result.Data.Version;
        if (result.Data.Item.Kind != AnalysisContentKind.SavedQuery || version.SavedQuery is null)
        {
            return SavedQueryOpenResult.Blocked(new SavedQueryBindingState(
                "Unsupported",
                "GET /api/v1/analysis/content/items/{itemId}",
                "The requested content item is not a saved query and cannot be opened in this editor."));
        }

        var draft = SavedQueryContentMapper.ToDraft(version.SavedQuery);
        draft.Name = result.Data.Item.Name;
        draft.Title = result.Data.Item.Title ?? string.Empty;
        return SavedQueryOpenResult.Success(draft, ToHandle(result.Data.Item));
    }

    public async Task<SavedQuerySaveResult> SaveNewVersionAsync(
        string itemId,
        string? basedOnVersionId,
        SavedQueryDefinitionDraft definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(itemId))
        {
            return SavedQuerySaveResult.Blocked(new SavedQueryBindingState(
                "Unavailable",
                "POST /api/v1/analysis/content/items/{itemId}/versions",
                "Save the saved query before adding a new version."));
        }

        var request = new CreateAnalysisContentVersionRequest
        {
            SavedQuery = SavedQueryContentMapper.ToContent(definition),
            BasedOnVersionId = string.IsNullOrWhiteSpace(basedOnVersionId) ? null : basedOnVersionId
        };

        var result = await _client.AddVersionAsync(itemId.Trim(), request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess && result.Data is not null
            ? SavedQuerySaveResult.Success(ToHandle(result.Data.Item))
            : SavedQuerySaveResult.Blocked(ToBindingState(result.Issue));
    }

    public async Task<SavedQueryPreviewOutcome> PreviewAsync(
        string itemId,
        int contentVersion,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return SavedQueryPreviewOutcome.Blocked(new SavedQueryBindingState(
                "Unavailable",
                "POST /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}/preview",
                "Save the saved query before requesting a preview."));
        }

        var request = new PreviewSavedQueryRequest { Limit = limit };
        var result = await _client
            .PreviewSavedQueryAsync(itemId.Trim(), contentVersion, request, cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess && result.Data is not null
            ? SavedQueryPreviewOutcome.Success(ToPreviewView(result.Data))
            : SavedQueryPreviewOutcome.Blocked(ToBindingState(result.Issue));
    }

    private static string ResolveName(SavedQueryDefinitionDraft definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.Name))
        {
            return definition.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(definition.Title))
        {
            return Slugify(definition.Title);
        }

        return $"saved-query-{Guid.NewGuid().ToString("N")[..8]}";
    }

    private static string Slugify(string value)
    {
        var slug = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        return string.IsNullOrEmpty(slug) ? $"saved-query-{Guid.NewGuid().ToString("N")[..8]}" : slug;
    }

    private static SavedQueryHandle ToHandle(AnalysisContentItem item) => new(
        item.ItemId,
        item.Name,
        item.Title,
        item.Visibility,
        item.CurrentVersion,
        item.CurrentVersionId);

    private static SavedQueryPreviewView ToPreviewView(SavedQueryPreviewResult result)
    {
        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in result.Features)
        {
            foreach (var key in feature.Attributes.Keys)
            {
                if (seen.Add(key))
                {
                    columns.Add(key);
                }
            }
        }

        var rows = result.Features
            .Select(feature => new SavedQueryPreviewRow(
                feature.Id,
                feature.HasGeometry,
                columns.ToDictionary(
                    column => column,
                    column => feature.Attributes.TryGetValue(column, out var value)
                        ? FormatCell(value)
                        : string.Empty,
                    StringComparer.Ordinal)))
            .ToArray();

        var geometryCount = result.Features.Count(feature => feature.HasGeometry);

        return new SavedQueryPreviewView(
            result.PreviewArtifactId,
            result.Version,
            result.LayerId,
            columns,
            rows,
            result.TotalCount,
            result.ExceededPreviewLimit,
            result.Features.Count,
            geometryCount,
            ToArtifactBinding(result.Binding));
    }

    private static SavedQueryArtifactBinding ToArtifactBinding(ArtifactBindingRef binding) => new(
        binding.ArtifactId,
        binding.Role,
        binding.TargetKind,
        binding.TargetSlot,
        binding.SourceVersionId);

    private static string FormatCell(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.Array => string.Join(", ", element.EnumerateArray().Select(item => item.GetRawText())),
        _ => element.GetRawText()
    };

    private static SavedQueryBindingState ToBindingState(AnalysisContentEndpointIssue? issue) =>
        issue is null
            ? new SavedQueryBindingState(
                "Unavailable",
                "honua-server Analysis Content API",
                "The Honua server did not return saved-query content.")
            : new SavedQueryBindingState(issue.State, issue.Contract, issue.Detail);
}
