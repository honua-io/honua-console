using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the Share manage surface (<c>ShareManagePage</c>).
/// </summary>
public static class ShareManageFieldKeys
{
    public const string ItemId = "share.itemId";
    public const string ExpiresAt = "share.expiresAt";
}

/// <summary>
/// Console-owned snapshot of the Share manage form the client validator evaluates: the content-item id the
/// operator is opening and the optional public-link / embed token expiry the operator is minting. <see cref="Now"/>
/// is injected so the future-date rule stays pure and unit-testable.
/// </summary>
/// <param name="ItemId">The content-item identifier (<c>item-...</c>).</param>
/// <param name="ExpiresAt">The optional token expiry (datetime-local, interpreted as UTC). Null means "never".</param>
/// <param name="Now">The current instant the expiry is compared against.</param>
public sealed record ShareManageState(string? ItemId, DateTimeOffset? ExpiresAt, DateTimeOffset Now);

/// <summary>
/// Pure client-side validator for the Share manage surface: the content-item id must be a plausible identifier,
/// and (when supplied) the token expiry must be in the future. The expiry is optional — a blank value mints a
/// non-expiring token, which is valid. Mirrors the <see cref="StudioMapValidator"/> pattern; keyed by
/// <see cref="ShareManageFieldKeys"/>. Console-only — Share has no server body-validator (catalog).
/// </summary>
public sealed class ShareManageValidator : IFieldValidator<ShareManageState>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static ShareManageValidator Instance { get; } = new();

    /// <inheritdoc />
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
