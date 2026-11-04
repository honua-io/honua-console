using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedStudioReportPublicationDataSource : IStudioReportPublicationDataSource
{
    private static readonly StudioReportCapabilityState MissingBinding = new(
        "Report builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the report builder can bind the server-owned content publication registry from honua-server.");

    public Task<StudioReportPublicationLoad> LoadAsync(
        string publicationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioReportPublicationLoad(null, [MissingBinding]));

    public Task<StudioReportCommandResult> PublishAsync(
        StudioReportEditorState state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(MissingBindingCommand());

    public Task<StudioReportCommandResult> RollbackAsync(
        string publicationId,
        string targetVersionId,
        string? expectedEtag = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(MissingBindingCommand());

    public Task<StudioReportCommandResult> UpdatePolicyAsync(
        string publicationId,
        string visibility,
        bool embeddable,
        string? expectedEtag = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(MissingBindingCommand());

    private static StudioReportCommandResult MissingBindingCommand() =>
        new(false, MissingBinding.Detail, Issue: MissingBinding);
}
