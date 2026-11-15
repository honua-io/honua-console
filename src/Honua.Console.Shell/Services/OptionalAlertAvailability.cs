using System.Net;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

internal static class OptionalAlertAvailability
{
    internal const string Message =
        "Alerts are not enabled or available on this server. Ask an administrator to enable geofence alerts (alerts.geofence).";

    internal static bool IsAbsentCollection(HttpStatusCode status, string relativePath) =>
        (status is HttpStatusCode.NotFound or HttpStatusCode.NotImplemented) &&
        (relativePath.Split('?')[0] is OperateAdminRoutes.Alerts or OperateAdminRoutes.AlertRules or OperateAdminRoutes.AlertZones);
}
