using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Web.Auth;

internal static class ConsoleServerProfileBinding
{
    public static string Capture(ConsoleEnvironmentProfile profile) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new ConsoleServerProfileBindingValue(
            profile.Id, profile.ServerBaseUri.AbsoluteUri, profile.EffectiveTenantId(), profile.Account.AccountId, profile.UpdatedAt),
            ConsoleServerProfileBindingJsonContext.Default.ConsoleServerProfileBindingValue)));
}

internal sealed record ConsoleServerProfileBindingValue(
    string ProfileId, string ServerBaseUri, string TenantId, string AccountId, DateTimeOffset UpdatedAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConsoleServerProfileBindingValue))]
internal sealed partial class ConsoleServerProfileBindingJsonContext : JsonSerializerContext;
