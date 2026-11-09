using Honua.Console.Contracts;
using Honua.Sdk.Admin.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Services;

public sealed record ConsolePublishAuthority(IConsoleAuthorityBindingValidator? Validator, string? Binding, string? Error)
{
    public static async Task<ConsolePublishAuthority> CaptureAsync(IServiceProvider services, OptionalString folderId, string? folderAuthority)
    {
        var validator = services.GetService<IConsoleAuthorityBindingValidator>();
        var binding = validator is null ? null : await validator.CaptureAsync();
        var error = folderId.IsSpecified && (folderAuthority is null || binding != folderAuthority)
            ? "Your user or environment changed. Refresh Folders and review the original publishing target."
            : null;
        return new(validator, binding, error);
    }
}
