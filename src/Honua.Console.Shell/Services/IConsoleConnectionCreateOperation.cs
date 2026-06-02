using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's connection-create OPERATION. Performs a REAL create of a data connection against
/// honua-server's admin endpoint (<c>POST /api/v1/admin/connections/</c>) and returns the resulting server
/// state, or an explicit failure carrying the rejection reason and any field-addressable validation errors.
///
/// Unlike the former Add Connection scaffolding (which rendered a static description with no form), this
/// abstraction persists the connection on the server. The live implementation is DI-gated on a configured
/// server base URL; when no server is configured the surface binds to
/// <see cref="UnsupportedConsoleConnectionCreateOperation"/>, which returns a missing-binding result and
/// performs no network call (Console Patterns Charter section 11 — never fabricate a create).
/// </summary>
public interface IConsoleConnectionCreateOperation
{
    Task<ConsoleConnectionCreateResult> CreateAsync(
        ConsoleConnectionCreateCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the draft connection's connectivity against honua-server WITHOUT persisting it
    /// (<c>POST /api/v1/admin/connections/test</c>), so the operator can prove the credentials/target work
    /// before creating the connection. Returns a missing-binding outcome (no network call) when no server is
    /// configured.
    /// </summary>
    Task<ConsoleConnectionTestOutcome> TestAsync(
        ConsoleConnectionCreateCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests an EXISTING connection's health against honua-server (<c>POST /api/v1/admin/connections/{id}/test</c>),
    /// which persists the resulting health status, so the connection detail page can run a test and reflect the
    /// refreshed health. Returns a missing-binding outcome (no network call) when no server is configured.
    /// </summary>
    Task<ConsoleConnectionTestOutcome> TestExistingAsync(
        string connectionId,
        CancellationToken cancellationToken = default);
}
