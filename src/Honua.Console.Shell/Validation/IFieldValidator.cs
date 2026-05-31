using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// The pure, side-effect-free client validation pattern, mirroring the existing
/// <c>Studio*PublishEvaluator</c> evaluators: a validator examines a snapshot of editor state and
/// returns field-addressable findings without touching a server. Implementations are cheap enough to
/// run on every edit and again on demand. This Wave-0 base intentionally ships with no editor-specific
/// validators — only the contract and the shared rule helpers each editor's validator will compose
/// (Waves 1-6 add the concrete <c>Studio*Validator</c> types).
/// </summary>
/// <typeparam name="TState">The console-owned editor state record/class the validator evaluates.</typeparam>
public interface IFieldValidator<in TState>
{
    /// <summary>
    /// Evaluates <paramref name="state"/> and returns the findings keyed by
    /// <see cref="ConsoleFieldError.FieldKey"/>. Returns an empty list when the state is valid; never
    /// returns <see langword="null"/> and never throws for ordinary invalid input.
    /// </summary>
    IReadOnlyList<ConsoleFieldError> Evaluate(TState state);
}
