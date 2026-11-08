namespace Honua.Console.IntegrationTests;

internal static class CatalogAdminPreview
{
    public static void Capture(string name, string markup)
    {
        var directory = Environment.GetEnvironmentVariable("HONUA_CATALOG_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{name}.html"), markup);
    }
}
