using System.Text;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
// The server wire enum, not the editor-catalog enum Honua.Console.Shell.Models.StudioPackageFamily.
using StudioPackageFamily = Honua.Console.Contracts.StudioPackageFamily;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Server-backed <see cref="IStudioAuthoringShell"/>. Binds the Studio package draft/lifecycle,
/// validation, and preview-planning surfaces to honua-server (#1180/#1181) through the
/// <see cref="IStudioPackageLifecycleClient"/> shim. Prompt and structured clarification stay
/// Console-local UX; no server-owned package data is mocked. When the server cannot be reached or
/// rejects the request, the session carries a <see cref="StudioAuthoringBindingState"/> for the shell's
/// missing-binding/forbidden surfaces rather than fabricated success.
/// </summary>
public sealed class ServerStudioAuthoringShell : IStudioAuthoringShell
{
    private readonly IStudioPackageLifecycleClient _client;

    public ServerStudioAuthoringShell(IStudioPackageLifecycleClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<StudioAuthoringSession> CreateInitialSessionAsync(CancellationToken cancellationToken = default)
    {
        var families = await _client.ListPackageFamiliesAsync(cancellationToken).ConfigureAwait(false);
        if (!families.IsSuccess || families.Data is null)
        {
            return BlockedSession(families.Issue);
        }

        var workflows = families.Data.Families
            .Select(StudioPackageFamilyCatalog.ToOption)
            .ToArray();

        if (workflows.Length == 0)
        {
            return BlockedSession(new StudioEndpointIssue(
                "Unsupported",
                "GET /api/v1/studio/package-families",
                "The Honua server did not report any Studio package families."));
        }

        var selected = workflows.FirstOrDefault(option =>
            string.Equals(option.SupportLevel, "Supported", StringComparison.Ordinal)) ?? workflows[0];

        return new StudioAuthoringSession(
            workflows,
            selected.Id,
            string.Empty,
            [],
            BuildPlaceholderSnapshot(selected));
    }

    public Task<StudioAuthoringSession> SelectWorkflowAsync(
        StudioAuthoringSession session,
        string workflowId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.BindingState is not null)
        {
            return Task.FromResult(session);
        }

        var option = ResolveOption(session, workflowId);
        var clarifications = string.IsNullOrWhiteSpace(session.Prompt)
            ? Array.Empty<StudioClarificationQuestion>()
            : StudioClarificationPlanner.Build(session.Prompt, IsPublicationFamily(option));

        // Switching family starts a new package; the draft is created on Generate.
        return Task.FromResult(session with
        {
            SelectedWorkflowId = option.Id,
            Clarifications = clarifications,
            ActivePackage = BuildPlaceholderSnapshot(option),
            PreviewPlan = null,
            Draft = null,
            StatusMessage = null
        });
    }

    public async Task<StudioAuthoringSession> GeneratePackageAsync(
        StudioAuthoringSession session,
        string workflowId,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.BindingState is not null)
        {
            return session;
        }

        var option = ResolveOption(session, workflowId);
        var family = StudioPackageFamilyCatalog.ParseId(option.Id) ?? StudioPackageFamily.Map;
        var normalizedPrompt = (prompt ?? string.Empty).Trim();
        var clarifications = StudioClarificationPlanner.Build(normalizedPrompt, IsPublicationFamily(option));

        var request = new CreateStudioPackageDraftRequest
        {
            PackageKey = BuildPackageKey(option.Id, normalizedPrompt),
            Envelope = new StudioPackageEnvelope
            {
                Family = family,
                SchemaVersion = string.IsNullOrWhiteSpace(option.SchemaVersion) ? "1.0" : option.SchemaVersion,
                Format = string.IsNullOrWhiteSpace(option.Format) ? null : option.Format
            }
        };

        var created = await _client.CreatePackageDraftAsync(request, cancellationToken).ConfigureAwait(false);
        if (!created.IsSuccess || created.Data is null)
        {
            return session with
            {
                SelectedWorkflowId = option.Id,
                Prompt = normalizedPrompt,
                BindingState = ToBindingState(created.Issue),
                StatusMessage = null
            };
        }

        var draft = created.Data;
        return session with
        {
            SelectedWorkflowId = option.Id,
            Prompt = normalizedPrompt,
            Clarifications = clarifications,
            ActivePackage = MapDraft(draft, option, StudioPackageLifecycleState.Draft, clarifications, normalizedPrompt),
            PreviewPlan = null,
            Draft = new StudioDraftHandle(
                draft.DraftId.ToString(),
                draft.ItemId.ToString(),
                draft.PackageKey,
                draft.Generation),
            BindingState = null,
            StatusMessage = $"Server draft created ({draft.PackageKey})."
        };
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
        var bindings = package.DataBindings
            .Select(binding => string.Equals(question.Id, "source-binding", StringComparison.Ordinal)
                ? binding with { SourceRef = choice.Label, Status = "Bound after clarification" }
                : binding)
            .ToArray();

        var warnings = StudioClarificationPlanner.ToWarnings(remaining);
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
                DataBindings = bindings,
                Warnings = warnings,
                Assumptions = assumptions,
                Provenance = provenance
            }
        });
    }

    public async Task<StudioAuthoringSession> ValidateAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Draft is null)
        {
            return session with { StatusMessage = "Generate a package before running validation." };
        }

        var result = await _client.ValidatePackageDraftAsync(Guid.Parse(session.Draft.DraftId), cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return session with { BindingState = ToBindingState(result.Issue) };
        }

        return session with
        {
            ActivePackage = session.ActivePackage with
            {
                ValidationItems = MapValidation(result.Data),
                Warnings = MergeWarnings(session.Clarifications, result.Data)
            },
            StatusMessage = $"Validation: {FormatStatus(result.Data.Status)}."
        };
    }

    public async Task<StudioAuthoringSession> PreviewPlanAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Draft is null)
        {
            return session with { StatusMessage = "Generate a package before planning a preview." };
        }

        var result = await _client.CreatePreviewPlanAsync(Guid.Parse(session.Draft.DraftId), cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return session with { BindingState = ToBindingState(result.Issue) };
        }

        var plan = result.Data;
        return session with
        {
            PreviewPlan = new StudioPreviewPlanView(plan.Synchronous, plan.RequiresJob, plan.Steps),
            ActivePackage = session.ActivePackage with
            {
                ValidationItems = MapValidation(plan.Validation),
                Warnings = MergeWarnings(session.Clarifications, plan.Validation)
            },
            StatusMessage = plan.RequiresJob
                ? "Preview plan ready (job-backed)."
                : "Preview plan ready (synchronous)."
        };
    }

    public async Task<StudioAuthoringSession> SaveVersionAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Draft is null)
        {
            return session with { StatusMessage = "Generate a package before saving a version." };
        }

        if (session.Clarifications.Count > 0)
        {
            return session with { StatusMessage = "Resolve open clarifications before saving a version." };
        }

        var result = await _client.SaveContentVersionAsync(
                Guid.Parse(session.Draft.DraftId),
                new SaveStudioContentVersionRequest { ChangeNote = session.Prompt },
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return session with { BindingState = ToBindingState(result.Issue) };
        }

        var version = result.Data;
        return session with
        {
            ActivePackage = session.ActivePackage with
            {
                LifecycleState = StudioPackageLifecycleState.SavedVersion,
                ValidationItems = MapValidation(version.Validation),
                Provenance = session.ActivePackage.Provenance
                    .Append(new StudioProvenanceEvent("Studio", "Content version saved", $"v{version.VersionNumber} ({version.VersionId})"))
                    .ToArray()
            },
            Draft = session.Draft with { CurrentVersionId = version.VersionId.ToString() },
            StatusMessage = $"Saved content version v{version.VersionNumber}."
        };
    }

    public async Task<StudioAuthoringSession> PublishAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Draft?.CurrentVersionId is null)
        {
            return session with { StatusMessage = "Save a content version before publishing." };
        }

        var result = await _client.CreatePublishRequestAsync(
                Guid.Parse(session.Draft.ItemId),
                Guid.Parse(session.Draft.CurrentVersionId),
                new CreateStudioPublicationRequest(),
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return session with { BindingState = ToBindingState(result.Issue) };
        }

        var publication = result.Data;
        var published = publication.Status == StudioPublicationRequestStatus.Accepted;
        return session with
        {
            ActivePackage = session.ActivePackage with
            {
                LifecycleState = published ? StudioPackageLifecycleState.Published : StudioPackageLifecycleState.SavedVersion,
                Provenance = session.ActivePackage.Provenance
                    .Append(new StudioProvenanceEvent("Studio", "Publication requested", $"{publication.Status} ({publication.RequestId})"))
                    .ToArray()
            },
            StatusMessage = published
                ? "Published the current content version."
                : $"Publication request {FormatPublicationStatus(publication.Status)}."
        };
    }

    private StudioWorkflowOption ResolveOption(StudioAuthoringSession session, string workflowId) =>
        session.Workflows.FirstOrDefault(option => string.Equals(option.Id, workflowId, StringComparison.Ordinal))
            ?? session.Workflows.FirstOrDefault(option => string.Equals(option.Id, session.SelectedWorkflowId, StringComparison.Ordinal))
            ?? session.Workflows[0];

    private static bool IsPublicationFamily(StudioWorkflowOption option)
    {
        var family = StudioPackageFamilyCatalog.ParseId(option.Id);
        return family is null || StudioPackageFamilyCatalog.IsPublicationFamily(family.Value);
    }

    private static StudioAuthoringSession BlockedSession(StudioEndpointIssue? issue) =>
        new(
            [],
            string.Empty,
            string.Empty,
            [],
            EmptySnapshot(),
            ToBindingState(issue));

    private static StudioAuthoringBindingState ToBindingState(StudioEndpointIssue? issue) =>
        issue is null
            ? new StudioAuthoringBindingState(
                "Unavailable",
                "honua-server Studio API",
                "The Honua server did not return Studio package data.")
            : new StudioAuthoringBindingState(issue.State, issue.Contract, issue.Detail);

    private static StudioPackageSnapshot BuildPlaceholderSnapshot(StudioWorkflowOption option)
    {
        var assumptions = new List<string>
        {
            $"Family {option.PackageType} support level: {(string.IsNullOrEmpty(option.SupportLevel) ? "unknown" : option.SupportLevel)}.",
            "Generate a package to create a server draft; the server owns validation, lineage, and publication."
        };
        assumptions.AddRange(option.Limitations);

        var warnings = string.Equals(option.SupportLevel, "Supported", StringComparison.Ordinal)
            ? Array.Empty<StudioPackageWarning>()
            : [new StudioPackageWarning(
                "support-level",
                $"The server reports {option.PackageType} as {(string.IsNullOrEmpty(option.SupportLevel) ? "not fully supported" : option.SupportLevel.ToLowerInvariant())}; some lifecycle actions may be unavailable.",
                "support_level")];

        return new StudioPackageSnapshot(
            StudioAuthoringContract.Name,
            StudioAuthoringContract.Version,
            $"{option.Id}-draft",
            option.PackageType,
            option.SchemaVersion,
            $"{option.Label} package",
            "Generate a package to create a server draft and run validation.",
            StudioPackageLifecycleState.Draft,
            assumptions,
            [],
            warnings,
            [new StudioValidationItem(StudioValidationSeverity.Info, "Not validated", "Generate a package to run server validation.")],
            []);
    }

    private static StudioPackageSnapshot MapDraft(
        StudioPackageDraft draft,
        StudioWorkflowOption option,
        StudioPackageLifecycleState lifecycle,
        IReadOnlyList<StudioClarificationQuestion> clarifications,
        string prompt)
    {
        var assumptions = new List<string>
        {
            $"Artifact family selected as {option.PackageType} (schema {draft.Envelope.SchemaVersion}).",
            "Server validation owns final permissions, lineage, and publication records."
        };
        assumptions.AddRange(draft.Validation.UnsupportedCapabilities.Select(capability => $"Unsupported capability: {capability}"));

        return new StudioPackageSnapshot(
            StudioAuthoringContract.Name,
            StudioAuthoringContract.Version,
            draft.PackageKey,
            option.PackageType,
            draft.Envelope.SchemaVersion,
            BuildTitle(option, prompt),
            DescribeStatus(draft.Validation.Status),
            lifecycle,
            assumptions,
            MapBindings(draft.Envelope.Bindings),
            MergeWarnings(clarifications, draft.Validation),
            MapValidation(draft.Validation),
            MapProvenance(draft.Envelope.Provenance));
    }

    private static IReadOnlyList<StudioDataBindingSummary> MapBindings(IReadOnlyList<StudioPackageBinding> bindings) =>
        bindings.Select(binding => new StudioDataBindingSummary(
            binding.Key,
            string.IsNullOrWhiteSpace(binding.Kind) ? binding.Key : binding.Kind,
            binding.Ref,
            binding.Crs is null && binding.Srid is null
                ? "Bound"
                : $"Bound ({binding.Crs ?? $"SRID {binding.Srid}"})")).ToArray();

    private static IReadOnlyList<StudioProvenanceEvent> MapProvenance(IReadOnlyList<StudioProvenanceRef> provenance) =>
        provenance.Select(item => new StudioProvenanceEvent(
            string.IsNullOrWhiteSpace(item.ActorId) ? item.Kind : item.ActorId!,
            item.Rel,
            item.Ref)).ToArray();

    private static IReadOnlyList<StudioValidationItem> MapValidation(StudioValidationSummary summary)
    {
        var items = new List<StudioValidationItem>();
        foreach (var diagnostic in summary.Diagnostics)
        {
            items.Add(new StudioValidationItem(
                MapSeverity(diagnostic.Severity),
                diagnostic.Code,
                string.IsNullOrWhiteSpace(diagnostic.Path)
                    ? diagnostic.Message
                    : $"{diagnostic.Message} ({diagnostic.Path})"));
        }

        switch (summary.Status)
        {
            case StudioPackageValidationStatus.Valid when items.Count == 0:
                items.Add(new StudioValidationItem(StudioValidationSeverity.Passed, "Validation passed", "The server validated the package with no diagnostics."));
                break;
            case StudioPackageValidationStatus.NotValidated when items.Count == 0:
                items.Add(new StudioValidationItem(StudioValidationSeverity.Info, "Not validated", "Run Validate to evaluate the draft against the server."));
                break;
        }

        return items;
    }

    private static IReadOnlyList<StudioPackageWarning> MergeWarnings(
        IReadOnlyList<StudioClarificationQuestion> clarifications,
        StudioValidationSummary summary)
    {
        var warnings = new List<StudioPackageWarning>(StudioClarificationPlanner.ToWarnings(clarifications));
        warnings.AddRange(summary.UnsupportedCapabilities.Select(capability => new StudioPackageWarning(
            $"unsupported-{capability}",
            $"The server reports an unsupported capability: {capability}.",
            "unsupported_capability")));
        return warnings;
    }

    private static StudioValidationSeverity MapSeverity(StudioPackageDiagnosticSeverity severity) => severity switch
    {
        StudioPackageDiagnosticSeverity.Info => StudioValidationSeverity.Info,
        StudioPackageDiagnosticSeverity.Warning => StudioValidationSeverity.Warning,
        _ => StudioValidationSeverity.Blocker
    };

    private static StudioPackageSnapshot EmptySnapshot() => new(
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

    private static string BuildTitle(StudioWorkflowOption option, string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return $"{option.Label} package draft";
        }

        const int maxPromptLength = 54;
        var clipped = prompt.Length <= maxPromptLength ? prompt : $"{prompt[..maxPromptLength]}...";
        return $"{option.Label}: {clipped}";
    }

    private static string BuildPackageKey(string workflowId, string prompt)
    {
        var slugBuilder = new StringBuilder();
        foreach (var character in prompt.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                slugBuilder.Append(character);
            }
            else if (slugBuilder.Length > 0 && slugBuilder[^1] != '-')
            {
                slugBuilder.Append('-');
            }

            if (slugBuilder.Length >= 48)
            {
                break;
            }
        }

        var slug = slugBuilder.ToString().Trim('-');
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return string.IsNullOrEmpty(slug)
            ? $"studio-{workflowId}-{suffix}"
            : $"studio-{workflowId}-{slug}-{suffix}";
    }

    private static string DescribeStatus(StudioPackageValidationStatus status) => status switch
    {
        StudioPackageValidationStatus.Valid => "Draft package validated by the server and ready to save a version.",
        StudioPackageValidationStatus.Warning => "Draft package validated with warnings; review before saving a version.",
        StudioPackageValidationStatus.Invalid => "Draft package has validation blockers; resolve them before saving a version.",
        _ => "Draft package created on the server; run Validate to evaluate it."
    };

    private static string FormatStatus(StudioPackageValidationStatus status) => status switch
    {
        StudioPackageValidationStatus.Valid => "valid",
        StudioPackageValidationStatus.Warning => "valid with warnings",
        StudioPackageValidationStatus.Invalid => "invalid",
        _ => "not validated"
    };

    private static string FormatPublicationStatus(StudioPublicationRequestStatus status) => status switch
    {
        StudioPublicationRequestStatus.Accepted => "accepted",
        StudioPublicationRequestStatus.Pending => "pending review",
        _ => "rejected"
    };
}
