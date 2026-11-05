using System.Reflection;
using Honua.Console.Shell;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.IntegrationTests;

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
