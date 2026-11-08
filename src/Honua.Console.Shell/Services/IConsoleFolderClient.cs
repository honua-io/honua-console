using Honua.Console.Shell.Models;
using Honua.Sdk.Admin.Models;

namespace Honua.Console.Shell.Services;

public sealed record ConsoleFolderCatalog(IReadOnlyList<Folder> Folders, string? ETag, string AuthorityBinding);

public sealed record ConsoleFolderMutationResult : ConsoleOperationResult<ConsoleFolderMutationResult>;

public interface IConsoleFolderClient
{
    Task<OperateSectionResult<ConsoleFolderCatalog>> ReadAsync(CancellationToken cancellationToken = default);
    Task<ConsoleFolderMutationResult> CreateAsync(ConsoleFolderCatalog catalog, CreateFolderRequest request, CancellationToken cancellationToken = default);
    Task<ConsoleFolderMutationResult> UpdateAsync(ConsoleFolderCatalog catalog, string folderId, UpdateFolderRequest request, CancellationToken cancellationToken = default);
    Task<ConsoleFolderMutationResult> DeleteAsync(ConsoleFolderCatalog catalog, string folderId, CancellationToken cancellationToken = default);
    Task<ConsoleFolderMutationResult> MoveAsync(ConsoleFolderCatalog catalog, string serviceName, MoveServiceRequest request, CancellationToken cancellationToken = default);
}

public sealed class UnsupportedConsoleFolderClient : IConsoleFolderClient
{
    private const string Missing = "Connect an environment to manage Folders.";
    public Task<OperateSectionResult<ConsoleFolderCatalog>> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<ConsoleFolderCatalog>.Denied(OperateSectionStatus.Missing, Missing));
    public Task<ConsoleFolderMutationResult> CreateAsync(ConsoleFolderCatalog catalog, CreateFolderRequest request, CancellationToken cancellationToken = default) => Refuse();
    public Task<ConsoleFolderMutationResult> UpdateAsync(ConsoleFolderCatalog catalog, string folderId, UpdateFolderRequest request, CancellationToken cancellationToken = default) => Refuse();
    public Task<ConsoleFolderMutationResult> DeleteAsync(ConsoleFolderCatalog catalog, string folderId, CancellationToken cancellationToken = default) => Refuse();
    public Task<ConsoleFolderMutationResult> MoveAsync(ConsoleFolderCatalog catalog, string serviceName, MoveServiceRequest request, CancellationToken cancellationToken = default) => Refuse();
    private static Task<ConsoleFolderMutationResult> Refuse() => Task.FromResult(ConsoleFolderMutationResult.MissingBinding(Missing));
}
