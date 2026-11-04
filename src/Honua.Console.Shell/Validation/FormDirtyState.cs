namespace Honua.Console.Shell.Validation;

public sealed class FormDirtyState
{
    public event Action? Changed;

    public bool IsDirty { get; private set; }

    public void MarkDirty()
    {
        if (IsDirty)
        {
            return;
        }

        IsDirty = true;
        Changed?.Invoke();
    }

    public void MarkClean()
    {
        if (!IsDirty)
        {
            return;
        }

        IsDirty = false;
        Changed?.Invoke();
    }
}
