using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

public static class ShareManageFieldKeys
{
    public const string ItemId = "share.itemId";
    public const string ExpiresAt = "share.expiresAt";
}

// A null expiry asks for a token that does not expire.
public sealed record ShareManageState(string? ItemId, DateTimeOffset? ExpiresAt, DateTimeOffset Now);

public sealed class ShareManageValidator : IFieldValidator<ShareManageState>
{
    public static ShareManageValidator Instance { get; } = new();

    public IReadOnlyList<ConsoleFieldError> Evaluate(ShareManageState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        if (string.IsNullOrWhiteSpace(state.ItemId))
        {
            errors.Add(Blocker(ShareManageFieldKeys.ItemId, "share.itemId.required", "Enter a content-item id to open."));
        }
        else if (!IdentifierRule.IsValid(state.ItemId))
        {
            errors.Add(Error(
                ShareManageFieldKeys.ItemId,
                "share.itemId.format",
                "Item id may only contain letters, numbers, '-', '_', '.', and ':' with no spaces."));
        }

        if (state.ExpiresAt is { } expires && !IsoDateRule.IsInFuture(expires, state.Now))
        {
            errors.Add(Error(
                ShareManageFieldKeys.ExpiresAt,
                "share.expiresAt.future",
                "Expiry must be in the future. Leave it blank for a token that never expires."));
        }

        return errors;
    }

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);

    private static ConsoleFieldError Error(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Error, message);
}
