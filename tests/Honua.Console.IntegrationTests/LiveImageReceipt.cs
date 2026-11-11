using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

// A known-valid live call that comes back unavailable is a failed receipt. A client
// rejection (validation, permission, conflict) is the caller's assertion, not an image skip.
internal static class LiveImageReceipt
{
    internal static bool LayerPublishUnavailable(string? state, string? detail) =>
        string.Equals(state, "Unavailable", StringComparison.OrdinalIgnoreCase)
        || (detail?.Contains("HTTP 5", StringComparison.OrdinalIgnoreCase) ?? false);

    internal static bool ContentIssueIsImageFailure(int? statusCode, string? state) =>
        statusCode is >= 500
        || string.Equals(state, "Unavailable", StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, "Unsupported", StringComparison.OrdinalIgnoreCase);

    internal static void RequireLayerPublish(ServiceLayerPublishResult result, string roundTrip)
    {
        if (result.Succeeded)
        {
            return;
        }

        Assert.False(
            LayerPublishUnavailable(result.State, result.Detail),
            $"The pinned honua-server image could not service the layer-publish path ({result.State} — {result.Detail}); "
            + $"the {roundTrip} needs a server build whose admin layer-publishing path is ready.");
    }
}
