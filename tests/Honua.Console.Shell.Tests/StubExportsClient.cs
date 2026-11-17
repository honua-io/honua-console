using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

internal sealed class StubExportsClient : IConsoleExportsClient
{
    private static OperateSectionResult<T> NotScripted<T>() =>
        OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, "Not scripted.");

    public OperateSectionResult<ExportListResponse> List { get; set; } = NotScripted<ExportListResponse>();
    public OperateSectionResult<ExportResponse> Detail { get; set; } = NotScripted<ExportResponse>();
    public OperateSectionResult<ExportResponse> Create { get; set; } = NotScripted<ExportResponse>();
    public OperateSectionResult<ExportResponse> Unpublish { get; set; } = NotScripted<ExportResponse>();
    public OperateSectionResult<ObjectStorageSetupResponse> Setup { get; set; } = NotScripted<ObjectStorageSetupResponse>();
    public OperateSectionResult<ObjectStorageTestResponse> Test { get; set; } = NotScripted<ObjectStorageTestResponse>();
    public OperateSectionResult<ObjectStorageConnectionResponse> Connection { get; set; } = NotScripted<ObjectStorageConnectionResponse>();
    public OperateSectionResult<ObjectStorageBrowseResponse> Listing { get; set; } = NotScripted<ObjectStorageBrowseResponse>();

    public int ListCalls { get; private set; }
    public List<string> GetCalls { get; } = [];
    public List<ExportCreateRequest> CreateCalls { get; } = [];
    public List<string> UnpublishCalls { get; } = [];
    public List<(string Bucket, string Prefix)> SetupCalls { get; } = [];
    public List<ObjectStorageConnectionRequest> TestCalls { get; } = [];
    public List<ObjectStorageConnectionRequest> ConnectionCalls { get; } = [];
    public List<(Guid ConnectionId, string Prefix)> BrowseCalls { get; } = [];

    public Task<OperateSectionResult<ExportListResponse>> ListAsync(CancellationToken cancellationToken = default)
    {
        ListCalls++;
        return Task.FromResult(List);
    }

    public Task<OperateSectionResult<ExportResponse>> GetAsync(string exportId, CancellationToken cancellationToken = default)
    {
        GetCalls.Add(exportId);
        return Task.FromResult(Detail);
    }

    public Task<OperateSectionResult<ExportResponse>> CreateAsync(ExportCreateRequest request, CancellationToken cancellationToken = default)
    {
        CreateCalls.Add(request);
        return Task.FromResult(Create);
    }

    public Task<OperateSectionResult<ExportResponse>> UnpublishAsync(string exportId, CancellationToken cancellationToken = default)
    {
        UnpublishCalls.Add(exportId);
        return Task.FromResult(Unpublish);
    }

    public Task<OperateSectionResult<ObjectStorageSetupResponse>> GetObjectStorageSetupAsync(
        string bucket, string prefix, CancellationToken cancellationToken = default)
    {
        SetupCalls.Add((bucket, prefix));
        return Task.FromResult(Setup);
    }

    public Task<OperateSectionResult<ObjectStorageTestResponse>> TestObjectStorageAsync(
        ObjectStorageConnectionRequest request, CancellationToken cancellationToken = default)
    {
        TestCalls.Add(request);
        return Task.FromResult(Test);
    }

    public Task<OperateSectionResult<ObjectStorageConnectionResponse>> CreateObjectStorageAsync(
        ObjectStorageConnectionRequest request, CancellationToken cancellationToken = default)
    {
        ConnectionCalls.Add(request);
        return Task.FromResult(Connection);
    }

    public Task<OperateSectionResult<ObjectStorageBrowseResponse>> BrowseAsync(
        Guid connectionId, string prefix, CancellationToken cancellationToken = default)
    {
        BrowseCalls.Add((connectionId, prefix));
        return Task.FromResult(Listing);
    }
}
