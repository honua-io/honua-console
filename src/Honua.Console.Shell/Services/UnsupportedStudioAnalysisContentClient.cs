using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Fallback <see cref="IStudioAnalysisContentClient"/> used when no absolute honua-server base address is
/// configured. Every call returns a missing-binding issue so the Studio analysis builder renders the shared
/// missing-binding surface (Console Patterns Charter §5/§11) instead of a standing in-memory analysis client.
/// There is no mock analysis data in the merged runtime — server-owned analysis content binds only to a real
/// honua-server through <see cref="HttpStudioAnalysisContentClient"/>.
/// </summary>
public sealed class UnsupportedStudioAnalysisContentClient : IStudioAnalysisContentClient
{
    private const string Contract = "honua-server Analysis Content API (/api/v1/analysis)";

    private static AnalysisEndpointIssue MissingBinding => new(
        "Missing binding",
        Contract,
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the Studio analysis builder can bind "
        + "the server-owned analysis content lifecycle from honua-server (#1182).");

    public Uri BaseUri { get; } = new("about:blank");

    public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> CreateItemAsync(
        CreateAnalysisContentItemRequest request,
        CancellationToken cancellationToken = default) => Blocked<AnalysisContentSnapshot>();

    public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> GetItemAsync(
        string itemId,
        CancellationToken cancellationToken = default) => Blocked<AnalysisContentSnapshot>();

    public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> GetLatestVersionAsync(
        string itemId,
        CancellationToken cancellationToken = default) => Blocked<AnalysisContentSnapshot>();

    public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> GetVersionAsync(
        string itemId,
        int contentVersion,
        CancellationToken cancellationToken = default) => Blocked<AnalysisContentSnapshot>();

    public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> CreateVersionAsync(
        string itemId,
        CreateAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default) => Blocked<AnalysisContentSnapshot>();

    public Task<AnalysisEndpointResult<SavedQueryPreviewResult>> PreviewSavedQueryAsync(
        string itemId,
        int contentVersion,
        PreviewSavedQueryRequest request,
        CancellationToken cancellationToken = default) => Blocked<SavedQueryPreviewResult>();

    public Task<AnalysisEndpointResult<AnalysisContentJobResponse>> SubmitRunAsync(
        string itemId,
        int contentVersion,
        RunAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default) => Blocked<AnalysisContentJobResponse>();

    public Task<AnalysisEndpointResult<AnalysisContentJobResponse>> SubmitRerunAsync(
        string itemId,
        int contentVersion,
        RerunAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default) => Blocked<AnalysisContentJobResponse>();

    public Task<AnalysisEndpointResult<AnalysisArtifactResolution>> GetArtifactAsync(
        string artifactId,
        CancellationToken cancellationToken = default) => Blocked<AnalysisArtifactResolution>();

    public Task<AnalysisEndpointResult<AnalysisJobLogs>> GetJobLogsAsync(
        string jobId,
        int? limit = null,
        CancellationToken cancellationToken = default) => Blocked<AnalysisJobLogs>();

    public Task<AnalysisEndpointResult<AnalysisJobFailure>> GetJobFailureAsync(
        string jobId,
        CancellationToken cancellationToken = default) => Blocked<AnalysisJobFailure>();

    private static Task<AnalysisEndpointResult<T>> Blocked<T>() =>
        Task.FromResult(AnalysisEndpointResult<T>.FromIssue(MissingBinding));
}
