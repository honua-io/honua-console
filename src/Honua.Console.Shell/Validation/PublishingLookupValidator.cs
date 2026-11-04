using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

public static class PublishingLookupFieldKeys
{
    public const string LookupId = "publishing.lookupId";
    public const string RepublishTitle = "publishing.republishTitle";
}

public sealed record PublishingLookupState(string? LookupId, string? RepublishTitle);

public sealed class PublishingLookupValidator : IFieldValidator<PublishingLookupState>
{
    public static PublishingLookupValidator Instance { get; } = new();

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
