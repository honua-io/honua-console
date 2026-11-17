using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

// Exports and object-storage Connections. Reads may use the service credential; every write needs the operator.
public interface IConsoleExportsClient
{
    Task<OperateSectionResult<ExportListResponse>> ListAsync(CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ExportResponse>> GetAsync(string exportId, CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ExportResponse>> CreateAsync(ExportCreateRequest request, CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ExportResponse>> UnpublishAsync(string exportId, CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ObjectStorageSetupResponse>> GetObjectStorageSetupAsync(
        string bucket, string prefix, CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ObjectStorageTestResponse>> TestObjectStorageAsync(
        ObjectStorageConnectionRequest request, CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ObjectStorageConnectionResponse>> CreateObjectStorageAsync(
        ObjectStorageConnectionRequest request, CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ObjectStorageBrowseResponse>> BrowseAsync(
        Guid connectionId, string prefix, CancellationToken cancellationToken = default);
}
