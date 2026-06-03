using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Report-builder data source used when no honua-server base address is configured. It renders an
/// explicit missing-binding state instead of fabricating publication data, keeping the merged runtime
/// free of a standing in-memory report publication client (Console Patterns Charter section 11). The
/// report authoring surface stays bound to the real honua-server content publication registry
/// (honua-server#1183) or shows this state.
/// </summary>
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

    public Task<StudioReportAiCapability> GetGenerationCapabilityAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StudioReportAiCapability.Blocked(MissingBinding));

    public Task<StudioReportGenerationOutcome> GenerateAsync(
        StudioReportEditorState currentState,
        StudioReportGenerationRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StudioReportGenerationOutcome.Blocked(MissingBinding));

    private static StudioReportCommandResult MissingBindingCommand() =>
        new(false, MissingBinding.Detail, Issue: MissingBinding);
}
