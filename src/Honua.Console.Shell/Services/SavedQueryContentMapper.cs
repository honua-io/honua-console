using System.Globalization;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Maps the editor's mutable <see cref="SavedQueryDefinitionDraft"/> to and from the server-owned
/// <see cref="SavedQueryContent"/>, and renders the Console-side "generated filter" readout. The
/// readout is a human-readable rendering of the bound source + predicates (pseudo-SQL); the server
/// emits no SQL, so it is explicitly a filter readout, not the server's executed query.
/// </summary>
public static class SavedQueryContentMapper
{
    public static SavedQueryContent ToContent(SavedQueryDefinitionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return new SavedQueryContent
        {
            NaturalLanguageQuery = NullIfBlank(draft.NaturalLanguageQuery),
            LayerId = draft.LayerId,
            ServiceName = NullIfBlank(draft.ServiceName),
            OutFields = ParseFields(draft.OutFields),
            OutputSrid = draft.OutputSrid,
            PreviewLimit = draft.PreviewLimit,
            Units = NullIfBlank(draft.Units),
            FilterPlan = BuildFilterPlan(draft)
        };
    }

    public static SavedQueryDefinitionDraft ToDraft(SavedQueryContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var draft = new SavedQueryDefinitionDraft
        {
            NaturalLanguageQuery = content.NaturalLanguageQuery ?? string.Empty,
            LayerId = content.LayerId,
            ServiceName = content.ServiceName ?? string.Empty,
            OutFields = string.Join(", ", content.OutFields),
            OutputSrid = content.OutputSrid,
            Units = content.Units ?? string.Empty,
            PreviewLimit = content.PreviewLimit,
            Combinator = content.FilterPlan?.Combinator ?? FilterPlanCombinator.And
        };

        if (content.FilterPlan is { } plan)
        {
            foreach (var clause in plan.Clauses)
            {
                var predicate = ToPredicate(clause);
                if (predicate is not null)
                {
                    draft.Predicates.Add(predicate);
                }
            }
        }

        return draft;
    }

    public static string RenderFilterReadout(SavedQueryDefinitionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var fields = ParseFields(draft.OutFields);
        var projection = fields.Count == 0 ? "*" : string.Join(", ", fields);
        var source = DescribeSource(draft);

        var builder = new StringBuilder();
        builder.Append("SELECT ").Append(projection).Append(" FROM ").Append(source);

        var predicateText = draft.Predicates
            .Select(RenderPredicate)
            .Where(text => !string.IsNullOrEmpty(text))
            .ToArray();

        if (predicateText.Length > 0)
        {
            var joiner = draft.Combinator == FilterPlanCombinator.Or ? " OR " : " AND ";
            builder.Append(Environment.NewLine).Append("WHERE ").Append(string.Join(joiner, predicateText));
        }
        else
        {
            builder.Append(Environment.NewLine).Append("-- no predicates: returns all features from the bound source");
        }

        if (draft.PreviewLimit is { } limit && limit > 0)
        {
            builder.Append(Environment.NewLine).Append("LIMIT ").Append(limit.ToString(CultureInfo.InvariantCulture));
        }

        if (draft.OutputSrid is { } srid)
        {
            builder.Append(Environment.NewLine).Append("-- output SRID: ").Append(srid.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string DescribeSource(SavedQueryDefinitionDraft draft)
    {
        var layer = $"layer:{draft.LayerId.ToString(CultureInfo.InvariantCulture)}";
        return string.IsNullOrWhiteSpace(draft.ServiceName) ? layer : $"{draft.ServiceName.Trim()}/{layer}";
    }

    private static FilterPlan? BuildFilterPlan(SavedQueryDefinitionDraft draft)
    {
        var clauses = draft.Predicates
            .Select(ToClause)
            .Where(clause => clause is not null)
            .Select(clause => clause!)
            .ToArray();

        return clauses.Length == 0
            ? null
            : new FilterPlan { Combinator = draft.Combinator, Clauses = clauses };
    }

    private static FilterPlanClause? ToClause(SavedQueryPredicateDraft predicate) => predicate.Kind switch
    {
        SavedQueryPredicateKind.Attribute when !string.IsNullOrWhiteSpace(predicate.Property) =>
            new FilterPlanClause
            {
                Type = "comparison",
                Comparison = new ComparisonClause
                {
                    Property = predicate.Property.Trim(),
                    Operator = NormalizeAttributeOperator(predicate.Operator),
                    Value = ParseAttributeValue(predicate.Value, predicate.Operator)
                }
            },
        SavedQueryPredicateKind.Spatial =>
            new FilterPlanClause
            {
                Type = "spatial",
                Spatial = new SpatialClause
                {
                    Operator = NullableTrim(predicate.Operator) ?? "intersects",
                    Geometry = ParseGeometry(predicate.GeometryGeoJson),
                    Distance = predicate.Distance,
                    DistanceUnit = NullIfBlank(predicate.DistanceUnit)
                }
            },
        SavedQueryPredicateKind.Temporal when !string.IsNullOrWhiteSpace(predicate.Property) =>
            new FilterPlanClause
            {
                Type = "temporal",
                Temporal = new TemporalClause
                {
                    Property = predicate.Property.Trim(),
                    Operator = NullableTrim(predicate.Operator) ?? "during",
                    Start = NullIfBlank(predicate.Start),
                    End = NullIfBlank(predicate.End)
                }
            },
        _ => null
    };

    private static SavedQueryPredicateDraft? ToPredicate(FilterPlanClause clause)
    {
        if (clause.Comparison is { } comparison)
        {
            return new SavedQueryPredicateDraft
            {
                Kind = SavedQueryPredicateKind.Attribute,
                Property = comparison.Property,
                Operator = string.IsNullOrWhiteSpace(comparison.Operator) ? "eq" : comparison.Operator,
                Value = FormatValue(comparison.Value)
            };
        }

        if (clause.Spatial is { } spatial)
        {
            return new SavedQueryPredicateDraft
            {
                Kind = SavedQueryPredicateKind.Spatial,
                Operator = string.IsNullOrWhiteSpace(spatial.Operator) ? "intersects" : spatial.Operator,
                GeometryGeoJson = spatial.Geometry?.GetRawText() ?? string.Empty,
                Distance = spatial.Distance,
                DistanceUnit = spatial.DistanceUnit ?? string.Empty
            };
        }

        if (clause.Temporal is { } temporal)
        {
            return new SavedQueryPredicateDraft
            {
                Kind = SavedQueryPredicateKind.Temporal,
                Property = temporal.Property,
                Operator = string.IsNullOrWhiteSpace(temporal.Operator) ? "during" : temporal.Operator,
                Start = temporal.Start ?? string.Empty,
                End = temporal.End ?? string.Empty
            };
        }

        // Nested clauses are not authored by this one-level builder; surface them read-only as a note.
        return null;
    }

    private static string RenderPredicate(SavedQueryPredicateDraft predicate) => predicate.Kind switch
    {
        SavedQueryPredicateKind.Attribute when !string.IsNullOrWhiteSpace(predicate.Property) =>
            $"{predicate.Property.Trim()} {AttributeOperatorSymbol(predicate.Operator)} {RenderAttributeValue(predicate)}",
        SavedQueryPredicateKind.Spatial =>
            RenderSpatial(predicate),
        SavedQueryPredicateKind.Temporal when !string.IsNullOrWhiteSpace(predicate.Property) =>
            RenderTemporal(predicate),
        _ => string.Empty
    };

    private static string RenderSpatial(SavedQueryPredicateDraft predicate)
    {
        var op = NullableTrim(predicate.Operator) ?? "intersects";
        var geometry = string.IsNullOrWhiteSpace(predicate.GeometryGeoJson) ? "<geometry>" : "<geojson>";
        if (string.Equals(op, "dwithin", StringComparison.OrdinalIgnoreCase))
        {
            var distance = predicate.Distance?.ToString(CultureInfo.InvariantCulture) ?? "<distance>";
            var unit = string.IsNullOrWhiteSpace(predicate.DistanceUnit) ? "meters" : predicate.DistanceUnit.Trim();
            return $"ST_DWithin(geometry, {geometry}, {distance} {unit})";
        }

        return $"ST_{Capitalize(op)}(geometry, {geometry})";
    }

    private static string RenderTemporal(SavedQueryPredicateDraft predicate)
    {
        var property = predicate.Property.Trim();
        var op = (NullableTrim(predicate.Operator) ?? "during").ToLowerInvariant();
        var start = string.IsNullOrWhiteSpace(predicate.Start) ? "<start>" : predicate.Start.Trim();
        var end = string.IsNullOrWhiteSpace(predicate.End) ? "<end>" : predicate.End.Trim();
        return op switch
        {
            "before" => $"{property} < {start}",
            "after" => $"{property} > {start}",
            _ => $"{property} BETWEEN {start} AND {end}"
        };
    }

    private static string RenderAttributeValue(SavedQueryPredicateDraft predicate)
    {
        if (string.Equals(predicate.Operator, "in", StringComparison.OrdinalIgnoreCase))
        {
            var members = predicate.Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return members.Length == 0 ? "(<values>)" : $"({string.Join(", ", members)})";
        }

        return string.IsNullOrWhiteSpace(predicate.Value) ? "<value>" : predicate.Value.Trim();
    }

    private static object? ParseAttributeValue(string raw, string op)
    {
        var value = (raw ?? string.Empty).Trim();
        if (string.Equals(op, "in", StringComparison.OrdinalIgnoreCase))
        {
            return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return longValue;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return doubleValue;
        }

        return value;
    }

    private static JsonElement? ParseGeometry(string geoJson)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(geoJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Invalid GeoJSON is dropped here and surfaced to the user by the readout/validation, not sent.
            return null;
        }
    }

    private static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        JsonElement element => FormatJsonElement(element),
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string FormatJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => string.Join(
            ", ",
            element.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())),
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => element.GetRawText()
    };

    private static string AttributeOperatorSymbol(string op) => (NullableTrim(op) ?? "eq").ToLowerInvariant() switch
    {
        "eq" => "=",
        "ne" => "<>",
        "lt" => "<",
        "le" => "<=",
        "gt" => ">",
        "ge" => ">=",
        "like" => "LIKE",
        "in" => "IN",
        var other => other.ToUpperInvariant()
    };

    private static string NormalizeAttributeOperator(string op) =>
        (NullableTrim(op) ?? "eq").ToLowerInvariant();

    private static IReadOnlyList<string> ParseFields(string raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NullableTrim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
