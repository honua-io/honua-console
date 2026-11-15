namespace Honua.Console.Shell.Services;

// Browser previews on the static host call the configured Honua API. The legacy host keeps its proxy routes.
public interface IConsolePreviewAddress
{
    string? LayerStyle(string? environmentId, string publicationId);
}

public sealed class ConfiguredApiPreviewAddress(Uri apiBase) : IConsolePreviewAddress
{
    public string? LayerStyle(string? environmentId, string publicationId)
    {
        if (string.IsNullOrWhiteSpace(publicationId)
            || string.IsNullOrWhiteSpace(environmentId)
            || !apiBase.IsAbsoluteUri
            || apiBase.UserInfo.Length != 0
            || apiBase.Query.Length != 0
            || apiBase.Fragment.Length != 0
            || !apiBase.AbsolutePath.EndsWith('/'))
        {
            return null;
        }

        return new Uri(apiBase, $"api/styles/{Uri.EscapeDataString(publicationId)}.json").AbsoluteUri;
    }
}
