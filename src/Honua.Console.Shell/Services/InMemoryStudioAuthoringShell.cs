using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Local, in-memory authoring simulator for explicit demo composition
/// (<c>AddHonuaConsoleDemoStudioAuthoringShell</c>) and unit tests only. It is NOT registered as the
/// runtime default — server-owned Studio package data binds to honua-server through
/// <see cref="ServerStudioAuthoringShell"/>. This simulator never claims to be backed by real server
/// package lifecycle data; it exists so the shell can be exercised without a server.
/// </summary>
public sealed class InMemoryStudioAuthoringShell : IStudioAuthoringShell
{
    private static readonly IReadOnlyList<StudioWorkflowOption> WorkflowOptions =
        StudioPackageFamilyCatalog.AllFamilies
            .Select(StudioPackageFamilyCatalog.ToOption)
            .ToArray();

    public Task<StudioAuthoringSession> CreateInitialSessionAsync(CancellationToken cancellationToken = default)
    {
        var workflow = WorkflowOptions[0];
        var session = new StudioAuthoringSession(
            WorkflowOptions,
            workflow.Id,
            string.Empty,
            [],
            CreatePackage(workflow, string.Empty, []));
        return Task.FromResult(session);
    }

    public Task<StudioAuthoringSession> SelectWorkflowAsync(
        StudioAuthoringSession session,
        string workflowId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var workflow = ResolveWorkflow(workflowId);
        var clarifications = string.IsNullOrWhiteSpace(session.Prompt)
            ? []
            : StudioClarificationPlanner.Build(session.Prompt, IsPublicationFamily(workflow));

        return Task.FromResult(session with
        {
            SelectedWorkflowId = workflow.Id,
            Clarifications = clarifications,
            ActivePackage = CreatePackage(workflow, session.Prompt, clarifications),
            Draft = null,
            PreviewPlan = null
        });
    }

    public Task<StudioAuthoringSession> GeneratePackageAsync(
        StudioAuthoringSession session,
        string workflowId,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var workflow = ResolveWorkflow(workflowId);
        var normalizedPrompt = (prompt ?? string.Empty).Trim();
        var clarifications = StudioClarificationPlanner.Build(normalizedPrompt, IsPublicationFamily(workflow));

        return Task.FromResult(session with
        {
            SelectedWorkflowId = workflow.Id,
            Prompt = normalizedPrompt,
            Clarifications = clarifications,
            ActivePackage = CreatePackage(workflow, normalizedPrompt, clarifications),
            Draft = new StudioDraftHandle(
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                $"demo-{workflow.Id}",
                0),
            PreviewPlan = null,
            StatusMessage = "Demo draft created (no server binding)."
        });
    }

    public Task<StudioAuthoringSession> ApplyClarificationAsync(
        StudioAuthoringSession session,
        string questionId,
        string choiceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var question = session.Clarifications.FirstOrDefault(q => string.Equals(q.Id, questionId, StringComparison.Ordinal));
        if (question is null)
        {
            return Task.FromResult(session);
        }

        var choice = question.Choices.FirstOrDefault(c => string.Equals(c.Id, choiceId, StringComparison.Ordinal))
            ?? question.Choices[0];
        var remaining = session.Clarifications
            .Where(q => !string.Equals(q.Id, question.Id, StringComparison.Ordinal))
            .ToArray();
        var package = session.ActivePackage;

        var dataBindings = package.DataBindings
            .Select(binding => string.Equals(question.Id, "source-binding", StringComparison.Ordinal)
                ? binding with { SourceRef = choice.Label, Status = "Bound after clarification" }
                : binding)
            .ToArray();

        var assumptions = package.Assumptions
            .Where(assumption => !string.Equals(assumption, $"Pending: {question.Label}", StringComparison.Ordinal))
            .Append(choice.Effect)
            .ToArray();
        var provenance = package.Provenance
            .Append(new StudioProvenanceEvent("Builder", "Clarification accepted", $"{question.Label}: {choice.Label}"))
            .ToArray();

        return Task.FromResult(session with
        {
            Clarifications = remaining,
            ActivePackage = package with
            {
                Summary = remaining.Length == 0
                    ? "Demo package is ready for preview-plan with clarified bindings."
                    : package.Summary,
                DataBindings = dataBindings,
                Warnings = remaining.Length == 0 ? [] : StudioClarificationPlanner.ToWarnings(remaining),
                ValidationItems = remaining.Length == 0 ? CreateReadyValidation() : CreateBlockedValidation(remaining),
                Assumptions = assumptions,
                Provenance = provenance
            }
        });
    }

    public Task<StudioAuthoringSession> ValidateAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var validation = session.Clarifications.Count > 0
            ? CreateBlockedValidation(session.Clarifications)
            : CreateReadyValidation();
        return Task.FromResult(session with
        {
            ActivePackage = session.ActivePackage with { ValidationItems = validation },
            StatusMessage = session.Clarifications.Count > 0 ? "Resolve clarifications before validation passes." : "Demo validation passed."
        });
    }

    public Task<StudioAuthoringSession> PreviewPlanAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Task.FromResult(session with
        {
            PreviewPlan = new StudioPreviewPlanView(true, false, ["Resolve bindings", "Render demo preview plan"]),
            StatusMessage = "Demo preview plan ready (synchronous)."
        });
    }

    public Task<StudioAuthoringSession> SaveVersionAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Clarifications.Count > 0)
        {
            return Task.FromResult(session with { StatusMessage = "Resolve open clarifications before saving a version." });
        }

        return Task.FromResult(Transition(session, StudioPackageLifecycleState.SavedVersion, "Saved demo version."));
    }

    public Task<StudioAuthoringSession> PublishAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Clarifications.Count > 0)
        {
            return Task.FromResult(session with { StatusMessage = "Resolve open clarifications before publishing." });
        }

        return Task.FromResult(Transition(session, StudioPackageLifecycleState.Published, "Published"));
    }

    private static StudioAuthoringSession Transition(
        StudioAuthoringSession session,
        StudioPackageLifecycleState state,
        string evidence) =>
        session with
        {
            ActivePackage = session.ActivePackage with
            {
                LifecycleState = state,
                Provenance = session.ActivePackage.Provenance
                    .Append(new StudioProvenanceEvent("Studio", "Lifecycle state changed", evidence))
                    .ToArray()
            },
            StatusMessage = evidence
        };

    private static bool IsPublicationFamily(StudioWorkflowOption workflow)
    {
        var family = StudioPackageFamilyCatalog.ParseId(workflow.Id);
        return family is null || StudioPackageFamilyCatalog.IsPublicationFamily(family.Value);
    }

    private static StudioWorkflowOption ResolveWorkflow(string workflowId) =>
        WorkflowOptions.FirstOrDefault(w => string.Equals(w.Id, workflowId, StringComparison.Ordinal))
            ?? WorkflowOptions[0];

    private static StudioPackageSnapshot CreatePackage(
        StudioWorkflowOption workflow,
        string prompt,
        IReadOnlyList<StudioClarificationQuestion> clarifications)
    {
        var needsClarification = clarifications.Count > 0;
        var hasSourceQuestion = clarifications.Any(q => string.Equals(q.Id, "source-binding", StringComparison.Ordinal));
        var title = string.IsNullOrWhiteSpace(prompt)
            ? $"{workflow.Label} package draft"
            : BuildTitle(workflow, prompt);

        var dataBindings = hasSourceQuestion
            ? new[]
            {
                new StudioDataBindingSummary("source-binding", "Primary source", "Unresolved Catalog source", "Clarification required")
            }
            : new[]
            {
                new StudioDataBindingSummary("source-binding", "Primary source", "catalog:item/demo-city-observations", "Ready for validation")
            };

        var warnings = needsClarification
            ? StudioClarificationPlanner.ToWarnings(clarifications)
            : Array.Empty<StudioPackageWarning>();

        var assumptions = needsClarification
            ? clarifications.Select(q => $"Pending: {q.Label}").ToArray()
            : new[]
            {
                $"Artifact family selected as {workflow.PackageType}.",
                "A real server binding owns final validation, lineage, and publication records."
            };

        return new StudioPackageSnapshot(
            StudioAuthoringContract.Name,
            StudioAuthoringContract.Version,
            needsClarification ? $"draft-{workflow.Id}-clarify" : $"draft-{workflow.Id}-package",
            workflow.PackageType,
            string.IsNullOrEmpty(workflow.SchemaVersion) ? "demo/v1" : workflow.SchemaVersion,
            title,
            needsClarification
                ? "Demo package is inspectable, but generation is blocked on clarification."
                : "Demo package is inspectable and ready for a preview plan.",
            StudioPackageLifecycleState.Draft,
            assumptions,
            dataBindings,
            warnings,
            needsClarification ? CreateBlockedValidation(clarifications) : CreateReadyValidation(),
            CreateProvenance(prompt, needsClarification));
    }

    private static IReadOnlyList<StudioValidationItem> CreateReadyValidation() =>
    [
        new(StudioValidationSeverity.Passed, "Package shape", "Contract name, package type, and schema version are present."),
        new(StudioValidationSeverity.Passed, "Inspector coverage", "Assumptions, bindings, warnings, validation, and provenance are inspectable."),
        new(StudioValidationSeverity.Info, "Server lifecycle", "Real validation, persistence, and publish are owned by the server binding.")
    ];

    private static IReadOnlyList<StudioValidationItem> CreateBlockedValidation(
        IReadOnlyList<StudioClarificationQuestion> clarifications) =>
    [
        new(StudioValidationSeverity.Blocker, "Clarification required", $"{clarifications.Count} structured clarification item(s) must be answered before a preview plan."),
        new(StudioValidationSeverity.Warning, "No silent assumptions", "Studio is holding generation at draft package state until the builder answers.")
    ];

    private static IReadOnlyList<StudioProvenanceEvent> CreateProvenance(string prompt, bool needsClarification) =>
    [
        new("Builder", "Prompt submitted", string.IsNullOrWhiteSpace(prompt) ? "No prompt submitted yet." : prompt),
        new("Studio", needsClarification ? "Clarification requested" : "Package generated", needsClarification
            ? "Ambiguous inputs were routed to structured questions."
            : "Demo package was generated from clarified prompt context.")
    ];

    private static string BuildTitle(StudioWorkflowOption workflow, string prompt)
    {
        const int maxPromptLength = 54;
        var clippedPrompt = prompt.Length <= maxPromptLength ? prompt : $"{prompt[..maxPromptLength]}...";
        return $"{workflow.Label}: {clippedPrompt}";
    }
}
