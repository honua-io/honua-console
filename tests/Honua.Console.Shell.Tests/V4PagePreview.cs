using AngleSharp.Html.Parser;

namespace Honua.Console.Shell.Tests;

internal static class V4PagePreview
{
    public static void Capture(string name, string markup)
    {
        var directory = Environment.GetEnvironmentVariable("HONUA_CATALOG_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{name}.html"),
            $"<div class=\"console-shell\" data-console-v4=\"true\">{new HtmlParser().ParseDocument(markup).Body!.InnerHtml}</div>");
    }
}
