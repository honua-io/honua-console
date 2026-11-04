using System.Text.RegularExpressions;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

internal static partial class StudioPanelBindingRules
{
    public readonly record struct BindingView(string Alias, string ContentRef);

    // A server route slug: lowercase letters, digits, and single hyphens between segments. Mirrors the
    // server's slug normalization shape; empty is allowed (the server derives a slug from the title).
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex RouteSlugPattern();

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
