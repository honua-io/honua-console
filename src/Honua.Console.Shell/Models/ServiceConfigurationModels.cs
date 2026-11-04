namespace Honua.Console.Shell.Models;

public sealed record ServiceLayerEnableCommand
{
    public required string ConnectionId { get; init; }

    public required int LayerId { get; init; }

    public string? ServiceName { get; init; }

    public bool Enabled { get; init; }
}

public sealed record ServiceProtocolsCommand
{
    public required string ServiceName { get; init; }

    public required IReadOnlyList<string> EnabledProtocols { get; init; }
}

// Null fields are left unchanged on the server.
public sealed record ServiceAccessPolicyCommand
{
    public required string ServiceName { get; init; }

    public bool? AllowAnonymous { get; init; }

    public bool? AllowAnonymousWrite { get; init; }

    public IReadOnlyList<string>? AllowedRoles { get; init; }

    public IReadOnlyList<string>? AllowedWriteRoles { get; init; }
}

// Null fields are left unchanged. A 501 from the server is an unsupported result, not a success.
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

public sealed record ServiceConfigurationResult
{
    public bool Succeeded { get; init; }

    public required string State { get; init; }

    public string? Detail { get; init; }

    public int? LayerId { get; init; }

    public string? ServiceName { get; init; }

    public bool? Enabled { get; init; }

    public IReadOnlyList<string> EnabledProtocols { get; init; } = [];

    public IReadOnlyList<ServiceConfigurationFieldError> FieldErrors { get; init; } = [];

    public static ServiceConfigurationResult MissingBinding(string detail) => new()
    {
        Succeeded = false,
        State = "Missing binding",
        Detail = detail
    };
}

// When unbound, show the missing-binding detail instead of inventing settings.
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

    public ServiceMapServerSettingsView? MapServer { get; init; }

    public static ServiceSettingsView Unbound(string serviceName, string detail) => new()
    {
        Bound = false,
        ServiceName = serviceName,
        Detail = detail
    };
}

// Null means the server did not report that cap.
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

public sealed record ServiceConfigurationFieldError(
    string Code,
    string Message,
    string? Path = null,
    string? FieldId = null,
    string? Severity = null);
