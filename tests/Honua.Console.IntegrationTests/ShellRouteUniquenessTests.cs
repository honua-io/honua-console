using System.Reflection;
using Honua.Console.Shell;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Route-uniqueness guard for the Console Shell's routable components.
///
/// The live Blazor interactive-server <c>Router</c> builds a single combined route table from
/// every <c>[Route]</c> on the app assembly. Two components that declare the same route template
/// make the router throw <c>InvalidOperationException: The following routes are ambiguous</c>,
/// which terminates the interactive circuit on every navigation. Prerender still returns 200, so
/// bUnit/render tests (which mount one component at a time and never build the combined route
/// table) cannot observe the ambiguity — only the real router or a real-browser smoke does.
///
/// This test reconstructs the same enumeration the router performs — every route template across
/// all <c>[RouteAttribute]</c>-bearing components in the Shell assembly — and asserts no template
/// is declared by more than one component. It fails on the historical
/// <c>/operate/jobs/{...}</c> collision (OperateJobPage vs OperateObservabilityPage) and passes
/// once a single component owns each template.
/// </summary>
public sealed class ShellRouteUniquenessTests
{
    [Fact]
    public void NoTwoRoutableComponentsDeclareTheSameRouteTemplate()
    {
        var assembly = typeof(ConsoleRoutes).Assembly;

        var routes =
            from type in assembly.GetTypes()
            where typeof(IComponent).IsAssignableFrom(type)
            from attribute in type.GetCustomAttributes<RouteAttribute>(inherit: false)
            select (Template: NormalizeTemplate(attribute.Template), Component: type.FullName ?? type.Name);

        var duplicates = routes
            .GroupBy(route => route.Template, StringComparer.Ordinal)
            .Where(group => group.Select(route => route.Component).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => $"'{group.Key}' declared by [{string.Join(", ", group.Select(route => route.Component).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal))}]")
            .OrderBy(message => message, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            duplicates.Length == 0,
            "Ambiguous Blazor route templates would crash the interactive circuit at runtime. "
                + "Each route template must be owned by exactly one routable component. Conflicts:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, duplicates));
    }

    [Fact]
    public void OperateObservabilityPageSolelyOwnsTheUnifiedJobRunRoute()
    {
        // docs/console-route-map.md §1/§6.5: /operate/jobs/{jobRunId} is the unified job-run
        // detail deep link owned by OperateObservabilityPage. Pin the owner so the duplicate
        // page cannot be reintroduced.
        var assembly = typeof(ConsoleRoutes).Assembly;
        const string jobRunTemplatePrefix = "/operate/jobs/{";

        var owners =
            (from type in assembly.GetTypes()
             where typeof(IComponent).IsAssignableFrom(type)
             from attribute in type.GetCustomAttributes<RouteAttribute>(inherit: false)
             where attribute.Template.StartsWith(jobRunTemplatePrefix, StringComparison.Ordinal)
             select type.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["OperateObservabilityPage"], owners);
    }

    // Route parameter names are local to each component; the router compares the segment shape, so
    // "/operate/jobs/{JobId}" and "/operate/jobs/{SelectedJobRunId}" collide. Normalize parameter
    // names to a placeholder so the uniqueness check compares the same shape the router resolves.
    private static string NormalizeTemplate(string template)
    {
        var segments = template.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.StartsWith('{') && segment.EndsWith('}'))
            {
                // Preserve catch-all and constraint shape (e.g. {*rest}, {id:int}) but drop the
                // parameter name, which the router treats as positional.
                var inner = segment[1..^1];
                var catchAll = inner.StartsWith('*') ? "*" : string.Empty;
                var colon = inner.IndexOf(':');
                var constraint = colon >= 0 ? inner[colon..] : string.Empty;
                segments[i] = "{" + catchAll + "param" + constraint + "}";
            }
        }

        return string.Join('/', segments);
    }
}
