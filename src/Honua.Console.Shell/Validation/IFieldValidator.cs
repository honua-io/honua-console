using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

public interface IFieldValidator<in TState>
{
    IReadOnlyList<ConsoleFieldError> Evaluate(TState state);
}
