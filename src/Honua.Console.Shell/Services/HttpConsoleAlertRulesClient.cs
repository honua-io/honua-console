using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the alert-rule DEFINITION authoring surface to a real honua-server through
/// the SHIPPED alert-rule admin contract (honua-server#1169,
/// <c>/api/v{version}/admin/alerts/rules…</c>). Base address comes from the active
/// <see cref="ConsoleEnvironmentProfile"/>; the admin API key is sent as
/// <c>X-API-Key</c> (the alert admin group is admin-authorized), mirroring
/// <see cref="HttpConsoleGitOpsReleaseClient"/>.
///
/// The group wraps every payload in the <c>ApiResponse&lt;T&gt;</c> envelope
/// (<see cref="ConsoleApiEnvelope{T}"/>): each read/write unwraps <c>.data</c> and
/// treats <c>success:false</c> or a non-2xx status as the mapped failure state.
/// A single shared <see cref="HttpClient"/> is reused; each request builds an
/// absolute URI and attaches its own key. Deserialization is source-generated for
/// trim/AOT safety. Charter section 11 is preserved: no environment profile means
/// every call returns the missing-binding (Unavailable) result, never seeded rules.
/// </summary>
public sealed class HttpConsoleAlertRulesClient : IConsoleAlertRulesClient
{
    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to load alert rules.";

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly string? _adminApiKey;

    public HttpConsoleAlertRulesClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profileStore,
        string? adminApiKey = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
    }

    public Task<OperateSectionResult<AlertRuleResponse>> GetRuleAsync(
        long ruleId,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Get,
            AlertAdminRoutes.Rule(ruleId),
            content: null,
            OperateObservabilityJsonContext.Default.AlertRuleEnvelope,
            cancellationToken);

    public Task<OperateSectionResult<AlertRuleHealthResponse>> GetRuleHealthAsync(
        long ruleId,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Get,
            AlertAdminRoutes.RuleHealth(ruleId),
            content: null,
            OperateObservabilityJsonContext.Default.AlertRuleHealthEnvelope,
            cancellationToken);

    public Task<OperateSectionResult<AlertRuleTestResponse>> TestRuleAsync(
        AlertRuleRequest rule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var body = Serialize(
            new AlertRuleTestRequest { Rule = rule },
            AlertAdminJsonContext.Default.AlertRuleTestRequest);
        return SendAsync(
            HttpMethod.Post,
            AlertAdminRoutes.RulesTest,
            body,
            OperateObservabilityJsonContext.Default.AlertRuleTestEnvelope,
            cancellationToken);
    }

    public Task<OperateSectionResult<AlertRuleResponse>> SaveRuleAsync(
        long? ruleId,
        AlertRuleRequest rule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var body = Serialize(rule, AlertAdminJsonContext.Default.AlertRuleRequest);
        return ruleId is { } id
            ? SendAsync(
                HttpMethod.Put,
                AlertAdminRoutes.Rule(id),
                body,
                OperateObservabilityJsonContext.Default.AlertRuleEnvelope,
                cancellationToken)
            : SendAsync(
                HttpMethod.Post,
                AlertAdminRoutes.Rules,
                body,
                OperateObservabilityJsonContext.Default.AlertRuleEnvelope,
                cancellationToken);
    }

    public Task<OperateSectionResult<AlertRuleResponse>> SetRuleEnabledAsync(
        long ruleId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var body = Serialize(
            new AlertRuleEnabledRequest { Enabled = enabled },
            AlertAdminJsonContext.Default.AlertRuleEnabledRequest);
        return SendAsync(
            HttpMethod.Put,
            AlertAdminRoutes.RuleEnabled(ruleId),
            body,
            OperateObservabilityJsonContext.Default.AlertRuleEnvelope,
            cancellationToken);
    }

    private async Task<OperateSectionResult<T>> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        string? content,
        JsonTypeInfo<ConsoleApiEnvelope<T>> envelopeTypeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        using var request = new HttpRequestMessage(method, BuildUri(profile.ServerBaseUri, relativePath));
        if (!string.IsNullOrWhiteSpace(_adminApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _adminApiKey);
        }

        if (content is not null)
        {
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return OperateSectionResult<T>.Denied(
                    MapStatus(response.StatusCode),
                    await BuildErrorMessageAsync(response, cancellationToken).ConfigureAwait(false));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var envelope = await JsonSerializer
                .DeserializeAsync(stream, envelopeTypeInfo, cancellationToken)
                .ConfigureAwait(false);

            if (envelope is null)
            {
                return OperateSectionResult<T>.Denied(
                    OperateSectionStatus.Unavailable,
                    "The honua-server admin API returned an empty response.");
            }

            // ApiResponse<T> envelope: success:false (or a null data) on a 2xx is a
            // server-declared failure, not a fabricated success.
            if (!envelope.Success || envelope.Data is null)
            {
                return OperateSectionResult<T>.Denied(
                    OperateSectionStatus.Unavailable,
                    string.IsNullOrWhiteSpace(envelope.Message)
                        ? "The honua-server admin API reported the alert-rule request as unsuccessful."
                        : envelope.Message!);
            }

            return OperateSectionResult<T>.Allowed(envelope.Data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<T>.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-server admin API is unreachable or returned an unreadable response.");
        }
    }

    private static async Task<string> BuildErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var status = $"The honua-server admin API returned {(int)response.StatusCode} {response.ReasonPhrase}.";

        // A 400 carries the validation message in the ApiResponse envelope; surface
        // it so the editor shows the server's reason rather than a generic code.
        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            return status;
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var envelope = await JsonSerializer
                .DeserializeAsync(stream, OperateObservabilityJsonContext.Default.AlertRuleEnvelope, cancellationToken)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(envelope?.Message) ? status : envelope!.Message!;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            return status;
        }
    }

    private static string Serialize<T>(T value, JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Serialize(value, typeInfo);

    private static Uri BuildUri(Uri baseUri, string relativePath) =>
        ConsoleServerHttp.BuildUri(baseUri, relativePath);

    private static OperateSectionStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound => OperateSectionStatus.Missing,
        HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable
    };
}
