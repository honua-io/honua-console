namespace Honua.Console.Shell.Models;

// The connected server keeps catalog, share and open-data content in process memory, so it would be
// lost on restart; these surfaces stay hidden until the server advertises a persistent store.
public static class CatalogUnavailable
{
    public const string Title = "Catalog and sharing are not available yet";

    public const string Message =
        "This server does not keep catalog and sharing content across restarts yet. Published layers and services are listed in Data & Layers.";
}
