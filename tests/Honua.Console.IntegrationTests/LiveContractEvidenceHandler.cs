namespace Honua.Console.IntegrationTests;

/// <summary>Retains synthetic draft payloads for diagnosing live server/SDK contract drift.</summary>
internal sealed class LiveContractEvidenceHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var directory = Environment.GetEnvironmentVariable("HONUA_CONSOLE_SERVER_LOG_DIR");
        if (!string.IsNullOrWhiteSpace(directory)
            && request.Method == HttpMethod.Post
            && request.RequestUri?.AbsolutePath.TrimEnd('/') is "/api/v1/studio/package-drafts" or "/api/v1/analysis/content")
        {
            // These routes contain only this suite's generated authoring fixtures. Never record
            // headers, connection credentials, login requests, or arbitrary server responses.
            var requestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, $"authoring-contract-{Guid.NewGuid():N}.json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    path = request.RequestUri.AbsolutePath,
                    status = (int)response.StatusCode,
                    requestBody,
                    responseBody
                }), cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}
