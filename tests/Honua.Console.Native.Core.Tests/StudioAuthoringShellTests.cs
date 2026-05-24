using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class StudioAuthoringShellTests
{
    [Fact]
    public void AmbiguousPromptProducesInspectablePackageWithClarification()
    {
        IStudioAuthoringShell shell = new InMemoryStudioAuthoringShell();
        var session = shell.CreateInitialSession();

        var result = shell.SubmitPrompt(session, "map", "Make a map");

        Assert.NotNull(result.ActivePackage);
        Assert.Equal(StudioAuthoringContract.Name, result.ActivePackage.ContractName);
        Assert.Equal(StudioAuthoringContract.Version, result.ActivePackage.ContractVersion);
        Assert.Equal("map.package", result.ActivePackage.PackageType);
        Assert.NotEmpty(result.Clarifications);
        Assert.Contains(result.Clarifications, question => question.Id == "source-binding");
        Assert.Contains(result.ActivePackage.ValidationItems, item => item.Severity == StudioValidationSeverity.Blocker);
        Assert.Contains(result.ActivePackage.Provenance, item => item.Action == "Clarification requested");
    }

    [Fact]
    public void ClarificationUpdatesPackageWithoutHidingInspectorData()
    {
        IStudioAuthoringShell shell = new InMemoryStudioAuthoringShell();
        var session = shell.SubmitPrompt(shell.CreateInitialSession(), "map", "Make a map");
        var sourceQuestion = session.Clarifications.First(question => question.Id == "source-binding");

        var clarified = shell.ApplyClarification(session, sourceQuestion.Id, sourceQuestion.Choices[0].Id);

        Assert.DoesNotContain(clarified.Clarifications, question => question.Id == "source-binding");
        Assert.Contains(clarified.ActivePackage.DataBindings, binding => binding.Status == "Bound after clarification");
        Assert.NotEmpty(clarified.ActivePackage.Assumptions);
        Assert.NotEmpty(clarified.ActivePackage.ValidationItems);
        Assert.NotEmpty(clarified.ActivePackage.Provenance);
    }

    [Fact]
    public void LifecycleStatesHaveDistinctVisualDescriptors()
    {
        var descriptors = StudioAuthoringContract.LifecycleDescriptors;

        Assert.Equal(4, descriptors.Count);
        Assert.Contains(descriptors, descriptor => descriptor.State == StudioPackageLifecycleState.Draft);
        Assert.Contains(descriptors, descriptor => descriptor.State == StudioPackageLifecycleState.Preview);
        Assert.Contains(descriptors, descriptor => descriptor.State == StudioPackageLifecycleState.SavedVersion);
        Assert.Contains(descriptors, descriptor => descriptor.State == StudioPackageLifecycleState.Published);
        Assert.Equal(descriptors.Count, descriptors.Select(descriptor => descriptor.CssClass).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(descriptors.Count, descriptors.Select(descriptor => descriptor.Label).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PackageTransitionsPreservePackageIdentity()
    {
        IStudioAuthoringShell shell = new InMemoryStudioAuthoringShell();
        var session = shell.SubmitPrompt(
            shell.CreateInitialSession(),
            "dashboard",
            "Create an org dashboard using the permits layer");

        var preview = shell.TransitionPackage(session, StudioPackageLifecycleState.Preview);
        var saved = shell.TransitionPackage(preview, StudioPackageLifecycleState.SavedVersion);
        var published = shell.TransitionPackage(saved, StudioPackageLifecycleState.Published);

        Assert.Equal(session.ActivePackage.PackageRef, preview.ActivePackage.PackageRef);
        Assert.Equal(preview.ActivePackage.PackageRef, saved.ActivePackage.PackageRef);
        Assert.Equal(saved.ActivePackage.PackageRef, published.ActivePackage.PackageRef);
        Assert.Equal(StudioPackageLifecycleState.Published, published.ActivePackage.LifecycleState);
        Assert.Contains(published.ActivePackage.Provenance, item => item.Evidence == "Published");
    }

    [Fact]
    public void OpenClarificationsBlockLifecycleTransition()
    {
        IStudioAuthoringShell shell = new InMemoryStudioAuthoringShell();
        var session = shell.SubmitPrompt(shell.CreateInitialSession(), "map", "Make a map");

        var blocked = shell.TransitionPackage(session, StudioPackageLifecycleState.Preview);

        Assert.Equal(StudioPackageLifecycleState.Draft, blocked.ActivePackage.LifecycleState);
        Assert.NotEmpty(blocked.Clarifications);
        Assert.Contains(blocked.ActivePackage.ValidationItems, item => item.Severity == StudioValidationSeverity.Blocker);
        Assert.DoesNotContain(blocked.ActivePackage.Provenance, item => item.Action == "Lifecycle state changed");
    }
}
