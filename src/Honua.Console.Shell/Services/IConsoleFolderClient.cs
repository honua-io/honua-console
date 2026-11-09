using Honua.Console.Shell.Models;
using Honua.Sdk.Admin.Models;

namespace Honua.Console.Shell.Services;

public sealed record ConsoleFolderCatalog(IReadOnlyList<Folder> Folders, string? ETag, string AuthorityBinding)
{
    public IEnumerable<Folder> ActiveFolders => Active(Folders);
    public static IEnumerable<Folder> Active(IEnumerable<Folder>? folders) =>
        (folders ?? []).Where(folder => !folder.Deleted && !string.IsNullOrWhiteSpace(folder.Metadata.Id));
    public static string LocationLabel(string? folderId, string? folderName) => folderId is null
        ? "Root" : string.IsNullOrWhiteSpace(folderName) ? "Folder unavailable" : folderName;
    public static string Label(Folder folder) => string.IsNullOrWhiteSpace(folder.Metadata.Title)
        ? folder.Metadata.Name : $"{folder.Metadata.Title} ({folder.Metadata.Name})";
}

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
