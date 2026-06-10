namespace Honua.Console.Shell.Models;

/// <summary>
/// Operator intent to enable or disable an already-published service layer: which connection + layer (and
/// optional target service) to toggle, and the desired enabled state. Mirrors the inputs the Operate
/// layers surface collects (Wave 5, plan §3 Family A).
/// </summary>
public sealed record ServiceLayerEnableCommand
{
    public required string ConnectionId { get; init; }

    public required int LayerId { get; init; }

    public string? ServiceName { get; init; }

    public bool Enabled { get; init; }
}

/// <summary>
/// Operator intent to change the set of protocols a service exposes (e.g. restrict a service to
/// FeatureServer only, or re-enable additional protocols). Wave 5 service-configuration operation.
/// </summary>
public sealed record ServiceProtocolsCommand
{
    public required string ServiceName { get; init; }

    public required IReadOnlyList<string> EnabledProtocols { get; init; }
}

/// <summary>
/// Operator intent to change a service's access policy (anonymous read/write + allowed roles). Null fields
/// are left unchanged server-side. Wave 5 service-configuration operation.
/// </summary>
public sealed record ServiceAccessPolicyCommand
{
    public required string ServiceName { get; init; }

    public bool? AllowAnonymous { get; init; }

    public bool? AllowAnonymousWrite { get; init; }

    public IReadOnlyList<string>? AllowedRoles { get; init; }

    public IReadOnlyList<string>? AllowedWriteRoles { get; init; }
}

/// <summary>
/// Operator intent to change a service's MapServer render settings (max/default image size, DPI, default
/// image format, default transparency, per-layer feature cap). Null fields are left unchanged server-side.
/// Closes Bucket 3-A #5 of the metadata-UI gap analysis. The server PUT may answer 501 (a V2 gap), in which
/// case the operation surfaces an honest "Unsupported" result rather than a fabricated success.
/// </summary>
public sealed record ServiceMapServerSettingsCommand
{
    public required string ServiceName { get; init; }

    public int? MaxImageWidth { get; init; }

    public int? MaxImageHeight { get; init; }

    public int? DefaultImageWidth { get; init; }

    public int? DefaultImageHeight { get; init; }

    public int? DefaultDpi { get; init; }

    public string? DefaultFormat { get; init; }

    public bool? DefaultTransparent { get; init; }

    public int? MaxFeaturesPerLayer { get; init; }
}

/// <summary>
/// Operator intent to change a service's settings caps — the request/result size limits the server enforces
/// (max/default record counts, per-layer feature cap, query timeout, edit-transaction cap, payload size,
/// supported output formats + default, default tile-matrix set, attachment support + size cap). Null fields
/// are left unchanged server-side; the server rejects negative caps. The server PUT may answer 501 on a build
/// that has not landed the write path, in which case the operation surfaces an honest "Unsupported" result
/// rather than a fabricated success.
/// </summary>
public sealed record ServiceSettingsCapsCommand
{
    public required string ServiceName { get; init; }

    public int? MaxRecordCount { get; init; }

    public int? DefaultRecordCount { get; init; }

    public int? MaxFeaturesPerLayer { get; init; }

    public int? QueryTimeoutMs { get; init; }

    public int? MaxEditsPerTransaction { get; init; }

    public long? MaxPayloadBytes { get; init; }

    public IReadOnlyList<string>? SupportedFormats { get; init; }

    public string? DefaultFormat { get; init; }

    public string? DefaultTileMatrixSet { get; init; }

    public bool? SupportsAttachments { get; init; }

    public long? MaxAttachmentSizeBytes { get; init; }
}

/// <summary>
/// Outcome of a service-configuration operation (layer enable/disable or service settings change). On
/// success, <see cref="Succeeded"/> is <c>true</c> and the projection fields carry the post-change server
/// state the operation read back. On failure (missing binding, validation rejection, transport error)
/// <see cref="Succeeded"/> is <c>false</c> and <see cref="State"/> carries the neutral state vocabulary
/// token. Never fabricates success — a missing server binding yields an explicit missing-binding result.
/// </summary>
public sealed record ServiceConfigurationResult
{
    public bool Succeeded { get; init; }

    /// <summary>State vocabulary token (e.g. "Enabled", "Disabled", "Updated", "Missing binding", "Rejected", "Unavailable").</summary>
    public required string State { get; init; }

    public string? Detail { get; init; }

    /// <summary>The affected layer id (for layer enable/disable operations).</summary>
    public int? LayerId { get; init; }

    /// <summary>The affected service name.</summary>
    public string? ServiceName { get; init; }

    /// <summary>The layer's enabled state read back after a toggle operation.</summary>
    public bool? Enabled { get; init; }

    /// <summary>The service's enabled protocols read back after a settings change.</summary>
    public IReadOnlyList<string> EnabledProtocols { get; init; } = [];

    /// <summary>
    /// Field-addressable validation errors surfaced when the server rejected the configuration change with
    /// the shared field-level validation contract (RFC-7807 ProblemDetails <c>errors[]</c>). Empty for
    /// non-validation failures. Console clients bind these onto the offending inputs.
    /// </summary>
    public IReadOnlyList<ServiceConfigurationFieldError> FieldErrors { get; init; } = [];

    public static ServiceConfigurationResult MissingBinding(string detail) => new()
    {
        Succeeded = false,
        State = "Missing binding",
        Detail = detail
    };
}

/// <summary>
/// Current server-read configuration of a service: its enabled + available protocols and access policy.
/// <see cref="Bound"/> is false (with <see cref="Detail"/> explaining why) when no server is configured or
/// the settings could not be read — the surface then shows a missing-binding state instead of fabricating one.
/// </summary>
public sealed record ServiceSettingsView
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public required string ServiceName { get; init; }

    public IReadOnlyList<string> EnabledProtocols { get; init; } = [];

    public IReadOnlyList<string> AvailableProtocols { get; init; } = [];

    public bool AllowAnonymous { get; init; }

    public bool AllowAnonymousWrite { get; init; }

    public IReadOnlyList<string> AllowedRoles { get; init; } = [];

    public IReadOnlyList<string> AllowedWriteRoles { get; init; } = [];

    /// <summary>
    /// The service's current MapServer render settings as read back from the server's settings projection, or
    /// <c>null</c> when the server did not return a MapServer block. Pre-populates the MapServer editor.
    /// </summary>
    public ServiceMapServerSettingsView? MapServer { get; init; }

    /// <summary>
    /// The service's current settings caps as read back from the server settings projection, or <c>null</c>
    /// when the server did not return a caps block. Pre-populates the settings-caps editor.
    /// </summary>
    public ServiceSettingsCapsView? SettingsCaps { get; init; }

    public static ServiceSettingsView Unbound(string serviceName, string detail) => new()
    {
        Bound = false,
        ServiceName = serviceName,
        Detail = detail
    };
}

/// <summary>
/// A service's current MapServer render settings, projected from the server settings read-back so the
/// MapServer editor can pre-populate its inputs. All fields are nullable — the server only reports the caps
/// it actually has configured.
/// </summary>
public sealed record ServiceMapServerSettingsView
{
    public int? MaxImageWidth { get; init; }

    public int? MaxImageHeight { get; init; }

    public int? DefaultImageWidth { get; init; }

    public int? DefaultImageHeight { get; init; }

    public int? DefaultDpi { get; init; }

    public string? DefaultFormat { get; init; }

    public bool? DefaultTransparent { get; init; }

    public int? MaxFeaturesPerLayer { get; init; }
}

/// <summary>
/// A service's current settings caps, projected from the server settings read-back so the settings-caps
/// editor can pre-populate its inputs. All fields are nullable — the server only reports the caps it actually
/// has configured.
/// </summary>
public sealed record ServiceSettingsCapsView
{
    public int? MaxRecordCount { get; init; }

    public int? DefaultRecordCount { get; init; }

    public int? MaxFeaturesPerLayer { get; init; }

    public int? QueryTimeoutMs { get; init; }

    public int? MaxEditsPerTransaction { get; init; }

    public long? MaxPayloadBytes { get; init; }

    public IReadOnlyList<string> SupportedFormats { get; init; } = [];

    public string? DefaultFormat { get; init; }

    public string? DefaultTileMatrixSet { get; init; }

    public bool? SupportsAttachments { get; init; }

    public long? MaxAttachmentSizeBytes { get; init; }
}

/// <summary>A field-addressable validation error surfaced by a service-configuration operation.</summary>
public sealed record ServiceConfigurationFieldError(
    string Code,
    string Message,
    string? Path = null,
    string? FieldId = null,
    string? Severity = null);
