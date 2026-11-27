using Microsoft.JSInterop;

namespace Honua.Console.Shell.Interop;

internal static class ConsoleJsInterop
{
    public static bool IsBenignFailure(Exception exception) =>
        exception is JSException
            or JSDisconnectedException
            or InvalidOperationException
            or OperationCanceledException;
}
