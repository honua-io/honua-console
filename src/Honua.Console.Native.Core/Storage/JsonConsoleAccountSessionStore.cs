using System.Text.Json;
using Honua.Console.Native.Core.Security;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Storage;

public sealed class JsonConsoleAccountSessionStore : IConsoleAccountSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly INativeSecretStore _secrets;

    public JsonConsoleAccountSessionStore(INativeSecretStore secrets)
    {
        _secrets = secrets;
    }

    public async Task<ConsoleAccountSession?> GetSessionAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var json = await _secrets.GetSecretAsync(GetSessionKey(profileId), cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ConsoleAccountSession>(json, JsonOptions);
    }

    public async Task SaveSessionAsync(ConsoleAccountSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.ProfileId);

        var json = JsonSerializer.Serialize(session, JsonOptions);
        await _secrets.SetSecretAsync(GetSessionKey(session.ProfileId), json, cancellationToken).ConfigureAwait(false);
    }

    public Task ClearSessionAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return _secrets.RemoveSecretAsync(GetSessionKey(profileId), cancellationToken).AsTask();
    }

    private static string GetSessionKey(string profileId) =>
        $"honua.console.native.account-session.{profileId}.v1";
}
