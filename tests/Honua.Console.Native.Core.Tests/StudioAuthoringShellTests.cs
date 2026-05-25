using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Unit tests for the local <see cref="InMemoryStudioAuthoringShell"/> demo simulator. The simulator is
/// permitted only in tests/explicit demo composition; server-owned Studio package data binds to
/// honua-server through <c>ServerStudioAuthoringShell</c> at runtime.
/// </summary>
public sealed class StudioAuthoringShellTests
{
    [Fact]
    public async Task AmbiguousPromptProducesInspectablePackageWithClarification()
    {
        IStudioAuthoringShell shell = new InMemoryStudioAuthoringShell();
        var session = await shell.CreateInitialSessionAsync();

        var result = await shell.GeneratePackageAsync(session, "map", "Make a map");

        Assert.NotNull(result.ActivePackage);
        Assert.Equal(StudioAuthoringContract.Name, result.ActivePackage.ContractName);
        Assert.Equal(StudioAuthoringContract.Version, result.ActivePackage.ContractVersion);
        Assert.Equal("map.package", result.ActivePackage.PackageType);
        Assert.NotNull(result.Draft);
        Assert.NotEmpty(result.Clarifications);
        Assert.Contains(result.Clarifications, question => question.Id == "source-binding");
        Assert.Contains(result.ActivePackage.ValidationItems, item => item.Severity == StudioValidationSeverity.Blocker);
        Assert.Contains(result.ActivePackage.Provenance, item => item.Action == "Clarification requested");
    }

    [Fact]
    public async Task ClarificationUpdatesPackageWithoutHidingInspectorData()
    {
        IStudioAuthoringShell shell = new InMemoryStudioAuthoringShell();
        var session = await shell.GeneratePackageAsync(await shell.CreateInitialSessionAsync(), "map", "Make a map");
        var sourceQuestion = session.Clarifications.First(question => question.Id == "source-binding");

        var clarified = await shell.ApplyClarificationAsync(session, sourceQuestion.Id, sourceQuestion.Choices[0].Id);

        Assert.DoesNotContain(clarified.Clarifications, question => question.Id == "source-binding");
        Assert.Contains(clarified.ActivePackage.DataBindings, binding => binding.Status == "Bound after clarification");
        Assert.NotEmpty(clarified.ActivePackage.Assumptions);
        Assert.NotEmpty(clarified.ActivePackage.ValidationItems);
        Assert.NotEmpty(clarified.ActivePackage.Provenance);
    }

    [Fact]
    public async Task PartialClarificationKeepsPendingAssumptionsForRemainingQuestions()
    {
        IStudioAuthoringShell shell = new InMemoryStudioAuthoringShell();
        var session = await shell.GeneratePackageAsync(await shell.CreateInitialSessionAsync(), "app", "Build an app");
        var sourceQuestion = session.Clarifications.First(question => question.Id == "source-binding");

        var clarified = await shell.ApplyClarificationAsync(session, sourceQuestion.Id, sourceQuestion.Choices[0].Id);

        Assert.DoesNotContain(clarified.ActivePackage.Assumptions, assumption => assumption == "Pending: Select the source binding");
        Assert.Contains(clarified.ActivePackage.Assumptions, assumption => assumption == "Pending: Choose the publication intent");
        Assert.Contains(clarified.ActivePackage.Assumptions, assumption => assumption == sourceQuestion.Choices[0].Effect);
        Assert.Contains(clarified.Clarifications, question => question.Id == "publish-intent");
        Assert.Contains(clarified.ActivePackage.ValidationItems, item => item.Severity == StudioValidationSeverity.Blocker);
        Assert.DoesNotContain(clarified.ActivePackage.Warnings, warning => warning.Id == "source-ambiguous");
        Assert.Contains(clarified.ActivePackage.Warnings, warning => warning.Id == "publish-intent-ambiguous");
    }

    [Fact]
    public async Task PublishIntentClarificationUsesPublicationWarning()
    {
        IStudioAuthoringShell shell = new InMemoryStudioAuthoringShell();

        var session = await shell.GeneratePackageAsync(
            await shell.CreateInitialSessionAsync(),
            "app",
            "Build an app using the permits layer");

        Assert.Single(session.Clarifications);
        Assert.Contains(session.Clarifications, question => question.Id == "publish-intent");
        Assert.DoesNotContain(session.ActivePackage.Warnings, warning => warning.Id == "source-ambiguous");
        Assert.Contains(
            session.ActivePackage.Warnings,
            warning => warning.Id == "publish-intent-ambiguous" && warning.Target == "publication_intent");
    }

    [Fact]
    public async Task WorkflowSelectionRebuildsClarificationsForCurrentPrompt()
    {
        IStudioAuthoringShell shell = new InMemoryStudioAuthoringShell();
        var session = await shell.GeneratePackageAsync(
            await shell.CreateInitialSessionAsync(),
            "map",
            "Create a map from parcels");

        var selected = await shell.SelectWorkflowAsync(session, "query");

        Assert.Equal("query", selected.SelectedWorkflowId);
        Assert.Equal("query.package", selected.ActivePackage.PackageType);
        Assert.DoesNotContain(selected.Clarifications, question => question.Id == "publish-intent");
        Assert.DoesNotContain(selected.ActivePackage.Assumptions, assumption => assumption == "Pending: Choose the publication intent");
        Assert.DoesNotContain(selected.ActivePackage.ValidationItems, item => item.Severity == StudioValidationSeverity.Blocker);
    }

    [Fact]
    public void LifecycleStatesHaveDistinctVisualDescriptors()
    {
        var descriptors = StudioAuthoringContract.LifecycleDescriptors;

        Assert.Equal(3, descriptors.Count);
        Assert.Contains(descriptors, descriptor => descriptor.State == StudioPackageLifecycleState.Draft);
        Assert.Contains(descriptors, descriptor => descriptor.State == StudioPackageLifecycleState.SavedVersion);
        Assert.Contains(descriptors, descriptor => descriptor.State == StudioPackageLifecycleState.Published);
        Assert.Equal(descriptors.Count, descriptors.Select(descriptor => descriptor.CssClass).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(descriptors.Count, descriptors.Select(descriptor => descriptor.Label).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task SaveAndPublishPreservePackageIdentity()
    {
        IStudioAuthoringShell shell = new InMemoryStudioAuthoringShell();
        var session = await shell.GeneratePackageAsync(
            await shell.CreateInitialSessionAsync(),
            "dashboard",
            "Create an org dashboard using the permits layer");

        var saved = await shell.SaveVersionAsync(session);
        var published = await shell.PublishAsync(saved);

        Assert.Empty(session.Clarifications);
        Assert.Equal(session.ActivePackage.PackageRef, saved.ActivePackage.PackageRef);
        Assert.Equal(saved.ActivePackage.PackageRef, published.ActivePackage.PackageRef);
        Assert.Equal(StudioPackageLifecycleState.SavedVersion, saved.ActivePackage.LifecycleState);
        Assert.Equal(StudioPackageLifecycleState.Published, published.ActivePackage.LifecycleState);
        Assert.Contains(published.ActivePackage.Provenance, item => item.Evidence == "Published");
    }

    [Fact]
    public async Task OpenClarificationsBlockSaveVersion()
    {
        IStudioAuthoringShell shell = new InMemoryStudioAuthoringShell();
        var session = await shell.GeneratePackageAsync(await shell.CreateInitialSessionAsync(), "map", "Make a map");

        var blocked = await shell.SaveVersionAsync(session);

        Assert.Equal(StudioPackageLifecycleState.Draft, blocked.ActivePackage.LifecycleState);
        Assert.NotEmpty(blocked.Clarifications);
        Assert.Contains(blocked.ActivePackage.ValidationItems, item => item.Severity == StudioValidationSeverity.Blocker);
        Assert.DoesNotContain(blocked.ActivePackage.Provenance, item => item.Action == "Lifecycle state changed");
    }

    [Fact]
    public async Task OpenClarificationsBlockValidationPreviewAndPublish()
    {
        IStudioAuthoringShell shell = new InMemoryStudioAuthoringShell();
        var session = await shell.GeneratePackageAsync(await shell.CreateInitialSessionAsync(), "map", "Make a map");
        var publishable = session with
        {
            ActivePackage = session.ActivePackage with { LifecycleState = StudioPackageLifecycleState.SavedVersion },
            Draft = session.Draft! with { CurrentVersionId = Guid.NewGuid().ToString() }
        };

        var validated = await shell.ValidateAsync(session);
        var previewed = await shell.PreviewPlanAsync(session);
        var published = await shell.PublishAsync(publishable);

        Assert.NotEmpty(session.Clarifications);
        Assert.Contains("clarifications", validated.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clarifications", previewed.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clarifications", published.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(previewed.PreviewPlan);
        Assert.Equal(StudioPackageLifecycleState.SavedVersion, published.ActivePackage.LifecycleState);
        Assert.DoesNotContain(published.ActivePackage.Provenance, item => item.Action == "Lifecycle state changed");
    }
}
