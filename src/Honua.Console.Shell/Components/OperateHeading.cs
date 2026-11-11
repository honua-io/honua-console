using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Components;

public static class OperateHeading
{
    // v4 result and section headings sit under a panel's h2; legacy pages keep h2.
    public static RenderFragment Section(bool v4Enabled, string? text) => builder =>
    {
        builder.OpenElement(0, v4Enabled ? "h3" : "h2");
        builder.AddContent(1, text);
        builder.CloseElement();
    };
}
