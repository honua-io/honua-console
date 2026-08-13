using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Render regression for the Studio "Form from prompt" (<see cref="StudioFormAiPage"/>) and "Report from
/// prompt" (<see cref="StudioReportAiPage"/>) conversational entries. Verifies the honest AI-unavailable
/// state (server has the API but no generation provider), the shared missing-binding surface (charter §11),
/// and that neither fabricates a draft. The generated/clarification paths reuse the same StudioAiConversation
/// primitive proven by StudioWorkflowAiPageTests.
/// </summary>
public sealed class StudioFormReportAiPageTests
{
    [Fact]
    public void FormAi_WhenGenerationOff_RendersHonestUnavailableState_AndDisablesChat()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(new FakeFormData());

        var page = ctx.Render<StudioFormAiPage>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-form-ai-unavailable]")),
            TimeSpan.FromSeconds(5));
        Assert.True(page.Find(".studio-ai-refine-input").HasAttribute("disabled"));
        Assert.Empty(page.FindAll("[data-form-ai-provider]"));
    }

    [Fact]
    public void FormAi_WhenUnbound_RendersSharedMissingBindingSurface()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(new FakeFormData { Unbound = true });

        var page = ctx.Render<StudioFormAiPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Form authoring is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Empty(page.FindAll("section[aria-label='Form readiness summary']"));
    }

    [Fact]
    public void ReportAi_WhenGenerationOff_RendersHonestUnavailableState_AndDisablesChat()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IStudioReportPublicationDataSource>(new FakeReportData());

        var page = ctx.Render<StudioReportAiPage>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-report-ai-unavailable]")),
            TimeSpan.FromSeconds(5));
        Assert.True(page.Find(".studio-ai-refine-input").HasAttribute("disabled"));
    }

    [Fact]
    public void ReportAi_WhenUnbound_RendersSharedMissingBindingSurface()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IStudioReportPublicationDataSource>(new FakeReportData { Unbound = true });

        var page = ctx.Render<StudioReportAiPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Report authoring is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Empty(page.FindAll("section[aria-label='Report readiness summary']"));
    }

    private sealed class FakeFormData : IStudioFormPackageDataSource
    {
        private static readonly StudioFormCapabilityState Missing =
            new("Form builder", "Missing binding", "Honua:Server:BaseUrl", "Configure Honua:Server:BaseUrl.");

        public bool Unbound { get; init; }

        public Task<StudioFormAiCapability> GetGenerationCapabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Unbound ? StudioFormAiCapability.Blocked(Missing) : StudioFormAiCapability.Off);

        public Task<StudioFormEditorLoad> LoadAsync(string? formId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Unbound
                ? new StudioFormEditorLoad(null, [Missing])
                : new StudioFormEditorLoad(new StudioFormEditorState(), []));

        public Task<StudioFormGenerationOutcome> GenerateAsync(
            StudioFormEditorState currentState, StudioFormGenerationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormGenerationOutcome { Status = StudioFormGenerationStatuses.Unsupported });

        public Task<StudioFormImportOutcome> ImportXlsFormAsync(StudioFormImportRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Unbound
                ? StudioFormImportOutcome.Blocked(Missing)
                : StudioFormImportOutcome.Failed(StudioFormImportStatuses.Unsupported, "Not exercised by this fake."));

        public Task<StudioFormWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormWorkspace([], []));

        public Task<StudioFormCommandResult> SaveDraftAsync(StudioFormEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, string.Empty));

        public Task<StudioFormCommandResult> ValidateAsync(StudioFormEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, string.Empty));

        public Task<StudioFormCommandResult> PublishAsync(StudioFormEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, string.Empty));

        public Task<StudioFormCommandResult> ReopenAsync(string formId, int version, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, string.Empty));

        public Task<StudioFormOfflinePolicyView> GetOfflinePolicyAsync(string formId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormOfflinePolicyView(false, []));
    }

    private sealed class FakeReportData : IStudioReportPublicationDataSource
    {
        private static readonly StudioReportCapabilityState Missing =
            new("Report builder", "Missing binding", "Honua:Server:BaseUrl", "Configure Honua:Server:BaseUrl.");

        public bool Unbound { get; init; }

        public Task<StudioReportAiCapability> GetGenerationCapabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Unbound ? StudioReportAiCapability.Blocked(Missing) : StudioReportAiCapability.Off);

        public Task<StudioReportGenerationOutcome> GenerateAsync(
            StudioReportEditorState currentState, StudioReportGenerationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioReportGenerationOutcome { Status = StudioReportGenerationStatuses.Unsupported });

        public Task<StudioReportPublicationLoad> LoadAsync(string publicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioReportPublicationLoad(null, [Missing]));

        public Task<StudioReportCommandResult> PublishAsync(StudioReportEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioReportCommandResult(true, string.Empty));

        public Task<StudioReportCommandResult> RollbackAsync(string publicationId, string targetVersionId, string? expectedEtag = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioReportCommandResult(true, string.Empty));

        public Task<StudioReportCommandResult> UpdatePolicyAsync(string publicationId, string visibility, bool embeddable, string? expectedEtag = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioReportCommandResult(true, string.Empty));
    }
}
