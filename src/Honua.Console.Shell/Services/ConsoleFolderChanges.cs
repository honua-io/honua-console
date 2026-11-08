namespace Honua.Console.Shell.Services;

public sealed class ConsoleFolderChanges
{
    public event Action? Changed;
    public void Notify() => Changed?.Invoke();
}
