using System.Text.RegularExpressions;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Shared, pure rule helpers for the report and dashboard builders, which both author the same
/// bindings/panels graph. Extracting these keeps <see cref="StudioReportValidator"/> and
/// <see cref="StudioDashboardValidator"/> declarative and guarantees both editors enforce identical
/// referential / uniqueness / format rules. All methods are side-effect-free (append to a caller-owned
/// error list) and culture-invariant.
/// </summary>
internal static partial class StudioPanelBindingRules
{
    /// <summary>A binding's authoring values, projected for validation regardless of editor type.</summary>
    public readonly record struct BindingView(string Alias, string ContentRef);

    // A server route slug: lowercase letters, digits, and single hyphens between segments. Mirrors the
    // server's slug normalization shape; empty is allowed (the server derives a slug from the title).
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex RouteSlugPattern();

    /// <summary>
    /// Validates the optional route slug's format. Blank is allowed (the server derives the slug from the
    /// title); a non-blank slug must be lowercase alphanumeric with single hyphens between segments.
    /// </summary>
    public static void EvaluateRouteSlug(string? routeSlug, string fieldKey, string surface, List<ConsoleFieldError> errors)
    {
        var trimmed = routeSlug?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        if (!RouteSlugPattern().IsMatch(trimmed))
        {
            errors.Add(new ConsoleFieldError(
                fieldKey,
                $"{surface}.routeSlug.format",
                ConsoleValidationSeverity.Error,
                "Route slug must be lowercase letters, digits, and single hyphens (for example monthly-infrastructure)."));
        }
    }

    /// <summary>Validates that the visibility value is one of the allowed enum members.</summary>
    public static void EvaluateVisibility(
        string? visibility,
        IReadOnlyList<string> allowed,
        string fieldKey,
        string surface,
        List<ConsoleFieldError> errors)
    {
        var trimmed = visibility?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return; // presence/default is the editor's concern; only a non-empty out-of-set value is invalid.
        }

        if (!allowed.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new ConsoleFieldError(
                fieldKey,
                $"{surface}.visibility.invalid",
                ConsoleValidationSeverity.Error,
                $"Visibility '{trimmed}' is not a recognised scope ({string.Join(", ", allowed)})."));
        }
    }

    /// <summary>
    /// Validates every binding (alias + content ref required, aliases unique) and returns the set of
    /// declared aliases for the panel referential check. Each finding is keyed to the offending binding's
    /// per-index field key.
    /// </summary>
    public static HashSet<string> EvaluateBindings(
        IEnumerable<BindingView> bindings,
        Func<int, string> aliasKey,
        Func<int, string> contentRefKey,
        List<ConsoleFieldError> errors)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var index = 0;
        foreach (var binding in bindings)
        {
            var alias = binding.Alias?.Trim() ?? string.Empty;
            if (alias.Length == 0)
            {
                errors.Add(new ConsoleFieldError(
                    aliasKey(index),
                    "binding.alias.required",
                    ConsoleValidationSeverity.Blocker,
                    "Give this data binding an alias."));
            }
            else
            {
                aliases.Add(alias);
                if (!seen.Add(alias))
                {
                    errors.Add(new ConsoleFieldError(
                        aliasKey(index),
                        "binding.alias.duplicate",
                        ConsoleValidationSeverity.Error,
                        $"Binding alias '{alias}' is already used. Binding aliases must be unique."));
                }
            }

            if (string.IsNullOrWhiteSpace(binding.ContentRef))
            {
                errors.Add(new ConsoleFieldError(
                    contentRefKey(index),
                    "binding.contentRef.required",
                    ConsoleValidationSeverity.Blocker,
                    "Give this data binding a content reference."));
            }

            index++;
        }

        return aliases;
    }

    /// <summary>
    /// Validates that a panel's binding alias references a declared binding (referential integrity). A blank
    /// alias is a blocker (the panel reads from nowhere); a non-blank alias that is not a declared binding is
    /// an error.
    /// </summary>
    public static void EvaluatePanelBindingAlias(
        string? bindingAlias,
        HashSet<string> declaredAliases,
        string fieldKey,
        List<ConsoleFieldError> errors)
    {
        var alias = bindingAlias?.Trim() ?? string.Empty;
        if (alias.Length == 0)
        {
            errors.Add(new ConsoleFieldError(
                fieldKey,
                "panel.bindingAlias.required",
                ConsoleValidationSeverity.Blocker,
                "Bind this panel to a declared data binding."));
            return;
        }

        if (!declaredAliases.Contains(alias))
        {
            errors.Add(new ConsoleFieldError(
                fieldKey,
                "panel.bindingAlias.unresolved",
                ConsoleValidationSeverity.Error,
                $"Binding alias '{alias}' does not reference a declared data binding."));
        }
    }

    /// <summary>
    /// Resolves a content-publication report/dashboard-document JSON Pointer to a console field key. Shared
    /// by both editors' pointer resolvers (the document shape — <c>/bindings/{n}/…</c>,
    /// <c>/panels/{n}/…</c>, scalar request fields — is identical). Pointers may be rooted at the request
    /// body or carry a leading <c>body</c> token; both resolve.
    /// </summary>
    public static string? ResolvePointer(
        string? pointer,
        Func<int, string> bindingAlias,
        Func<int, string> bindingContentRef,
        string bindings,
        Func<int, string> panelBindingAlias,
        Func<int, string> panelChartSpec,
        string panels,
        string visibility,
        string routeSlug,
        string title)
    {
        var segments = JsonPointer.Split(pointer);
        if (segments.Count == 0)
        {
            return null;
        }

        var index = 0;
        if (string.Equals(segments[0], "body", StringComparison.OrdinalIgnoreCase))
        {
            index = 1;
        }

        if (index >= segments.Count)
        {
            return null;
        }

        var head = segments[index].ToLowerInvariant();

        if (head == "bindings")
        {
            if (index + 1 < segments.Count && int.TryParse(segments[index + 1], out var bIndex) && bIndex >= 0)
            {
                var leaf = index + 2 < segments.Count ? segments[index + 2].ToLowerInvariant() : null;
                return leaf == "contentref" ? bindingContentRef(bIndex) : bindingAlias(bIndex);
            }

            return bindings;
        }

        if (head == "panels")
        {
            if (index + 1 < segments.Count && int.TryParse(segments[index + 1], out var pIndex) && pIndex >= 0)
            {
                var leaf = index + 2 < segments.Count ? segments[index + 2].ToLowerInvariant() : null;
                return leaf switch
                {
                    "chartspec" => panelChartSpec(pIndex),
                    _ => panelBindingAlias(pIndex),
                };
            }

            return panels;
        }

        return head switch
        {
            "policy" => visibility, // /policy/visibility
            "visibility" => visibility,
            "routeslug" => routeSlug,
            "title" => title,
            _ => null,
        };
    }
}
