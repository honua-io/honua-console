namespace Honua.Console.Shell.Services;

/// <summary>
/// The top-level intent the omni-prompt AI console routes a free-text prompt to (honua-console#203).
/// One prompt box handles BOTH lanes; the classifier decides which approval surface it lands in.
/// </summary>
public enum OmniPromptIntent
{
    /// <summary>GIS authoring: publish / map / dashboard / analysis / ETL / style → the Studio generate path,
    /// whose <em>sub-type</em> is inferred server-side. Lands in the #200 AI publish outcome/approval card.</summary>
    Studio,

    /// <summary>Operations: deploy / roll back / upgrade → the deploy-control path. Lands in the deploy approval
    /// panel (#197) so the operator reviews and actuates through the server's governed approval gate.</summary>
    DevOps,
}

/// <summary>
/// The confidence the classifier assigns to its <see cref="OmniPromptIntent"/> choice. Low confidence (an empty
/// or genuinely ambiguous prompt) drives a confirm chip rather than silently routing into the wrong lane
/// (honua-console#203: "fall back to a confirm chip if low-confidence").
/// </summary>
public enum OmniPromptConfidence
{
    /// <summary>Strong lane signal — route directly to the chosen lane's surface.</summary>
    High,

    /// <summary>Weak / no lane signal — surface a confirm chip and let the human pick the lane.</summary>
    Low,
}

/// <summary>The classifier's verdict: the routed lane, the confidence, and a short human rationale for the chip.</summary>
public readonly record struct OmniPromptClassification(
    OmniPromptIntent Intent,
    OmniPromptConfidence Confidence,
    string Rationale);

/// <summary>
/// Routes an omni-prompt's free text to a top-level lane — Studio (GIS authoring) or DevOps (ops) — for the
/// unified AI console (honua-console#203). This is the thin, console-side v1 classifier the issue calls for:
/// honua-server exposes no intent-classify / operations-catalog endpoint today, so a deterministic keyword
/// heuristic decides the lane and surfaces a confirm chip when the signal is weak. It NEVER actuates anything —
/// it only chooses which existing approval surface the prompt is handed to. Kept pure so the same words always
/// resolve to the same lane and the routing is unit-testable without a server.
/// </summary>
public interface IOmniPromptIntentClassifier
{
    OmniPromptClassification Classify(string prompt);
}

/// <inheritdoc />
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
