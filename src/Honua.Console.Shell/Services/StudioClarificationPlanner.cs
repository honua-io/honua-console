using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Builds the Console-local structured clarification gate shown before a draft is generated/saved. The
/// honua-server package API has no clarification concept; this is pure Console UX so the builder never
/// generates against an ambiguous source binding or publication intent. Shared by the server-backed and
/// demo authoring shells.
/// </summary>
public static class StudioClarificationPlanner
{
    public static IReadOnlyList<StudioClarificationQuestion> Build(string? prompt, bool isPublicationFamily)
    {
        var normalized = (prompt ?? string.Empty).ToLowerInvariant();
        var questions = new List<StudioClarificationQuestion>();

        if (string.IsNullOrWhiteSpace(prompt)
            || !ContainsAny(normalized, "from", "using", "layer", "dataset", "table", "catalog", "parcels", "permits", "incidents", "schools", "saved map"))
        {
            questions.Add(new StudioClarificationQuestion(
                "source-binding",
                "Select the source binding",
                "Studio cannot validate fields, CRS, permissions, or lineage without a source.",
                [
                    new("catalog-search", "Search Catalog for a source", "Use Catalog search before binding source data."),
                    new("saved-map", "Use the current saved map", "Bind the generated package to the current saved map."),
                    new("upload", "Start from an uploaded table", "Bind the package after the uploaded table is registered.")
                ]));
        }

        if (isPublicationFamily
            && !ContainsAny(normalized, "public", "private", "org", "embed", "share", "internal", "published"))
        {
            questions.Add(new StudioClarificationQuestion(
                "publish-intent",
                "Choose the publication intent",
                "Saved version and published release require different package state.",
                [
                    new("draft-only", "Keep as a private draft", "Do not create a publication record until review."),
                    new("org-preview", "Prepare an organization preview", "Validate org-visible dependencies before publish."),
                    new("public-review", "Prepare public review", "Validate public-link and embed constraints before publish.")
                ]));
        }

        return questions;
    }

    public static IReadOnlyList<StudioPackageWarning> ToWarnings(
        IReadOnlyList<StudioClarificationQuestion> clarifications) =>
        clarifications.Select(question => question.Id switch
        {
            "source-binding" => new StudioPackageWarning(
                "source-ambiguous",
                "Source layer or field binding is ambiguous. Studio is waiting for structured clarification before applying data assumptions.",
                "data_bindings"),
            "publish-intent" => new StudioPackageWarning(
                "publish-intent-ambiguous",
                "Publication intent is unresolved. Save Version and Publish stay blocked until the builder chooses an intent.",
                "publication_intent"),
            _ => new StudioPackageWarning(
                $"{question.Id}-ambiguous",
                $"{question.Label} is unresolved. Studio is waiting for structured clarification before applying assumptions.",
                "clarifications")
        }).ToArray();

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.Ordinal));
}
