using System.Reflection;
using System.Text;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Captured Console-side context for a support ticket. The customer never copies
/// log dumps; the Console auto-attaches who/where/which-build/what-route plus the
/// connected server's recent errors, and supplies the instance URL + an optional
/// scoped key so honua-support's auto-bundle can pull live telemetry.
/// </summary>
public sealed record ConsoleSupportContext
{
    public string? UserId { get; init; }

    public string? UserDisplayName { get; init; }

    public string? TenantId { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];

    public string EnvironmentKind { get; init; } = string.Empty;

    public string? EnvironmentName { get; init; }

    public string? ServerUri { get; init; }

    public string AppVersion { get; init; } = string.Empty;

    public string Commit { get; init; } = string.Empty;

    public string CurrentRoute { get; init; } = string.Empty;

    public IReadOnlyList<OperateRecentError> RecentErrors { get; init; } = [];

    public string? RecentErrorsNote { get; init; }

    /// <summary>
    /// Renders the captured context as a human-readable block appended to the
    /// ticket symptoms so the honua-support operator (and its triage) get full
    /// context inline even before the auto-bundle telemetry contract lands
    /// (honua-support#20).
    /// </summary>
    public string ToSymptomContext()
    {
        var builder = new StringBuilder();
        builder.AppendLine("--- Console-captured context ---");
        AppendIfPresent(builder, "User", string.IsNullOrWhiteSpace(UserDisplayName) ? UserId : $"{UserDisplayName} ({UserId})");
        AppendIfPresent(builder, "Tenant", TenantId);
        if (Roles.Count > 0)
        {
            builder.AppendLine($"Roles: {string.Join(", ", Roles)}");
        }

        AppendIfPresent(builder, "Environment", string.IsNullOrWhiteSpace(EnvironmentName)
            ? EnvironmentKind
            : $"{EnvironmentName} ({EnvironmentKind})");
        AppendIfPresent(builder, "Server", ServerUri);
        AppendIfPresent(builder, "Console version", AppVersion);
        AppendIfPresent(builder, "Console commit", Commit);
        AppendIfPresent(builder, "Route", CurrentRoute);

        if (RecentErrors.Count > 0)
        {
            builder.AppendLine($"Recent server errors ({RecentErrors.Count}):");
            foreach (var error in RecentErrors)
            {
                builder.AppendLine(
                    $"  - {error.Timestamp:u} HTTP {error.StatusCode} {error.Path} [{error.CorrelationId}] {error.Message}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(RecentErrorsNote))
        {
            builder.AppendLine($"Recent server errors: {RecentErrorsNote}");
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendIfPresent(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"{label}: {value.Trim()}");
        }
    }
}

/// <summary>
/// Collects <see cref="ConsoleSupportContext"/> with zero user effort from the
/// account session, active environment profile, Console build metadata, the
/// current route, and the connected server's recent-error buffer.
/// </summary>
public sealed class ConsoleSupportContextBuilder
{
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly IConsoleAccountSessionStore _sessionStore;
    private readonly IConsoleOperateObservabilityClient _observabilityClient;

    public ConsoleSupportContextBuilder(
        IConsoleEnvironmentProfileStore profileStore,
        IConsoleAccountSessionStore sessionStore,
        IConsoleOperateObservabilityClient observabilityClient)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _observabilityClient = observabilityClient ?? throw new ArgumentNullException(nameof(observabilityClient));
    }

    public async Task<ConsoleSupportContext> CaptureAsync(
        string currentRoute,
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        ConsoleAccountSession? session = null;
        if (profile is not null && profile.Account.AuthMode != ConsoleAccountAuthMode.Anonymous)
        {
            session = await _sessionStore.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<OperateRecentError> recentErrors = [];
        string? recentErrorsNote = null;
        if (profile is not null)
        {
            var errors = await _observabilityClient.GetRecentErrorsAsync(10, cancellationToken).ConfigureAwait(false);
            if (errors.IsAllowed)
            {
                recentErrors = errors.Value!;
            }
            else
            {
                recentErrorsNote = string.IsNullOrWhiteSpace(errors.Message)
                    ? OperateSectionPresentation.FallbackMessage(errors.Status)
                    : errors.Message;
            }
        }

        return new ConsoleSupportContext
        {
            UserId = session?.AccountId ?? profile?.Account.AccountId,
            UserDisplayName = session?.DisplayName ?? profile?.Account.DisplayName,
            TenantId = session?.TenantId ?? profile?.TenantId,
            Roles = session?.RoleIds ?? [],
            EnvironmentKind = profile?.EnvironmentKind ?? string.Empty,
            EnvironmentName = profile?.DisplayName,
            ServerUri = profile?.ServerBaseUri.AbsoluteUri,
            AppVersion = ResolveVersion(),
            Commit = ResolveCommit(),
            CurrentRoute = currentRoute,
            RecentErrors = recentErrors,
            RecentErrorsNote = recentErrorsNote
        };
    }

    // The browser host stamps build metadata into Honua.Console.Web's
    // ConsoleBuildMetadata, but the Shell cannot reference the Web host, so the
    // version/commit are read the same way ConsoleBuildMetadata.Create() does:
    // the informational version attribute and the HONUA_CONSOLE_COMMIT_SHA env var.
    private static string ResolveVersion() =>
        typeof(ConsoleSupportContextBuilder).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+', 2)[0] ?? "unknown";

    private static string ResolveCommit()
    {
        var sha = Environment.GetEnvironmentVariable("HONUA_CONSOLE_COMMIT_SHA");
        if (string.IsNullOrWhiteSpace(sha))
        {
            return "unknown";
        }

        return sha.Length > 12 ? sha[..12] : sha;
    }
}
