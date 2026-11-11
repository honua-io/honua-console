using AngleSharp.Html.Parser;

namespace Honua.Console.IntegrationTests;

internal static class CatalogAdminPreview
{
    public static void CaptureV4(string name, string markup) =>
        Capture(name, $"<div class=\"console-shell\" data-console-v4=\"true\">{markup}</div>");

    public static void Capture(string name, string markup)
    {
        var directory = Environment.GetEnvironmentVariable("HONUA_CATALOG_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var document = new HtmlParser().ParseDocument(markup);
        foreach (var textarea in document.QuerySelectorAll("textarea[value]"))
        {
            textarea.TextContent = textarea.GetAttribute("value") ?? string.Empty;
            textarea.RemoveAttribute("value");
        }
        File.WriteAllText(Path.Combine(directory, $"{name}.html"), document.Body!.InnerHtml);
    }
}
