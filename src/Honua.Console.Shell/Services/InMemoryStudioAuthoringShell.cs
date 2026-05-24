using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class InMemoryStudioAuthoringShell : IStudioAuthoringShell
{
    private static readonly IReadOnlyList<StudioWorkflowOption> WorkflowOptions =
    [
        new("map", "Map", "map.package", "Layers, style, filters, popups, legend, and extent."),
        new("dashboard", "Dashboard", "dashboard.package", "Charts, maps, KPIs, filters, and linked selections."),
        new("report", "Report", "report.package", "Narrative sections, maps, tables, charts, and export settings."),
        new("form", "Form", "form.package", "Fields, rules, attachments, offline policy, and submit action."),
        new("app", "App", "app.package", "Routes, panels, generated app assets, permissions, and theme tokens."),
        new("query", "Query", "query.package", "Source layers, filters, joins, spatial predicates, and output schema."),
        new("analysis", "Analysis", "analysis.package", "Inputs, method, parameters, output package, and job profile."),
        new("workflow", "Workflow", "workflow.package", "Sources, transforms, sinks, triggers, dry-runs, and artifacts."),
        new("gp", "GP service", "workflow.package", "Process endpoint, parameters, worker profile, and invocation policy."),
        new("etl", "ETL", "workflow.package", "Extract, transform, load targets, schedules, and rejected-row evidence.")
    ];

    private static readonly IReadOnlyList<StudioRecentProject> RecentProjects =
    [
        new("Transit access map", "map.package", StudioPackageLifecycleState.Preview, "Updated 12 minutes ago"),
        new("Permit dashboard", "dashboard.package", StudioPackageLifecycleState.SavedVersion, "Version 3 saved yesterday"),
        new("Field inspection app", "app.package", StudioPackageLifecycleState.Published, "Published May 21")
    ];

    public StudioAuthoringSession CreateInitialSession()
    {
        var workflow = WorkflowOptions[0];
        var package = CreatePackage(
            workflow,
            "Start a map package from a prompt or selected Catalog source.",
            []);

        return new StudioAuthoringSession(
            WorkflowOptions,
            workflow.Id,
            string.Empty,
            [],
            package,
            RecentProjects);
    }

    public StudioAuthoringSession SelectWorkflow(StudioAuthoringSession session, string workflowId)
    {
        ArgumentNullException.ThrowIfNull(session);

        var workflow = ResolveWorkflow(workflowId);
        return session with
        {
            SelectedWorkflowId = workflow.Id,
            ActivePackage = CreatePackage(workflow, session.Prompt, session.Clarifications)
        };
    }

    public StudioAuthoringSession SubmitPrompt(StudioAuthoringSession session, string workflowId, string prompt)
    {
        ArgumentNullException.ThrowIfNull(session);

        var workflow = ResolveWorkflow(workflowId);
        var normalizedPrompt = (prompt ?? string.Empty).Trim();
        var clarifications = BuildClarifications(workflow, normalizedPrompt);

        return session with
        {
            SelectedWorkflowId = workflow.Id,
            Prompt = normalizedPrompt,
            Clarifications = clarifications,
            ActivePackage = CreatePackage(workflow, normalizedPrompt, clarifications)
        };
    }

    public StudioAuthoringSession ApplyClarification(StudioAuthoringSession session, string questionId, string choiceId)
    {
        ArgumentNullException.ThrowIfNull(session);

        var question = session.Clarifications.FirstOrDefault(q => string.Equals(q.Id, questionId, StringComparison.Ordinal));
        if (question is null)
        {
            return session;
        }

        var choice = question.Choices.FirstOrDefault(c => string.Equals(c.Id, choiceId, StringComparison.Ordinal))
            ?? question.Choices[0];
        var remainingClarifications = session.Clarifications
            .Where(q => !string.Equals(q.Id, question.Id, StringComparison.Ordinal))
            .ToArray();
        var package = session.ActivePackage;

        var dataBindings = package.DataBindings
            .Select(binding => string.Equals(question.Id, "source-binding", StringComparison.Ordinal)
                ? binding with
                {
                    SourceRef = choice.Label,
                    Status = "Bound after clarification"
                }
                : binding)
            .ToArray();

        var warnings = remainingClarifications.Length == 0
            ? package.Warnings
                .Where(warning => !string.Equals(warning.Id, "source-ambiguous", StringComparison.Ordinal))
                .ToArray()
            : package.Warnings;

        var validationItems = remainingClarifications.Length == 0
            ? CreateReadyValidation()
            : CreateBlockedValidation(remainingClarifications);

        var provenance = package.Provenance
            .Append(new StudioProvenanceEvent("Builder", "Clarification accepted", $"{question.Label}: {choice.Label}"))
            .ToArray();
        var assumptions = package.Assumptions
            .Where(assumption => !assumption.StartsWith("Pending:", StringComparison.Ordinal))
            .Append(choice.Effect)
            .ToArray();

        return session with
        {
            Clarifications = remainingClarifications,
            ActivePackage = package with
            {
                Summary = remainingClarifications.Length == 0
                    ? "Package is ready for preview with clarified bindings and publish intent."
                    : package.Summary,
                DataBindings = dataBindings,
                Warnings = warnings,
                ValidationItems = validationItems,
                Provenance = provenance,
                Assumptions = assumptions
            }
        };
    }

    public StudioAuthoringSession TransitionPackage(StudioAuthoringSession session, StudioPackageLifecycleState state)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session with
        {
            ActivePackage = session.ActivePackage with
            {
                LifecycleState = state,
                Provenance = session.ActivePackage.Provenance
                    .Append(new StudioProvenanceEvent("Studio", "Lifecycle state changed", FormatLifecycle(state)))
                    .ToArray()
            }
        };
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
                new StudioDataBindingSummary(
                    "source-binding",
                    "Primary source",
                    "Unresolved Catalog source",
                    "Clarification required")
            }
            : new[]
            {
                new StudioDataBindingSummary(
                    "source-binding",
                    "Primary source",
                    "catalog:item/demo-city-observations",
                    "Ready for validation")
            };

        var warnings = needsClarification
            ? new[]
            {
                new StudioPackageWarning(
                    "source-ambiguous",
                    "Source layer, field, or publish intent is ambiguous. Studio is waiting for structured clarification before applying assumptions.",
                    "data_bindings")
            }
            : new[]
            {
                new StudioPackageWarning(
                    "preview-sample",
                    "Preview uses a sample-sized binding until the server package lifecycle API is connected.",
                    "preview")
            };

        var assumptions = needsClarification
            ? clarifications.Select(q => $"Pending: {q.Label}").ToArray()
            : new[]
            {
                $"Artifact family selected as {workflow.PackageType}.",
                "Server validation will own final permissions, lineage, and publication records."
            };

        return new StudioPackageSnapshot(
            StudioAuthoringContract.Name,
            StudioAuthoringContract.Version,
            needsClarification ? $"draft-{workflow.Id}-clarify" : $"draft-{workflow.Id}-package",
            workflow.PackageType,
            StudioAuthoringContract.PackageSchemaVersion,
            title,
            needsClarification
                ? "Draft package is inspectable, but generation is blocked on clarification."
                : "Draft package is inspectable and ready for preview.",
            StudioPackageLifecycleState.Draft,
            assumptions,
            dataBindings,
            warnings,
            needsClarification ? CreateBlockedValidation(clarifications) : CreateReadyValidation(),
            CreateProvenance(prompt, needsClarification));
    }

    private static IReadOnlyList<StudioClarificationQuestion> BuildClarifications(
        StudioWorkflowOption workflow,
        string prompt)
    {
        var questions = new List<StudioClarificationQuestion>();
        var normalized = prompt.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(prompt) || !ContainsAny(normalized, "from", "using", "layer", "dataset", "table", "catalog", "parcels", "permits", "incidents", "schools", "saved map"))
        {
            questions.Add(new StudioClarificationQuestion(
                "source-binding",
                "Select the source binding",
                "Studio cannot validate fields, CRS, permissions, or lineage without a source.",
                [
                    new("catalog-search", "Search Catalog for a source", "Use Catalog search before binding source data."),
                    new("saved-map", "Use the current saved map", "Bind the generated package to the current saved map."),
                    new("upload", "Start from an uploaded table", "Bind the package after the uploaded table is registered.")
                ]));
        }

        if (IsPublicationWorkflow(workflow) && !ContainsAny(normalized, "public", "private", "org", "embed", "share", "internal", "published"))
        {
            questions.Add(new StudioClarificationQuestion(
                "publish-intent",
                "Choose the publication intent",
                "Preview, saved version, and published release require different package state.",
                [
                    new("draft-only", "Keep as a private draft", "Do not create a publication record until review."),
                    new("org-preview", "Prepare an organization preview", "Validate org-visible dependencies before publish."),
                    new("public-review", "Prepare public review", "Validate public-link and embed constraints before publish.")
                ]));
        }

        return questions;
    }

    private static IReadOnlyList<StudioValidationItem> CreateReadyValidation() =>
    [
        new(StudioValidationSeverity.Passed, "Package shape", "Contract name, package type, and schema version are present."),
        new(StudioValidationSeverity.Passed, "Inspector coverage", "Assumptions, bindings, warnings, validation, and provenance are inspectable."),
        new(StudioValidationSeverity.Info, "Server lifecycle", "Persistence and publish calls remain server-owned follow-up work.")
    ];

    private static IReadOnlyList<StudioValidationItem> CreateBlockedValidation(
        IReadOnlyList<StudioClarificationQuestion> clarifications) =>
    [
        new(StudioValidationSeverity.Blocker, "Clarification required", $"{clarifications.Count} structured clarification item(s) must be answered before preview."),
        new(StudioValidationSeverity.Warning, "No silent assumptions", "Studio is holding generation at draft package state until the builder answers.")
    ];

    private static IReadOnlyList<StudioProvenanceEvent> CreateProvenance(string prompt, bool needsClarification) =>
    [
        new("Builder", "Prompt submitted", string.IsNullOrWhiteSpace(prompt) ? "No prompt submitted yet." : prompt),
        new("Studio", needsClarification ? "Clarification requested" : "Package generated", needsClarification
            ? "Ambiguous inputs were routed to structured questions."
            : "Draft package was generated from clarified prompt context.")
    ];

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.Ordinal));

    private static bool IsPublicationWorkflow(StudioWorkflowOption workflow) =>
        workflow.Id is "map" or "dashboard" or "report" or "form" or "app" or "workflow" or "gp" or "etl";

    private static string BuildTitle(StudioWorkflowOption workflow, string prompt)
    {
        const int maxPromptLength = 54;
        var clippedPrompt = prompt.Length <= maxPromptLength ? prompt : $"{prompt[..maxPromptLength]}...";
        return $"{workflow.Label}: {clippedPrompt}";
    }

    private static string FormatLifecycle(StudioPackageLifecycleState state) =>
        StudioAuthoringContract.LifecycleDescriptors.First(descriptor => descriptor.State == state).Label;
}
