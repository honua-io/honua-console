namespace Honua.Console.Shell.Services;

public enum OmniPromptIntent
{
    Studio,

    DevOps,
}

public enum OmniPromptConfidence
{
    High,

    Low,
}

public readonly record struct OmniPromptClassification(
    OmniPromptIntent Intent,
    OmniPromptConfidence Confidence,
    string Rationale);

// Classification only chooses an approval surface and does not execute an operation.
public interface IOmniPromptIntentClassifier
{
    OmniPromptClassification Classify(string prompt);
}

public sealed class OmniPromptIntentClassifier : IOmniPromptIntentClassifier
{
    // DevOps / operations vocabulary: deploy, roll back, upgrade, promote a release, etc. These are the
    // operational verbs/nouns that should land in the deploy approval lane rather than the Studio publish lane.
    private static readonly string[] DevOpsMarkers =
    [
        "roll back", "rollback", "roll-back",
        "deploy", "redeploy", "deployment",
        "upgrade", "downgrade",
        "release the", "promote", "promotion",
        "production server", "prod server", "the prod ", "to prod", "in prod",
        "staging server", "to staging", "in staging",
        "revision", "last good", "previous revision",
        "restart", "scale up", "scale down", "scale the",
        "cut over", "cutover", "failover", "fail over",
        "hotfix", "patch the server", "server version", "platform upgrade",
        "gitops", "reconcile",
    ];

    // Studio / GIS authoring vocabulary: publish a resource, build a map/dashboard/report, run analysis, style a
    // layer, an ETL pipeline. These are the authoring verbs/nouns that should land in the Studio generate lane.
    private static readonly string[] StudioMarkers =
    [
        "publish", "feature service", "featureserver", "feature server",
        "map service", "mapserver", "map server", "imageserver",
        "stac", "ogc", "wms", "wfs", "wmts", "odata",
        "parcels", "layer", "dataset", "geojson", "shapefile", "geopackage",
        "dashboard", "report", "map", "basemap",
        "analysis", "analyze", "analyse", "buffer", "spatial query",
        "style", "styled by", "color by", "symbolize", "symbolise",
        "etl", "pipeline", "workflow", "ingest", "import the", "load the",
    ];

    public OmniPromptClassification Classify(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new OmniPromptClassification(
                OmniPromptIntent.Studio,
                OmniPromptConfidence.Low,
                "Describe what you want — publishing a layer, or a deploy/rollback/upgrade.");
        }

        var text = prompt.ToLowerInvariant();
        var devOpsScore = CountMarkers(text, DevOpsMarkers);
        var studioScore = CountMarkers(text, StudioMarkers);

        // No recognised signal on either side → ambiguous; let the human confirm the lane.
        if (devOpsScore == 0 && studioScore == 0)
        {
            return new OmniPromptClassification(
                OmniPromptIntent.Studio,
                OmniPromptConfidence.Low,
                "I couldn't tell whether this is a publishing request or an ops action — pick a lane.");
        }

        // A clear winner routes directly; a tie (both lanes fired) is genuinely ambiguous → confirm chip.
        if (devOpsScore > studioScore)
        {
            return new OmniPromptClassification(
                OmniPromptIntent.DevOps,
                OmniPromptConfidence.High,
                "This reads as an ops action (deploy / rollback / upgrade) — routing to deploy approvals.");
        }

        if (studioScore > devOpsScore)
        {
            return new OmniPromptClassification(
                OmniPromptIntent.Studio,
                OmniPromptConfidence.High,
                "This reads as a GIS authoring request — routing to the Studio publish flow.");
        }

        return new OmniPromptClassification(
            OmniPromptIntent.Studio,
            OmniPromptConfidence.Low,
            "This could be either a publishing request or an ops action — pick a lane.");
    }

    private static int CountMarkers(string text, IReadOnlyList<string> markers)
    {
        var hits = 0;
        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.Ordinal))
            {
                hits++;
            }
        }

        return hits;
    }
}
