namespace Honua.Console.Shell.Validation;

/// <summary>
/// A simple per-editor dirty-tracking holder backing the unsaved-changes guard. The editor calls
/// <see cref="MarkDirty"/> on an <c>@bind</c> change and <see cref="MarkClean"/> on a successful
/// save/load; the guard reads <see cref="IsDirty"/> to decide whether to warn on navigation. This is
/// the deliberately-simple manual-flag approach (the catalog notes a snapshot-hash is an alternative,
/// but given the nested mutable editor state a manual flag is the least error-prone). Registered scoped
/// so each Blazor circuit / editor instance gets its own holder.
/// </summary>
public sealed class FormDirtyState
{
    /// <summary>Raised whenever <see cref="IsDirty"/> changes, so a guard can re-evaluate.</summary>
    public event Action? Changed;

    /// <summary>True when the editor holds unsaved changes.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Flags unsaved changes. Idempotent; only raises <see cref="Changed"/> on a transition.</summary>
    public void MarkDirty()
    {
        if (IsDirty)
        {
            return;
        }

        IsDirty = true;
        Changed?.Invoke();
    }

    /// <summary>Clears the dirty flag (e.g. after a successful save or a fresh load). Idempotent.</summary>
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
