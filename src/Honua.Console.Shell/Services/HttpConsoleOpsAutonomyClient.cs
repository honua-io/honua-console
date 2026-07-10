using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live honua-server binding for graduated ops autonomy. Reads capability-detect the API
/// (404/501 means unsupported); interactive mutations use the strict operator-bearer seam
/// and never silently downgrade to the process-wide admin key.
/// </summary>
public sealed class HttpConsoleOpsAutonomyClient : IConsoleOpsAutonomyClient
{
    private const int AuditPageSize = 100;
    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to manage ops autonomy.";

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profiles;
    private readonly IConsoleAccountSessionStore _sessions;
    private readonly string? _adminApiKey;
    private readonly IConsoleOperatorBearerProvider _operatorBearerProvider;
    private readonly ConsoleServerCredentialMode _credentialMode;

    /// <summary>Initializes the live autonomy client.</summary>
    public HttpConsoleOpsAutonomyClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions,
        string? adminApiKey = null,
        IConsoleOperatorBearerProvider? operatorBearerProvider = null,
        ConsoleServerCredentialMode credentialMode = ConsoleServerCredentialMode.Interactive)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
        _operatorBearerProvider = operatorBearerProvider
            ?? new ConsoleOperatorBearerProvider(
                sessions,
                new UnavailableConsoleOperatorBearerExchange(),
                TimeProvider.System);
        _credentialMode = credentialMode;
    }

    /// <inheritdoc />
    public async Task<OperateSectionResult<OpsAutonomySnapshot>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<OpsAutonomySnapshot>.Denied(
                OperateSectionStatus.Unavailable,
                NoProfileMessage);
        }

        // Policies are the capability probe. Stop on an older server so the existing
        // propose-only findings flow remains untouched and we do not fan out dead reads.
        var policies = await GetAsync(
                profile,
                OpsAutonomyRoutes.Policies,
                OpsAutonomyJsonContext.Default.OpsAutonomyPolicyListResponse,
                cancellationToken)
            .ConfigureAwait(false);
        if (!policies.IsAllowed)
        {
            return OperateSectionResult<OpsAutonomySnapshot>.Denied(policies.Status, policies.Message);
        }

        var settingsTask = GetAsync(
            profile,
            OpsAutonomyRoutes.Settings,
            OpsAutonomyJsonContext.Default.OpsAutonomySettingsResponse,
            cancellationToken);
        // Read one bounded audit page and filter locally. Older server builds interpret a
        // type-only resourceRef as an exact type/id match after mapping and consequently
        // return no rows. A kind-only page works across both old and new server semantics.
        var auditTask = GetAuditAsync(profile, cancellationToken);

        await Task.WhenAll(settingsTask, auditTask).ConfigureAwait(false);
        var settings = await settingsTask.ConfigureAwait(false);
        if (!settings.IsAllowed)
        {
            return OperateSectionResult<OpsAutonomySnapshot>.Denied(settings.Status, settings.Message);
        }

        var audit = await auditTask.ConfigureAwait(false);
        var auditEntries = OpsAutonomyAuditMapper.Map(AllowedItems(audit));
        var auditPageMayBeTruncated = audit.IsAllowed
            && audit.Value!.Items.Count >= AuditPageSize;
        var auditPartial = !audit.IsAllowed
            || audit.Value?.PartialResult == true
            || auditPageMayBeTruncated;
        var auditMessage = BuildAuditMessage(audit, auditPageMayBeTruncated);

        return OperateSectionResult<OpsAutonomySnapshot>.Allowed(
            new OpsAutonomySnapshot(
                settings.Value!,
                policies.Value!.Policies,
                auditEntries,
                auditPartial,
                auditMessage),
            partialResult: auditPartial,
            message: auditMessage);
    }

    /// <inheritdoc />
    public async Task<OperateSectionResult<OpsAutonomyPolicyResponse>> SetPolicyModeAsync(
        string rule,
        string mode,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        if (!string.Equals(mode, "ProposeOnly", StringComparison.Ordinal)
            && !string.Equals(mode, "AutoApply", StringComparison.Ordinal))
        {
            throw new ArgumentException("Mode must be ProposeOnly or AutoApply.", nameof(mode));
        }

        var request = new OpsAutonomyPolicyUpdateRequest
        {
            Mode = mode,
            Reason = reason?.Trim() ?? string.Empty
        };
        return await PutAsync(
                OpsAutonomyRoutes.Policy(rule),
                request,
                OpsAutonomyJsonContext.Default.OpsAutonomyPolicyUpdateRequest,
                OpsAutonomyJsonContext.Default.OpsAutonomyPolicyResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperateSectionResult<OpsAutonomySettingsResponse>> SetKillSwitchAsync(
        bool enabled,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var request = new OpsAutonomySettingsUpdateRequest
        {
            KillSwitchEnabled = enabled,
            Reason = reason?.Trim() ?? string.Empty
        };
        return await PutAsync(
                OpsAutonomyRoutes.SettingsUpdate,
                request,
                OpsAutonomyJsonContext.Default.OpsAutonomySettingsUpdateRequest,
                OpsAutonomyJsonContext.Default.OpsAutonomySettingsResponse,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<OperateSectionResult<OperateEventPageResponse>> GetAuditAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken)
    {
        var query = new OperateEventQueryParameters
        {
            Kind = "Audit",
            PageSize = AuditPageSize
        };
        return GetAsync(
            profile,
            OperateAdminRoutes.Events + query.ToQueryString(),
            OperateObservabilityJsonContext.Default.OperateEventPageResponse,
            cancellationToken,
            unsupportedMessage: "The connected server does not expose the unified Operate audit feed; autonomy controls remain available, but action history cannot be shown.");
    }

    private async Task<OperateSectionResult<T>> GetAsync<T>(
        ConsoleEnvironmentProfile profile,
        string route,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken,
        string? unsupportedMessage = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            ConsoleServerHttp.BuildUri(profile.ServerBaseUri, route));
        await ConsoleServerHttp.AttachAuthenticationAsync(
                request,
                _sessions,
                profile,
                _adminApiKey,
                cancellationToken)
            .ConfigureAwait(false);

        return await SendAsync(request, typeInfo, cancellationToken, unsupportedMessage).ConfigureAwait(false);
    }

    private async Task<OperateSectionResult<TResponse>> PutAsync<TRequest, TResponse>(
        string route,
        TRequest body,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<TResponse>.Denied(
                OperateSectionStatus.Unavailable,
                NoProfileMessage);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            ConsoleServerHttp.BuildUri(profile.ServerBaseUri, route));
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, requestTypeInfo),
            Encoding.UTF8,
            "application/json");
        var authentication = await ConsoleServerHttp.AttachMutationAuthenticationAsync(
                request,
                _operatorBearerProvider,
                profile,
                _adminApiKey,
                _credentialMode,
                cancellationToken)
            .ConfigureAwait(false);
        if (!authentication.IsAuthenticated)
        {
            return OperateSectionResult<TResponse>.Denied(
                OperateSectionStatus.Forbidden,
                authentication.Message);
        }

        return await SendAsync(
                request,
                responseTypeInfo,
                cancellationToken,
                unsupportedMessage: "The connected server does not expose this ops-autonomy mutation endpoint; no local state was changed.")
            .ConfigureAwait(false);
    }

    private async Task<OperateSectionResult<T>> SendAsync<T>(
        HttpRequestMessage request,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken,
        string? unsupportedMessage = null)
    {
        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var status = MapStatus(response.StatusCode);
                return OperateSectionResult<T>.Denied(
                    status,
                    status == OperateSectionStatus.Unsupported
                        ? unsupportedMessage
                            ?? "The connected honua-server does not expose graduated ops autonomy; findings remain propose-only."
                        : $"The honua-server ops-autonomy API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var value = await JsonSerializer
                .DeserializeAsync(stream, typeInfo, cancellationToken)
                .ConfigureAwait(false);
            return value is null
                ? OperateSectionResult<T>.Denied(
                    OperateSectionStatus.Unavailable,
                    "The honua-server ops-autonomy API returned an empty response.")
                : OperateSectionResult<T>.Allowed(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<T>.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-server ops-autonomy API is unreachable or returned an unreadable response.");
        }
    }

    private static IEnumerable<OperateEventResponse> AllowedItems(
        OperateSectionResult<OperateEventPageResponse> result) =>
        result.IsAllowed ? result.Value!.Items : [];

    private static string BuildAuditMessage(
        OperateSectionResult<OperateEventPageResponse> audit,
        bool pageMayBeTruncated)
    {
        if (audit.IsAllowed && audit.Value?.PartialResult != true && !pageMayBeTruncated)
        {
            return string.Empty;
        }

        if (pageMayBeTruncated && audit.Value?.PartialResult != true)
        {
            return $"Only the newest {AuditPageSize} audit events were examined; older autonomy events may not be visible.";
        }

        if (!string.IsNullOrWhiteSpace(audit.Message))
        {
            return audit.Message;
        }

        var message = audit.Value?.SourceErrors is { Count: > 0 } errors
            ? "Autonomy audit is partial: " + string.Join(
                "; ",
                errors.OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => $"{item.Key}: {item.Value}"))
            : "The bounded audit page returned a partial result.";
        return pageMayBeTruncated
            ? $"{message} Only the newest {AuditPageSize} audit events were examined."
            : message;
    }

    private static OperateSectionStatus MapStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound or HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable
    };
}
