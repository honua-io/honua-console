using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads honua-server's deterministic ops-findings surface (group
/// <c>/api/v1/admin/observability</c>, operator-authorized with an explicit headless API-key
/// fallback, bare JSON — NO ApiResponse envelope) and proposes a finding's recommended action through the
/// existing operation-gateway approval flow. Findings are deterministic server output —
/// no model/LLM reasoning is involved (ADR-0028). Each call returns an
/// <see cref="OperateSectionResult{T}"/> whose status drives the shared
/// missing/forbidden/unsupported/unavailable surfaces. Per the Console Patterns Charter
/// section 11 the client never returns seeded data; with no environment bound every read
/// returns a missing-binding result.
/// </summary>
public interface IConsoleOpsFindingsClient
{
    /// <summary>Lists the active ops findings for the connected environment.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The findings list, or a non-allowed section status.</returns>
    Task<OperateSectionResult<OpsFindingsListResponse>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Proposes a finding's recommended action through the approval gateway. A 404
    /// (finding condition cleared, or no recommended action) maps to
    /// <see cref="OperateSectionStatus.Missing"/> so the page can refresh the list with an
    /// explanatory notice rather than surfacing a hard error.
    /// </summary>
    /// <param name="findingId">The deterministic finding identifier to propose.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The propose outcome, or a non-allowed section status.</returns>
    Task<OperateSectionResult<OpsFindingProposeResponse>> ProposeAsync(
        string findingId,
        CancellationToken cancellationToken = default);
}
