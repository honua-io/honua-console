using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

internal static class ConsoleOptionalFeatures
{
    internal const string Scenes = "Scene discovery is not enabled or supported by this server. On a supported server, ask an administrator to set Capabilities:Experimental:scene.catalog:Enabled=true and restart the server.";
    internal const string Sensors = "SensorThings is not enabled or supported by this server. On a supported server, ask an administrator to set Capabilities:Experimental:serve.sensorthings:Enabled=true and restart the server.";
    internal const string Alerts = "Alerts are not enabled or supported by this server. On a supported server, ask an administrator to set Capabilities:Experimental:alerts.geofence:Enabled=true and restart the server.";

    internal static bool IsAlertCollection(string path) => path.Split('?')[0] is
        OperateAdminRoutes.Alerts or OperateAdminRoutes.AlertRules or OperateAdminRoutes.AlertZones;
}
