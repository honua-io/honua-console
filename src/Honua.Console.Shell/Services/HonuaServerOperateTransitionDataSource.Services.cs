using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed partial class HonuaServerOperateTransitionDataSource
{
    private sealed class ServiceProjection(
        IReadOnlyDictionary<string, HonuaAdminServiceSettingsResponse> serviceSettings,
        IReadOnlyList<LayerRow> layerRows, OperateServiceEndpointResolver publications)
    {
        public IReadOnlyList<OperateServiceDetail> Map(IReadOnlyList<Honua.Sdk.Admin.Models.ServiceSummary> summaries)
        {
            // Registry responses may omit services that still own published layers.
            return CombineServiceSummaries(summaries)
                .Select(service => MapService(service))
                .ToArray();
        }

        private IReadOnlyList<Honua.Sdk.Admin.Models.ServiceSummary> CombineServiceSummaries(
            IReadOnlyList<Honua.Sdk.Admin.Models.ServiceSummary> summaries)
        {
            var knownServiceNames = summaries
                .Select(summary => summary.ServiceName?.Trim())
                .Where(serviceName => !string.IsNullOrWhiteSpace(serviceName))
                .Select(serviceName => serviceName!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var derivedServices = layerRows
                .GroupBy(row => Coalesce(row.Source.ServiceName, "default"), StringComparer.OrdinalIgnoreCase)
                .Where(group => !knownServiceNames.Contains(group.Key))
                .Select(group => new Honua.Sdk.Admin.Models.ServiceSummary
                {
                    ServiceName = group.Key,
                    LayerCount = group.Count(),
                    EnabledProtocols = []
                });

            return summaries
                .Concat(derivedServices)
                .OrderBy(summary => summary.ServiceName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private OperateServiceDetail MapService(Honua.Sdk.Admin.Models.ServiceSummary service)
        {
            var serviceName = service.ServiceName!;
            serviceSettings.TryGetValue(serviceName, out var settings);
            var protocols = ((settings?.EnabledProtocols ?? []).Count > 0 ? settings!.EnabledProtocols : service.EnabledProtocols ?? [])
                .Where(protocol => !string.IsNullOrWhiteSpace(protocol))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var layers = layerRows
                .Where(row => string.Equals(
                    Coalesce(row.Source.ServiceName, "default"),
                    serviceName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(row => row.Projection)
                .OrderBy(layer => layer.LayerId)
                .ToArray();

            var detail = new OperateServiceDetail(
                serviceName,
                Coalesce(service.Title, service.PublicName, service.Description, serviceName),
                protocols.Length == 0 ? "Geo service" : string.Join(", ", protocols),
                service.LayerCount > 0 ? "Configured" : "No layers",
                "Canonical metadata is owned by honua-server Metadata v2 resources. Service settings control runtime protocols, access policy, and exposure.",
                layers,
                MapRuntimeSettings(service, settings, protocols),
                [])
            {
                FolderId = service.FolderId,
                FolderName = service.FolderName,
                PublicName = service.PublicName,
                FolderMutationIssues = service.FolderMutationIssues
            };
            return detail with { Endpoints = publications.ForService(detail.PublicServiceName, protocols, detail.FolderName) };
        }

        private IReadOnlyList<OperateRuntimeSetting> MapRuntimeSettings(
            Honua.Sdk.Admin.Models.ServiceSummary service,
            HonuaAdminServiceSettingsResponse? settings,
            IReadOnlyList<string> protocols)
        {
            var runtimeSettings = new List<OperateRuntimeSetting>
            {
                new(
                    "enabledProtocols",
                    protocols.Count == 0 ? "None reported" : string.Join(", ", protocols),
                    "Service runtime protocol exposure",
                    RequiresRestart: false),
                new(
                    "layerCount",
                    service.LayerCount.ToString(CultureInfo.InvariantCulture),
                    "Published service layer",
                    RequiresRestart: false)
            };

            if (settings?.AccessPolicy is { } accessPolicy)
            {
                runtimeSettings.Add(new OperateRuntimeSetting(
                    "accessPolicy",
                    FormatAccessPolicy(accessPolicy),
                    "Service access policy",
                    RequiresRestart: false));
            }

            if (settings?.MapServer is { } mapServer)
            {
                runtimeSettings.Add(new OperateRuntimeSetting(
                    "mapServer.defaultFormat",
                    Coalesce(mapServer.DefaultFormat, "Not reported"),
                    "MapServer runtime defaults",
                    RequiresRestart: false));
                runtimeSettings.Add(new OperateRuntimeSetting(
                    "mapServer.maxImageSize",
                    $"{mapServer.MaxImageWidth?.ToString(CultureInfo.InvariantCulture) ?? "?"} x {mapServer.MaxImageHeight?.ToString(CultureInfo.InvariantCulture) ?? "?"}",
                    "MapServer runtime defaults",
                    RequiresRestart: false));
            }

            return runtimeSettings;
        }
    }
}
