using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the Studio app builder. The client validator
/// (<see cref="StudioAppValidator"/>), the inline render surfaces, and the server-error resolver
/// (<see cref="StudioAppServerErrorBinder"/>) all share these so a client finding and a server finding for
/// the same input land on the same key. Form-level keys are constants; per-page and per-action keys are
/// derived from the item's <em>position</em> (which is exactly what the server's <c>/pages/{n}/…</c> and
/// <c>/actions/{n}/…</c> JSON Pointers address) so every authored row is independently addressable.
/// </summary>
public static class StudioAppFieldKeys
{
    public const string Title = "app.title";
    public const string Visibility = "app.visibility";
    public const string Pages = "app.pages";
    public const string Actions = "app.actions";

    /// <summary>Per-page route key for the page at <paramref name="index"/>.</summary>
    public static string PageRoute(int index) => $"app.page[{index}].route";

    /// <summary>Per-page content-binding key for the page at <paramref name="index"/>.</summary>
    public static string PageBinding(int index) => $"app.page[{index}].binding";

    /// <summary>Per-action page-route key for the action at <paramref name="index"/>.</summary>
    public static string ActionPageRoute(int index) => $"app.action[{index}].pageRoute";

    /// <summary>Per-action required-permission key for the action at <paramref name="index"/>.</summary>
    public static string ActionPermission(int index) => $"app.action[{index}].requiredPermission";
}

/// <summary>
/// Pure client-side cross-field / referential / format validator for the Studio app builder, mirroring the
/// <see cref="StudioReportValidator"/> / <see cref="StudioMapValidator"/> pattern: it examines the
/// console-owned <see cref="StudioAppEditorState"/> and emits field-addressable
/// <see cref="ConsoleFieldError"/> findings keyed by <see cref="StudioAppFieldKeys"/> so the editor can
/// surface them inline next to the offending input. It complements — never replaces — server validation;
/// it covers the rules expressible against console-owned state:
/// <list type="bullet">
///   <item>required: at least one page, each page's route + bound component, each action's permission;</item>
///   <item>each page route must start with <c>/</c>;</item>
///   <item>page routes must be unique;</item>
///   <item>each action's <c>PageRoute</c> must reference an existing page route (referential);</item>
///   <item>Visibility and each action's RequiredPermission must be a recognised enum member.</item>
/// </list>
/// </summary>
public sealed class StudioAppValidator : IFieldValidator<StudioAppEditorState>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static StudioAppValidator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleFieldError> Evaluate(StudioAppEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        if (string.IsNullOrWhiteSpace(state.Title))
        {
            errors.Add(Blocker(StudioAppFieldKeys.Title, "app.title.required", "Give the app a title."));
        }

        if (!StudioAppVisibilityModes.All.Contains(state.Visibility?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(state.Visibility))
        {
            errors.Add(Error(
                StudioAppFieldKeys.Visibility,
                "app.visibility.invalid",
                $"Visibility '{state.Visibility}' is not a recognised scope ({string.Join(", ", StudioAppVisibilityModes.All)})."));
        }

        if (state.Pages.Count == 0)
        {
            errors.Add(Blocker(StudioAppFieldKeys.Pages, "app.pages.required", "Add at least one app page."));
        }

        // Track declared routes (trimmed) for the referential action.PageRoute check and uniqueness.
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < state.Pages.Count; index++)
        {
            var page = state.Pages[index];
            var route = page.Route?.Trim() ?? string.Empty;

            if (route.Length == 0)
            {
                errors.Add(Blocker(
                    StudioAppFieldKeys.PageRoute(index),
                    "app.page.route.required",
                    "Give this page a route."));
            }
            else
            {
                routes.Add(route);

                if (!route.StartsWith('/'))
                {
                    errors.Add(Error(
                        StudioAppFieldKeys.PageRoute(index),
                        "app.page.route.format",
                        "Page route must start with '/' (for example /map)."));
                }

                if (!seenRoutes.Add(route))
                {
                    errors.Add(Error(
                        StudioAppFieldKeys.PageRoute(index),
                        "app.page.route.duplicate",
                        $"Page route '{route}' is already used. Page routes must be unique."));
                }
            }

            if (string.IsNullOrWhiteSpace(page.ContentBinding))
            {
                errors.Add(Blocker(
                    StudioAppFieldKeys.PageBinding(index),
                    "app.page.binding.required",
                    "Bind this page's component to a saved content version."));
            }
        }

        for (var index = 0; index < state.Actions.Count; index++)
        {
            var action = state.Actions[index];

            if (string.IsNullOrWhiteSpace(action.RequiredPermission))
            {
                errors.Add(Blocker(
                    StudioAppFieldKeys.ActionPermission(index),
                    "app.action.permission.required",
                    "Declare a required permission for this action."));
            }
            else if (!StudioAppPermissions.All.Contains(action.RequiredPermission.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(Error(
                    StudioAppFieldKeys.ActionPermission(index),
                    "app.action.permission.invalid",
                    $"Permission '{action.RequiredPermission}' is not a recognised permission ({string.Join(", ", StudioAppPermissions.All)})."));
            }

            var pageRoute = action.PageRoute?.Trim() ?? string.Empty;
            if (pageRoute.Length == 0)
            {
                errors.Add(Error(
                    StudioAppFieldKeys.ActionPageRoute(index),
                    "app.action.pageRoute.required",
                    "Wire this action to a page route."));
            }
            else if (!routes.Contains(pageRoute))
            {
                errors.Add(Error(
                    StudioAppFieldKeys.ActionPageRoute(index),
                    "app.action.pageRoute.unresolved",
                    $"Page route '{pageRoute}' does not reference an existing app page."));
            }
        }

        return errors;
    }

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);

    private static ConsoleFieldError Error(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Error, message);
}

/// <summary>
/// Binds the server-returned Studio app-package validation diagnostics (<c>{code,severity,path,message}</c>,
/// the app.package lifecycle validate shape) onto the app editor's <see cref="ValidationState"/> server
/// channel, keyed by the same <see cref="StudioAppFieldKeys"/> the client validator uses, by resolving each
/// diagnostic's JSON Pointer (e.g. <c>/pages/2/route</c>, <c>/actions/0/pageRoute</c>) to the matching
/// console field key. A diagnostic whose pointer cannot be resolved falls back to the raw locator /
/// form-level key so it still surfaces.
/// </summary>
public static class StudioAppServerErrorBinder
{
    /// <summary>Maps app validation items (<c>{severity,code,path,message}</c>) onto console field keys.</summary>
    public static IReadOnlyList<ConsoleFieldError> Map(IEnumerable<StudioAppValidationItem>? issues)
    {
        if (issues is null)
        {
            return Array.Empty<ConsoleFieldError>();
        }

        var mapper = new ServerFieldErrorMapper((locator, _) => StudioAppPointerResolver.Resolve(locator));
        return mapper.Map(issues.Select(issue => new ConsoleFieldValidationError
        {
            Code = issue.Code,
            Severity = issue.Severity,
            Path = issue.Path,
            Message = issue.Message,
        }));
    }
}

/// <summary>
/// Resolves a Studio app-package JSON Pointer (the <c>path</c> on a server diagnostic) to the console-owned
/// <see cref="StudioAppFieldKeys"/> for the offending input. The server addresses the app.package envelope
/// body, so the pointers look like <c>/pages/2/route</c>, <c>/pages/2/component/binding</c>,
/// <c>/actions/0/pageRoute</c>, <c>/actions/0/requiredPermission</c>, plus the scalar request fields
/// <c>/title</c> and <c>/sharePolicy/visibility</c>. A leading <c>body</c> token is tolerated. Returns
/// <see langword="null"/> for an unrecognised pointer so the mapper falls back to the raw locator.
/// </summary>
public static class StudioAppPointerResolver
{
    /// <summary>Resolves <paramref name="pointer"/> to a console field key, or <see langword="null"/>.</summary>
    public static string? Resolve(string? pointer)
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

        if (head == "pages")
        {
            if (index + 1 < segments.Count && int.TryParse(segments[index + 1], out var pageIndex) && pageIndex >= 0)
            {
                var leaf = index + 2 < segments.Count ? segments[index + 2].ToLowerInvariant() : null;
                return leaf switch
                {
                    "component" or "binding" or "contentbinding" => StudioAppFieldKeys.PageBinding(pageIndex),
                    _ => StudioAppFieldKeys.PageRoute(pageIndex),
                };
            }

            return StudioAppFieldKeys.Pages;
        }

        if (head == "actions")
        {
            if (index + 1 < segments.Count && int.TryParse(segments[index + 1], out var actionIndex) && actionIndex >= 0)
            {
                var leaf = index + 2 < segments.Count ? segments[index + 2].ToLowerInvariant() : null;
                return leaf switch
                {
                    "requiredpermission" or "permission" => StudioAppFieldKeys.ActionPermission(actionIndex),
                    _ => StudioAppFieldKeys.ActionPageRoute(actionIndex),
                };
            }

            return StudioAppFieldKeys.Actions;
        }

        return head switch
        {
            "title" => StudioAppFieldKeys.Title,
            "sharepolicy" or "visibility" => StudioAppFieldKeys.Visibility,
            _ => null,
        };
    }
}
