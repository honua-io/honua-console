using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the Operate publishing lookup surface (<c>OperatePublishingPage</c>).
/// </summary>
public static class PublishingLookupFieldKeys
{
    public const string LookupId = "publishing.lookupId";
    public const string RepublishTitle = "publishing.republishTitle";
}

/// <summary>
/// Console-owned snapshot of the publishing lookup form: the publication id the operator is reviewing and the
/// optional republish title. The republish title is free-form and optional, so it never produces a finding.
/// </summary>
/// <param name="LookupId">The publication identifier to look up.</param>
/// <param name="RepublishTitle">The optional new-version title for republish (free text; never validated).</param>
public sealed record PublishingLookupState(string? LookupId, string? RepublishTitle);

/// <summary>
/// Pure client-side validator for the Operate publishing lookup: the publication id must be a plausible
/// identifier token before the Review action runs. The republish title is optional and unvalidated. Mirrors the
/// <see cref="StudioMapValidator"/> pattern; keyed by <see cref="PublishingLookupFieldKeys"/>. Console-only — the
/// publication registry has no field-addressable body-validator for the lookup id.
/// </summary>
public sealed class PublishingLookupValidator : IFieldValidator<PublishingLookupState>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static PublishingLookupValidator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleFieldError> Evaluate(PublishingLookupState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        if (string.IsNullOrWhiteSpace(state.LookupId))
        {
            errors.Add(Blocker(PublishingLookupFieldKeys.LookupId, "publishing.lookupId.required", "Enter a publication id to review."));
        }
        else if (!IdentifierRule.IsValid(state.LookupId))
        {
            errors.Add(Error(
                PublishingLookupFieldKeys.LookupId,
                "publishing.lookupId.format",
                "Publication id may only contain letters, numbers, '-', '_', '.', and ':' with no spaces."));
        }

        // RepublishTitle is intentionally optional/free-form: no rule.

        return errors;
    }

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);

    private static ConsoleFieldError Error(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Error, message);
}
