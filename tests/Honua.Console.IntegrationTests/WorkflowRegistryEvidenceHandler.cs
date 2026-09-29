using System.Text.Json;

namespace Honua.Console.IntegrationTests;

/// <summary>Retains only registry node identity and runtime-kind wire values, without credentials or schemas.</summary>
internal sealed class WorkflowRegistryEvidenceHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var directory = Environment.GetEnvironmentVariable("HONUA_CONSOLE_SERVER_LOG_DIR");
        if (string.IsNullOrWhiteSpace(directory) || request.Method != HttpMethod.Get
            || request.RequestUri?.AbsolutePath.TrimEnd('/') != "/api/v1/console/workflow-node-registry"
            || !response.IsSuccessStatusCode)
        {
            return response;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var nodes = document.RootElement.GetProperty("data").GetProperty("nodes").EnumerateArray()
            .Select((node, index) => new
            {
                index,
                nodeTypeId = node.GetProperty("nodeTypeId").Clone(),
                runtimeKind = node.GetProperty("runtimeKind").Clone()
            }).ToArray();
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, $"workflow-registry-{Guid.NewGuid():N}.json"),
            JsonSerializer.Serialize(new { path = request.RequestUri.AbsolutePath, status = (int)response.StatusCode, nodes }),
            cancellationToken).ConfigureAwait(false);
        return response;
    }
}
