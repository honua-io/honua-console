namespace Honua.Console.Shell.Validation;

// Each editor keeps its own instance. A scoped service would share dirty state across the circuit.
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
