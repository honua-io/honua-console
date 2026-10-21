namespace Honua.Console.Contracts;

/// <summary>
/// Admin and Studio routes the pinned honua-server does not register (honua-console#420).
/// Clients return <see cref="State"/> and do not send HTTP, including when a server base URL is configured.
/// Registered routes (fields, relationships, filter, service settings, app and map generation) stay on the wire.
/// Remove a contract from <see cref="All"/> when honua-server registers that route.
/// </summary>
public static class UnregisteredServerRoutes
{
    public const string State = "Unsupported";

    public const int StatusCode = 404;

    public const string Detail = "The connected honua-server does not register this route.";

    private static readonly HashSet<string> Contracts = new(StringComparer.Ordinal)
    {
        "GET /api/v1/admin/services/{serviceName}/settings-caps",
        "PUT /api/v1/admin/services/{serviceName}/settings-caps",
        "GET /api/v1/admin/metadata/layers/{layerId}/extrusion",
        "PUT /api/v1/admin/metadata/layers/{layerId}/extrusion",
        "GET /api/v1/admin/metadata/layers/{layerId}/status",
        "PUT /api/v1/admin/metadata/layers/{layerId}/status",
        "GET /api/v1/admin/metadata/publications/{publicationId}/overrides",
        "PUT /api/v1/admin/metadata/publications/{publicationId}/overrides",
        "GET /api/v1/admin/metadata/layers/{layerId}/subtypes",
        "PUT /api/v1/admin/metadata/layers/{layerId}/subtypes",
        "GET /api/v1/admin/metadata/layers/{layerId}/attribute-rules",
        "PUT /api/v1/admin/metadata/layers/{layerId}/attribute-rules",
        "GET /api/v1/admin/metadata/layers/{layerId}/discovery",
        "PUT /api/v1/admin/metadata/layers/{layerId}/discovery",
        "GET /api/v1/admin/services/{serviceName}/discovery",
        "PUT /api/v1/admin/services/{serviceName}/discovery",
        "GET /api/v1/admin/metadata/layers/{layerId}/display",
        "PUT /api/v1/admin/metadata/layers/{layerId}/display",
        "GET /api/v1/admin/metadata/layers/{layerId}/editing",
        "PUT /api/v1/admin/metadata/layers/{layerId}/editing",
        "GET /api/v1/admin/metadata/layers/{layerId}/spatial",
        "PUT /api/v1/admin/metadata/layers/{layerId}/spatial",
        "GET /api/v1/admin/forms/packages/generation/providers",
        "POST /api/v1/admin/forms/packages/generate",
        "POST /api/v1/admin/forms/packages/import-xlsform",
        "POST /api/v1/analysis/content/generate",
        "POST /api/v1/analysis/content/queries/generate",
        "GET /api/v1/console/publications/generation/providers",
        "POST /api/v1/console/publications/generate",
        "GET /api/v1/console/workflow-generation/providers",
        "POST /api/v1/console/workflow-packages/generate",
    };

    public static IReadOnlySet<string> All => Contracts;

    public static bool Contains(string contract) => Contracts.Contains(contract);

    public static HonuaAdminEndpointResult<T> Admin<T>(string contract) =>
        HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(State, contract, Detail, StatusCode));

    public static WorkflowEndpointResult<T> Workflow<T>(string contract) =>
        WorkflowEndpointResult<T>.FromIssue(new WorkflowEndpointIssue(State, contract, Detail, StatusCode));
}
