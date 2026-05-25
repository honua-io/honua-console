using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace Honua.Console.Shell.Models;

/// <summary>
/// Console-owned transition view model for native Operate pages.
/// This is not a server wire contract; replace the data source with honua-sdk-dotnet clients
/// when the admin metadata projections land.
/// </summary>
public sealed record OperateTransitionWorkspace(
    IReadOnlyList<OperateConnectionSummary> Connections,
    IReadOnlyList<OperateResourceEditPreview> ResourceEdits,
    IReadOnlyList<OperateServiceDetail> Services,
    IReadOnlyList<OperateSettingsChange> SettingsChanges);

public sealed record OperateConnectionSummary(
    string Id,
    string Name,
    string Provider,
    string Target,
    string Principal,
    string Status,
    string LastTested,
    OperateConnectionDiagnostic? LastDiagnostic);

public sealed record OperateConnectionDiagnostic(
    string Outcome,
    string FailureCode,
    string Summary,
    IReadOnlyList<OperateDiagnosticSignal> Signals,
    IReadOnlyList<string> OperatorActions,
    IReadOnlyDictionary<string, string> Evidence)
{
    public OperateConnectionDiagnostic ToSafeDiagnostic() =>
        this with
        {
            Summary = OperateSecretRedactor.Redact(Summary),
            Signals = Signals
                .Select(signal => signal with
                {
                    Message = OperateSecretRedactor.Redact(signal.Message)
                })
                .ToArray(),
            OperatorActions = OperatorActions
                .Select(OperateSecretRedactor.Redact)
                .ToArray(),
            Evidence = new ReadOnlyDictionary<string, string>(
                Evidence.ToDictionary(
                    entry => entry.Key,
                    entry => OperateSecretRedactor.Redact(entry.Value),
                    StringComparer.Ordinal))
        };
}

public sealed record OperateDiagnosticSignal(
    string Check,
    string Severity,
    string Message);

public sealed record OperateResourceEditPreview(
    string ResourceId,
    string Name,
    string Source,
    string DraftChange,
    string ValidationState,
    OperateBlastRadius BlastRadius,
    IReadOnlyList<OperateValidationIssue> ValidationIssues,
    IReadOnlyList<string> EditTabs);

public sealed record OperateBlastRadius(
    IReadOnlyList<string> CatalogItems,
    IReadOnlyList<string> Services,
    IReadOnlyList<string> Layers,
    IReadOnlyList<string> SavedMaps,
    IReadOnlyList<string> ShareLinks,
    IReadOnlyList<string> GeneratedApps);

public sealed record OperateValidationIssue(
    string Severity,
    string Field,
    string Message,
    string OperatorResolution);

public sealed record OperateServiceDetail(
    string Name,
    string DisplayName,
    string ServiceType,
    string RuntimeStatus,
    string MetadataOwnership,
    IReadOnlyList<OperateServiceLayerProjection> Layers,
    IReadOnlyList<OperateRuntimeSetting> RuntimeSettings,
    IReadOnlyList<OperatePublicationSlot> PublicationSlots);

public sealed record OperateServiceLayerProjection(
    int LayerId,
    string Name,
    string Geometry,
    string CanonicalResourceId,
    string CanonicalResourceName);

public sealed record OperateRuntimeSetting(
    string Name,
    string Value,
    string ApplyScope,
    bool RequiresRestart);

public sealed record OperatePublicationSlot(
    string Name,
    string Url,
    string Status);

public sealed record OperateSettingsChange(
    string Id,
    string Category,
    string Name,
    string ProposedChange,
    string ApplyScope,
    bool RequiresRestart,
    string RestartRequirement,
    string PolicyState);

public static partial class OperateSecretRedactor
{
    private const string RedactedValue = "[redacted]";

    public static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var redacted = RawConnectionStringPattern().Replace(value, RedactedValue);
        redacted = NamedConnectionStringPattern().Replace(redacted, match => $"{match.Groups["name"].Value}={RedactedValue}");
        redacted = AuthorizationBearerPattern().Replace(redacted, match => $"{match.Groups["prefix"].Value}Bearer {RedactedValue}");
        redacted = BearerPattern().Replace(redacted, $"Bearer {RedactedValue}");
        redacted = SecretReferencePattern().Replace(redacted, match =>
        {
            var scope = match.Groups["scope"].Value;
            var identifier = match.Groups["identifier"].Value;
            return $"secret://{scope}/{identifier}/{RedactedValue}";
        });
        redacted = SensitiveKeyValuePattern().Replace(redacted, match => $"{match.Groups["name"].Value}={RedactedValue}");

        return redacted;
    }

    [GeneratedRegex(
        @"(?ix)
        \b
        (?:
            Server|Host|Data\ Source|Address|Addr|Network\ Address|Database|Initial\ Catalog
        )\s*=\s*[^;\r\n]+
        (?:
            ;
            [^;\r\n=]+\s*=\s*[^;\r\n]+
        ){2,}
        ;?",
        RegexOptions.CultureInvariant)]
    private static partial Regex RawConnectionStringPattern();

    [GeneratedRegex(
        @"(?ix)
        \b(?<name>connection(?:[_\s-]?string)?)\s*[:=]\s*
        (?:""[^""]*""|'[^']*'|[^\r\n]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex NamedConnectionStringPattern();

    [GeneratedRegex(
        @"(?i)(?<prefix>\bAuthorization\s*:\s*)Bearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationBearerPattern();

    [GeneratedRegex(
        @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(
        @"secret://(?<scope>[A-Za-z0-9_.-]+)/(?<identifier>[A-Za-z0-9_.-]+)(?:/[A-Za-z0-9_.:/@+-]+)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretReferencePattern();

    [GeneratedRegex(
        @"(?ix)
        \b(?<name>
            password|pwd|passcode|
            api[_\s-]?key|
            access[_\s-]?token|
            refresh[_\s-]?token|
            client[_\s-]?secret|
            shared[_\s-]?secret
        )\s*[:=]\s*
        (?:""[^""]*""|'[^']*'|[^;,\s]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveKeyValuePattern();
}
