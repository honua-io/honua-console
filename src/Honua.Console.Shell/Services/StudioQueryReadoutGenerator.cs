using System.Globalization;
using System.Text;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Generates the read-only SQL/filter readout the query builder shows over the authored source binding and
/// predicates before save (AC#2). This is a Console-side rendering of the predicate clauses the operator
/// has authored — a SQL-shaped WHERE projection for human review, not the server's compiled query plan
/// (the server owns the canonical query pipeline through honua-server#1182). It is deterministic and pure
/// so it can be unit-tested without a server and re-rendered on every keystroke.
/// </summary>
public static class StudioQueryReadoutGenerator
{
    /// <summary>Builds the SQL-shaped readout (SELECT … FROM … WHERE …) for the authored query.</summary>
    public static string Generate(StudioQueryEditor query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var projection = query.OutFields.Count == 0
            ? "*"
            : string.Join(", ", query.OutFields.Where(f => !string.IsNullOrWhiteSpace(f)));
        if (string.IsNullOrWhiteSpace(projection))
        {
            projection = "*";
        }

        var source = string.IsNullOrWhiteSpace(query.ServiceName)
            ? $"layer:{query.LayerId.ToString(CultureInfo.InvariantCulture)}"
            : $"{query.ServiceName}/layer/{query.LayerId.ToString(CultureInfo.InvariantCulture)}";

        var builder = new StringBuilder();
        builder.Append("SELECT ").Append(projection).Append(" FROM ").Append(source);

        var clauses = query.Predicates
            .Select(RenderClause)
            .Where(clause => !string.IsNullOrWhiteSpace(clause))
            .ToList();

        if (clauses.Count > 0)
        {
            var joiner = string.Equals(query.Combinator, StudioQueryCombinators.Or, StringComparison.OrdinalIgnoreCase)
                ? " OR "
                : " AND ";
            builder.Append(" WHERE ").Append(string.Join(joiner, clauses));
        }

        return builder.ToString();
    }

    private static string RenderClause(StudioQueryPredicateEditor predicate)
    {
        return predicate.Kind switch
        {
            StudioQueryPredicateKinds.Spatial => RenderSpatial(predicate),
            StudioQueryPredicateKinds.Temporal => RenderTemporal(predicate),
            _ => RenderComparison(predicate)
        };
    }

    private static string RenderComparison(StudioQueryPredicateEditor predicate)
    {
        if (string.IsNullOrWhiteSpace(predicate.Field))
        {
            return string.Empty;
        }

        var op = predicate.Operator;
        if (string.Equals(op, "in", StringComparison.OrdinalIgnoreCase))
        {
            var values = predicate.Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(QuoteIfNeeded);
            return $"{predicate.Field} IN ({string.Join(", ", values)})";
        }

        if (string.Equals(op, "like", StringComparison.OrdinalIgnoreCase))
        {
            return $"{predicate.Field} LIKE {Quote(predicate.Value)}";
        }

        return $"{predicate.Field} {op} {QuoteIfNeeded(predicate.Value)}";
    }

    private static string RenderSpatial(StudioQueryPredicateEditor predicate)
    {
        // The authored geometry is a GeoJSON literal; the readout shows a stable placeholder rather than
        // inlining a (potentially large) geometry blob into the SQL-shaped review string.
        const string geometry = "<geometry>";

        if (string.Equals(predicate.Operator, "dwithin", StringComparison.OrdinalIgnoreCase))
        {
            var distance = string.IsNullOrWhiteSpace(predicate.Value) ? "0" : predicate.Value;
            return $"DWITHIN(geometry, {geometry}, {distance} {predicate.DistanceUnit})";
        }

        var op = string.IsNullOrWhiteSpace(predicate.Operator) ? "INTERSECTS" : predicate.Operator.ToUpperInvariant();
        return $"{op}(geometry, {geometry})";
    }

    private static string RenderTemporal(StudioQueryPredicateEditor predicate)
    {
        if (string.IsNullOrWhiteSpace(predicate.Field))
        {
            return string.Empty;
        }

        return predicate.Operator switch
        {
            "before" => $"{predicate.Field} < {Quote(predicate.End)}",
            "after" => $"{predicate.Field} > {Quote(predicate.Start)}",
            "during" => $"{predicate.Field} BETWEEN {Quote(predicate.Start)} AND {Quote(predicate.End)}",
            _ => $"{predicate.Field} {predicate.Operator} {Quote(predicate.Start)}"
        };
    }

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "''";
        }

        if (bool.TryParse(value, out _)
            || double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
        {
            return value;
        }

        return Quote(value);
    }

    private static string Quote(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
