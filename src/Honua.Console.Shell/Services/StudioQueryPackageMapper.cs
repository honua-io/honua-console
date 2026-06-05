using System.Globalization;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Maps between the Console query-builder editor state and the honua-server saved-query content contract
/// (honua-server#1182, AnalysisContentKind.SavedQuery). The Console editor is a thin projection over the
/// server-owned <see cref="HonuaSavedQueryContent"/> / <see cref="HonuaFilterPlan"/> graph; this mapper is
/// the single place that lowers an authored query into the server document and lifts a loaded version back
/// into editor state, so the server contract is never duplicated across the data source and the UI.
/// </summary>
public static class StudioQueryPackageMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>A fresh, empty draft template for a not-yet-saved query (no server round-trip).</summary>
    public static StudioQueryEditor CreateTemplate() => new();

    /// <summary>Lowers the authored query into the server saved-query content document.</summary>
    public static HonuaSavedQueryContent ToSavedQueryContent(StudioQueryEditor query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new HonuaSavedQueryContent
        {
            NaturalLanguageQuery = string.IsNullOrWhiteSpace(query.NaturalLanguageQuery)
                ? null
                : query.NaturalLanguageQuery,
            LayerId = query.LayerId,
            ServiceName = string.IsNullOrWhiteSpace(query.ServiceName) ? null : query.ServiceName,
            FilterPlan = ToFilterPlan(query),
            OutFields = query.OutFields
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Select(field => field.Trim())
                .ToArray(),
            OutputSrid = query.OutputSrid,
            PreviewLimit = query.PreviewLimit <= 0 ? null : query.PreviewLimit,
            OutputFormat = query.OutputFormat,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["console.title"] = query.Title,
                ["console.description"] = query.Description,
                ["console.parameters"] = EncodeParameters(query.Parameters)
            }
        };
    }

    /// <summary>Lowers the authored predicates into the server filter plan, or null when none are set.</summary>
    public static HonuaFilterPlan? ToFilterPlan(StudioQueryEditor query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var clauses = query.Predicates
            .Select(ToClause)
            .Where(clause => clause is not null)
            .Select(clause => clause!)
            .ToArray();

        if (clauses.Length == 0)
        {
            return null;
        }

        return new HonuaFilterPlan
        {
            Combinator = string.Equals(query.Combinator, StudioQueryCombinators.Or, StringComparison.OrdinalIgnoreCase)
                ? HonuaFilterPlanCombinators.Or
                : HonuaFilterPlanCombinators.And,
            Clauses = clauses
        };
    }

    /// <summary>Lifts a loaded server saved-query version back into editor state.</summary>
    public static StudioQueryEditor ToEditorState(HonuaAnalysisContentVersionResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var item = response.Item;
        var version = response.Version;
        var content = version.SavedQuery ?? new HonuaSavedQueryContent();
        var metadata = content.Metadata;

        var query = new StudioQueryEditor
        {
            QueryId = string.IsNullOrWhiteSpace(item.ItemId) ? version.ItemId : item.ItemId,
            Version = version.Version,
            ETag = version.ContentHash,
            Title = ResolveTitle(item, metadata),
            Description = metadata.GetValueOrDefault("console.description", string.Empty),
            NaturalLanguageQuery = content.NaturalLanguageQuery ?? string.Empty,
            ServiceName = content.ServiceName ?? string.Empty,
            LayerId = content.LayerId,
            OutputSrid = content.OutputSrid,
            PreviewLimit = content.PreviewLimit is > 0 ? content.PreviewLimit.Value : 25,
            OutputFormat = StudioQueryOutputFormats.All.Contains(content.OutputFormat ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                ? content.OutputFormat!
                : StudioQueryOutputFormats.All[0]
        };

        if (content.FilterPlan is { } plan)
        {
            query.Combinator = string.Equals(plan.Combinator, HonuaFilterPlanCombinators.Or, StringComparison.OrdinalIgnoreCase)
                ? StudioQueryCombinators.Or
                : StudioQueryCombinators.And;
            foreach (var clause in plan.Clauses)
            {
                var predicate = ToPredicate(clause);
                if (predicate is not null)
                {
                    query.Predicates.Add(predicate);
                }
            }
        }

        foreach (var field in content.OutFields)
        {
            if (!string.IsNullOrWhiteSpace(field))
            {
                query.OutFields.Add(field);
            }
        }

        foreach (var parameter in DecodeParameters(metadata.GetValueOrDefault("console.parameters", string.Empty)))
        {
            query.Parameters.Add(parameter);
        }

        return query;
    }

    /// <summary>
    /// Applies a server-proposed saved query (from the natural-language generation contract) onto the
    /// current query editor, preserving the server-owned identity (query id / version / etag) so a refine on
    /// an already-saved draft does not lose it. Source binding, predicates, projection, parameters, output
    /// format/SRID/preview-limit, title/description, and the natural-language text are replaced from the
    /// proposal, mirroring how a reopened version rehydrates. The proposal changed the query, so it must be
    /// re-saved (a new immutable version) before preview; any prior preview is cleared.
    /// </summary>
    public static StudioQueryEditor ApplyGeneratedQuery(StudioQueryEditor current, HonuaSavedQueryContent content)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(content);

        var metadata = content.Metadata;

        // Server-owned identity (QueryId/Version/ETag) stays put; only the authored content is replaced.
        current.Title = ResolveTitle(new HonuaAnalysisContentItem { Title = current.Title }, metadata);
        current.Description = metadata.GetValueOrDefault("console.description", current.Description);
        current.NaturalLanguageQuery = content.NaturalLanguageQuery ?? current.NaturalLanguageQuery;
        current.ServiceName = content.ServiceName ?? string.Empty;
        current.LayerId = content.LayerId;
        current.OutputSrid = content.OutputSrid;
        current.PreviewLimit = content.PreviewLimit is > 0 ? content.PreviewLimit.Value : 25;
        current.OutputFormat = StudioQueryOutputFormats.All.Contains(content.OutputFormat ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? content.OutputFormat!
            : StudioQueryOutputFormats.All[0];

        current.Predicates.Clear();
        current.Combinator = StudioQueryCombinators.And;
        if (content.FilterPlan is { } plan)
        {
            current.Combinator = string.Equals(plan.Combinator, HonuaFilterPlanCombinators.Or, StringComparison.OrdinalIgnoreCase)
                ? StudioQueryCombinators.Or
                : StudioQueryCombinators.And;
            foreach (var clause in plan.Clauses)
            {
                var predicate = ToPredicate(clause);
                if (predicate is not null)
                {
                    current.Predicates.Add(predicate);
                }
            }
        }

        current.OutFields.Clear();
        foreach (var field in content.OutFields)
        {
            if (!string.IsNullOrWhiteSpace(field))
            {
                current.OutFields.Add(field);
            }
        }

        current.Parameters.Clear();
        foreach (var parameter in DecodeParameters(metadata.GetValueOrDefault("console.parameters", string.Empty)))
        {
            current.Parameters.Add(parameter);
        }

        // A changed query must be re-saved + re-previewed.
        current.Preview = null;
        return current;
    }

    /// <summary>Lifts a server saved-query preview result into the builder's map/table preview projection.</summary>
    public static StudioQueryPreview ToPreview(HonuaSavedQueryPreviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var columns = result.Features
            .SelectMany(feature => feature.Attributes.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var features = result.Features
            .Select(feature => new StudioQueryPreviewFeatureView(
                feature.Id,
                feature.HasGeometry,
                feature.Attributes.ToDictionary(
                    pair => pair.Key,
                    pair => RenderAttribute(pair.Value),
                    StringComparer.Ordinal)))
            .ToArray();

        return new StudioQueryPreview(
            result.PreviewArtifactId,
            result.LayerId,
            result.TotalCount,
            result.ExceededPreviewLimit,
            features,
            columns,
            ResolveDownstreamTargets(features.Any(feature => feature.HasGeometry)));
    }

    /// <summary>
    /// The downstream content families a saved query can become (AC#3). A query with geometry can feed a
    /// map; every query can feed dashboards/reports/apps/workflows as a data input.
    /// </summary>
    public static IReadOnlyList<string> ResolveDownstreamTargets(bool hasGeometry)
    {
        var targets = new List<string>();
        if (hasGeometry)
        {
            targets.Add("map");
        }

        targets.Add("dashboard");
        targets.Add("report");
        targets.Add("app");
        targets.Add("workflow");
        return targets;
    }

    private static HonuaFilterPlanClause? ToClause(StudioQueryPredicateEditor predicate)
    {
        switch (predicate.Kind)
        {
            case StudioQueryPredicateKinds.Spatial:
                if (string.IsNullOrWhiteSpace(predicate.Operator))
                {
                    return null;
                }

                return new HonuaFilterPlanClause
                {
                    Type = HonuaFilterClauseTypes.Spatial,
                    Spatial = new HonuaSpatialClause
                    {
                        Operator = predicate.Operator,
                        Geometry = ParseGeometry(predicate.Geometry),
                        Distance = string.Equals(predicate.Operator, "dwithin", StringComparison.OrdinalIgnoreCase)
                            && double.TryParse(predicate.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var distance)
                            ? distance
                            : null,
                        DistanceUnit = string.Equals(predicate.Operator, "dwithin", StringComparison.OrdinalIgnoreCase)
                            ? predicate.DistanceUnit
                            : null
                    }
                };

            case StudioQueryPredicateKinds.Temporal:
                if (string.IsNullOrWhiteSpace(predicate.Field))
                {
                    return null;
                }

                return new HonuaFilterPlanClause
                {
                    Type = HonuaFilterClauseTypes.Temporal,
                    Temporal = new HonuaTemporalClause
                    {
                        Property = predicate.Field,
                        Operator = predicate.Operator,
                        Start = string.IsNullOrWhiteSpace(predicate.Start) ? null : predicate.Start,
                        End = string.IsNullOrWhiteSpace(predicate.End) ? null : predicate.End
                    }
                };

            default:
                if (string.IsNullOrWhiteSpace(predicate.Field))
                {
                    return null;
                }

                return new HonuaFilterPlanClause
                {
                    Type = HonuaFilterClauseTypes.Comparison,
                    Comparison = new HonuaComparisonClause
                    {
                        Property = predicate.Field,
                        Operator = predicate.Operator,
                        Value = ToValueElement(predicate.Operator, predicate.Value)
                    }
                };
        }
    }

    private static StudioQueryPredicateEditor? ToPredicate(HonuaFilterPlanClause clause)
    {
        switch (clause.Type)
        {
            case HonuaFilterClauseTypes.Spatial when clause.Spatial is { } spatial:
                return new StudioQueryPredicateEditor
                {
                    Kind = StudioQueryPredicateKinds.Spatial,
                    Operator = spatial.Operator,
                    Geometry = spatial.Geometry?.GetRawText() ?? string.Empty,
                    Value = spatial.Distance?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    DistanceUnit = spatial.DistanceUnit ?? "meters"
                };

            case HonuaFilterClauseTypes.Temporal when clause.Temporal is { } temporal:
                return new StudioQueryPredicateEditor
                {
                    Kind = StudioQueryPredicateKinds.Temporal,
                    Field = temporal.Property,
                    Operator = temporal.Operator,
                    Start = temporal.Start ?? string.Empty,
                    End = temporal.End ?? string.Empty
                };

            case HonuaFilterClauseTypes.Comparison when clause.Comparison is { } comparison:
                return new StudioQueryPredicateEditor
                {
                    Kind = StudioQueryPredicateKinds.Comparison,
                    Field = comparison.Property,
                    Operator = comparison.Operator,
                    Value = comparison.Value is { } value ? RenderAttribute(value) : string.Empty
                };

            default:
                return null;
        }
    }

    private static JsonElement? ToValueElement(string op, string value)
    {
        if (string.Equals(op, "in", StringComparison.OrdinalIgnoreCase))
        {
            var values = value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
            return JsonSerializer.SerializeToElement(values, JsonOptions);
        }

        if (bool.TryParse(value, out var boolean))
        {
            return JsonSerializer.SerializeToElement(boolean, JsonOptions);
        }

        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            return JsonSerializer.SerializeToElement(number, JsonOptions);
        }

        return JsonSerializer.SerializeToElement(value, JsonOptions);
    }

    private static JsonElement? ParseGeometry(string geometry)
    {
        if (string.IsNullOrWhiteSpace(geometry))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(geometry);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string RenderAttribute(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Undefined => string.Empty,
        JsonValueKind.Array => string.Join(
            ", ",
            element.EnumerateArray().Select(RenderAttribute)),
        _ => element.GetRawText()
    };

    private static string ResolveTitle(HonuaAnalysisContentItem item, IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("console.title", out var title) && !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        if (!string.IsNullOrWhiteSpace(item.Title))
        {
            return item.Title!;
        }

        return item.Name;
    }

    private static string EncodeParameters(IReadOnlyList<StudioQueryParameterEditor> parameters) =>
        string.Join(
            ";",
            parameters
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
                .Select(parameter => $"{parameter.Name}={parameter.Value}"));

    private static IEnumerable<StudioQueryParameterEditor> DecodeParameters(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            yield break;
        }

        foreach (var token in encoded.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = token.Split('=', 2);
            var name = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            yield return new StudioQueryParameterEditor
            {
                Name = name,
                Value = parts.Length > 1 ? parts[1] : string.Empty
            };
        }
    }
}
