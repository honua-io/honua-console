namespace Honua.Console.Shell.Models;

public static class ConsoleVocabulary
{
    public static string SurfaceName(string surface) => surface switch
    {
        "Resources" => "Datasets",
        "Members" => "Users",
        _ => surface
    };

    public static string PublishingKindName(PublishingResourceKind kind) => kind switch
    {
        PublishingResourceKind.Service => "Service",
        PublishingResourceKind.Layer => "Layer",
        PublishingResourceKind.CatalogEntry => "Catalog listing",
        PublishingResourceKind.StudioArtifact => "Content",
        _ => "Unknown kind"
    };
}
