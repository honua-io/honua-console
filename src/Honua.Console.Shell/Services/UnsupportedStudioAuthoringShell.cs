using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The runtime default when no honua-server base address is configured. Every operation returns a
/// missing-binding session so the Studio shell renders the shared blocked surface (charter §5) instead
/// of mock package data. Mirrors <see cref="UnsupportedOperateTransitionDataSource"/>.
/// </summary>
public sealed class UnsupportedStudioAuthoringShell : IStudioAuthoringShell
{
    private static readonly StudioAuthoringBindingState MissingBinding = new(
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so Studio can bind the server-owned package lifecycle from honua-server.");

    private static readonly StudioPackageSnapshot EmptyPackage = new(
        StudioAuthoringContract.Name,
        StudioAuthoringContract.Version,
        "studio-unbound",
        "package",
        string.Empty,
        "Studio package shell",
        "Studio package data is not bound to a server.",
        StudioPackageLifecycleState.Draft,
        [],
        [],
        [],
        [],
        []);

    private static readonly StudioAuthoringSession BlockedSession = new(
        [],
        string.Empty,
        string.Empty,
        [],
        EmptyPackage,
        MissingBinding);

    public Task<StudioAuthoringSession> CreateInitialSessionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(BlockedSession);

    public Task<StudioAuthoringSession> SelectWorkflowAsync(
        StudioAuthoringSession session,
        string workflowId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(session with { BindingState = MissingBinding });

    public Task<StudioAuthoringSession> GeneratePackageAsync(
        StudioAuthoringSession session,
        string workflowId,
        string prompt,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(session with { BindingState = MissingBinding });

    public Task<StudioAuthoringSession> ApplyClarificationAsync(
        StudioAuthoringSession session,
        string questionId,
        string choiceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(session with { BindingState = MissingBinding });

    public Task<StudioAuthoringSession> ValidateAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(session with { BindingState = MissingBinding });

    public Task<StudioAuthoringSession> PreviewPlanAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(session with { BindingState = MissingBinding });

    public Task<StudioAuthoringSession> SaveVersionAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(session with { BindingState = MissingBinding });

    public Task<StudioAuthoringSession> PublishAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(session with { BindingState = MissingBinding });
}
