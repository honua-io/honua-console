namespace Honua.Console.Shell.Models;

/// <summary>
/// Field-state vocabulary shared across every Studio detail surface (design-handoff
/// console-canvas/field-state.jsx). Operators learn the visual language once: who owns a value and
/// whether it is editable. Each state maps to a labelled pill rendered by
/// <c>Honua.Console.Shell.Components.FieldStateRow</c>.
/// </summary>
public enum FieldState
{
    /// <summary>Operator-entered value (✏️ "input"). The default for plain authoring inputs.</summary>
    Input,

    /// <summary>Server-discovered value that the operator may override (🔍 "auto").</summary>
    Discovered,

    /// <summary>Value derived from other fields, not directly editable (🧮 "derived").</summary>
    Calculated,

    /// <summary>System-assigned identity/lifecycle value, read-only (⚙️ "system").</summary>
    System,

    /// <summary>Admin / server-config value gated to elevated roles (🔒 "admin only").</summary>
    Admin,
}
