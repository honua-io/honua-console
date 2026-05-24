using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class InMemoryOperateTransitionDataSource : IOperateTransitionDataSource
{
    private readonly OperateTransitionWorkspace _workspace;

    private InMemoryOperateTransitionDataSource(OperateTransitionWorkspace workspace)
    {
        _workspace = workspace;
    }

    public static InMemoryOperateTransitionDataSource CreateSeeded() =>
        new(CreateWorkspace());

    public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_workspace);

    public Task<OperateConnectionSummary?> FindConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_workspace.Connections.FirstOrDefault(
            connection => string.Equals(connection.Id, connectionId, StringComparison.OrdinalIgnoreCase)));

    public Task<OperateResourceEditPreview?> FindResourceEditAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_workspace.ResourceEdits.FirstOrDefault(
            resource => string.Equals(resource.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase)));

    public Task<OperateServiceDetail?> FindServiceAsync(
        string serviceName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_workspace.Services.FirstOrDefault(
            service => string.Equals(service.Name, serviceName, StringComparison.OrdinalIgnoreCase)));

    private static OperateTransitionWorkspace CreateWorkspace() =>
        new(
            Connections:
            [
                new OperateConnectionSummary(
                    "prod-postgres",
                    "Production PostGIS Warehouse",
                    "PostgreSQL/PostGIS",
                    "pg-prod.internal.honua:5432/land",
                    "gis_operator",
                    "Failed",
                    "2026-05-24 16:42 UTC",
                    new OperateConnectionDiagnostic(
                        "Failed",
                        "AUTH_INVALID_CREDENTIAL",
                        "Driver reached the database host, but authentication failed for gis_operator. connectionString=Host=pg-prod.internal.honua;Database=land;User Id=gis_operator;Password=wrong-value",
                        [
                            new OperateDiagnosticSignal(
                                "network",
                                "Passed",
                                "TCP handshake succeeded for pg-prod.internal.honua:5432."),
                            new OperateDiagnosticSignal(
                                "credentials",
                                "Failed",
                                "Server rejected password=wrong-value for user gis_operator with Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.abc.def."),
                            new OperateDiagnosticSignal(
                                "policy",
                                "Passed",
                                "Operator has test permission through operate:connections:test.")
                        ],
                        [
                            "Rotate the credential stored at secret://connections/prod-postgres/vault-material and test again.",
                            "Confirm the gis_operator role can read metadata schemas and feature tables.",
                            "If the password was rotated outside Honua, update the connection secret reference before publishing."
                        ],
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["failureCode"] = "AUTH_INVALID_CREDENTIAL",
                            ["driver"] = "Npgsql 8",
                            ["endpoint"] = "pg-prod.internal.honua:5432",
                            ["credentialReference"] = "secret://connections/prod-postgres/vault-material",
                            ["wireMessage"] = "password authentication failed for user gis_operator"
                        })),
                new OperateConnectionSummary(
                    "agol-open-data",
                    "ArcGIS Online Open Data",
                    "ArcGIS REST",
                    "https://services.arcgis.com/honua/open-data",
                    "portal-publisher",
                    "Passed",
                    "2026-05-24 15:58 UTC",
                    null),
                new OperateConnectionSummary(
                    "cityworks-api",
                    "Cityworks Work Orders",
                    "HTTP API",
                    "https://cityworks.example.gov/api",
                    "work-order-sync",
                    "Warning",
                    "2026-05-24 13:25 UTC",
                    new OperateConnectionDiagnostic(
                        "Warning",
                        "RATE_LIMIT_NEAR_QUOTA",
                        "API health check passed, but the provider reports 86 percent of the daily quota consumed.",
                        [
                            new OperateDiagnosticSignal(
                                "auth",
                                "Passed",
                                "Token exchange succeeded using the configured secret reference."),
                            new OperateDiagnosticSignal(
                                "quota",
                                "Warning",
                                "Daily quota is above the operator warning threshold.")
                        ],
                        [
                            "Review scheduled sync frequency before the next publication window.",
                            "Request a higher provider quota if production syncs must run hourly."
                        ],
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["quotaWindow"] = "daily",
                            ["quotaUsed"] = "86%",
                            ["secretReference"] = "secret://connections/cityworks-api/api-token"
                        }))
            ],
            ResourceEdits:
            [
                new OperateResourceEditPreview(
                    "res-parcels-2026",
                    "City Parcels 2026",
                    "PostGIS table public.parcels_2026",
                    "Draft changes field aliases, nullable flags, public metadata, and publish visibility.",
                    "Blocked by 1 error and 2 warnings",
                    new OperateBlastRadius(
                        CatalogItems: ["cat-city-parcels"],
                        Services: ["planning-feature-service"],
                        Layers: ["Parcels", "Parcel Labels"],
                        SavedMaps: ["Planning Review", "Public Zoning", "Permit Intake"],
                        ShareLinks: ["open-data-parcels"],
                        GeneratedApps: ["permit-review-app", "zoning-dashboard"]),
                    [
                        new OperateValidationIssue(
                            "Error",
                            "parcel_id",
                            "External key cannot change from text to integer while published services depend on it.",
                            "Keep the existing field type or create a replacement field and migrate downstream consumers."),
                        new OperateValidationIssue(
                            "Warning",
                            "owner_group",
                            "The selected owner group cannot publish to the public catalog endpoint.",
                            "Choose a group with publish permission or keep the item private."),
                        new OperateValidationIssue(
                            "Warning",
                            "geometry",
                            "The source table has 14 invalid geometries that will be skipped by the tile cache.",
                            "Repair geometries before promoting the publication slot.")
                    ],
                    [
                        "Overview",
                        "Source",
                        "Fields",
                        "Metadata",
                        "Publish",
                        "Access",
                        "Validation",
                        "Presentation",
                        "Advanced"
                    ])
            ],
            Services:
            [
                new OperateServiceDetail(
                    "planning-feature-service",
                    "Planning Feature Service",
                    "Feature service",
                    "Running",
                    "Canonical metadata is owned by data resources. Service settings only control runtime, slots, and exposure.",
                    [
                        new OperateServiceLayerProjection(
                            12,
                            "Parcels",
                            "Polygon",
                            "res-parcels-2026",
                            "City Parcels 2026"),
                        new OperateServiceLayerProjection(
                            13,
                            "Permit Applications",
                            "Point",
                            "res-permits-2026",
                            "Permit Applications 2026")
                    ],
                    [
                        new OperateRuntimeSetting(
                            "maxRecordCount",
                            "5000",
                            "Service runtime only",
                            RequiresRestart: false),
                        new OperateRuntimeSetting(
                            "cacheTtl",
                            "15 minutes",
                            "Tile and query cache",
                            RequiresRestart: false),
                        new OperateRuntimeSetting(
                            "featureSubscriptions",
                            "Enabled",
                            "Realtime service worker",
                            RequiresRestart: true)
                    ],
                    [
                        new OperatePublicationSlot(
                            "production",
                            "https://server.honua.example/services/planning",
                            "Serving revision 42"),
                        new OperatePublicationSlot(
                            "staging",
                            "https://server.honua.example/services/planning-staging",
                            "Ready for promotion")
                    ])
            ],
            SettingsChanges:
            [
                new OperateSettingsChange(
                    "identity-provider",
                    "Identity",
                    "OIDC provider metadata",
                    "Replace issuer metadata URL for the production tenant.",
                    "Identity policy and sign-in discovery",
                    RequiresRestart: false,
                    "No restart; active sessions keep their current claims until refresh.",
                    "Requires operate:identity:write."),
                new OperateSettingsChange(
                    "api-keys",
                    "Identity",
                    "API key rotation",
                    "Issue a new key for catalog automation and disable the previous key after overlap.",
                    "New key only; existing keys continue until revoked",
                    RequiresRestart: false,
                    "No restart.",
                    "Secret value is only shown once by the server, never stored in Console state."),
                new OperateSettingsChange(
                    "cors",
                    "Runtime",
                    "CORS allowed origins",
                    "Add https://planning.example.gov for public embed previews.",
                    "HTTP pipeline for browser requests",
                    RequiresRestart: true,
                    "Web host restart required before browsers see the new policy.",
                    "Requires operate:runtime:write."),
                new OperateSettingsChange(
                    "license",
                    "License",
                    "License entitlement file",
                    "Upload renewed Pro entitlement for the staging tenant.",
                    "License cache and edition gates",
                    RequiresRestart: false,
                    "No restart; cache refresh applies the entitlement.",
                    "Requires operate:license:write."),
                new OperateSettingsChange(
                    "catalog-endpoint",
                    "Catalog",
                    "Catalog endpoint visibility",
                    "Enable the public Open Data endpoint for eligible published resources.",
                    "Catalog endpoint policy and generated landing pages",
                    RequiresRestart: false,
                    "No restart; published items re-evaluate on next catalog refresh.",
                    "Requires catalog:admin.")
            ]);
}
