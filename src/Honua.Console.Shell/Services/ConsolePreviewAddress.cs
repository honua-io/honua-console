namespace Honua.Console.Shell.Services;

// Browser previews on the static host call the configured Honua API. The legacy host keeps its proxy routes.
public interface IConsolePreviewAddress
{
    string? LayerStyle(string? environmentId, int layerId);
}

public sealed class ConfiguredApiPreviewAddress(Uri apiBase) : IConsolePreviewAddress
{
    public string? LayerStyle(string? environmentId, int layerId)
    {
        if (string.IsNullOrWhiteSpace(environmentId)
            || !apiBase.IsAbsoluteUri
            || apiBase.UserInfo.Length != 0
            || apiBase.Query.Length != 0
            || apiBase.Fragment.Length != 0
            || !apiBase.AbsolutePath.EndsWith('/'))
        {
            return null;
        }

        return new Uri(apiBase, FormattableString.Invariant($"api/styles/{layerId}.json")).AbsoluteUri;
    }
}
