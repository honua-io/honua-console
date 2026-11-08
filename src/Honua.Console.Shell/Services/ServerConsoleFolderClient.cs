using System.Net;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Sdk.Admin;
using Honua.Sdk.Admin.Exceptions;
using Honua.Sdk.Admin.Models;

namespace Honua.Console.Shell.Services;

public sealed class ServerConsoleFolderClient(HttpClient http, IConsoleAuthorityBindingValidator authority) : IConsoleFolderClient, IDisposable
{
    private const string AuthorityChanged = "Your user or environment changed. Refresh Folders and review the target before trying again.";

    public async Task<OperateSectionResult<ConsoleFolderCatalog>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var binding = await authority.CaptureAsync(cancellationToken);
        if (binding is null) return OperateSectionResult<ConsoleFolderCatalog>.Denied(OperateSectionStatus.Forbidden, "Sign in to the selected environment to manage Folders.");
        try
        {
            using var transport = BoundTransport(binding);
            var (folders, etag) = await new HonuaAdminClient(transport).ListFoldersAsync(cancellationToken: cancellationToken);
            if (!await authority.IsCurrentAsync(binding, cancellationToken))
                return OperateSectionResult<ConsoleFolderCatalog>.Denied(OperateSectionStatus.Unavailable, AuthorityChanged);
            return OperateSectionResult<ConsoleFolderCatalog>.Allowed(new(folders, etag, binding));
        }
        catch (HonuaAdminApiException exception)
        {
            return OperateSectionResult<ConsoleFolderCatalog>.Denied(ReadStatus(exception.StatusCode), Reason(exception));
        }
        catch (Exception exception) when (exception is HttpRequestException or HonuaAdminOperationException)
        {
            return OperateSectionResult<ConsoleFolderCatalog>.Denied(OperateSectionStatus.Unavailable, "Folders could not be read. Check the environment and refresh.");
        }
    }

    public Task<ConsoleFolderMutationResult> CreateAsync(ConsoleFolderCatalog catalog, CreateFolderRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(catalog, async (client, options) => { await client.CreateFolderAsync(request, options, cancellationToken); }, cancellationToken);

    public Task<ConsoleFolderMutationResult> UpdateAsync(ConsoleFolderCatalog catalog, string folderId, UpdateFolderRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(catalog, async (client, options) => { await client.UpdateFolderAsync(folderId, request, options, cancellationToken); }, cancellationToken);

    public Task<ConsoleFolderMutationResult> DeleteAsync(ConsoleFolderCatalog catalog, string folderId, CancellationToken cancellationToken = default) =>
        MutateAsync(catalog, async (client, options) => { await client.DeleteFolderAsync(folderId, options, cancellationToken); }, cancellationToken);

    public Task<ConsoleFolderMutationResult> MoveAsync(ConsoleFolderCatalog catalog, string serviceName, MoveServiceRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(catalog, async (client, options) => { await client.MoveServiceAsync(serviceName, request, options, cancellationToken); }, cancellationToken);

    private async Task<ConsoleFolderMutationResult> MutateAsync(ConsoleFolderCatalog catalog,
        Func<IHonuaAdminFoldersClient, FolderMutationOptions, Task> mutate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(catalog.ETag))
            return new() { State = "Snapshot required", Detail = "The server did not return a catalog ETag. Refresh Folders before editing." };
        if (!await authority.IsCurrentAsync(catalog.AuthorityBinding, cancellationToken))
            return new() { State = "Target changed", Detail = AuthorityChanged };
        try
        {
            using var transport = BoundTransport(catalog.AuthorityBinding);
            await mutate(new HonuaAdminClient(transport), new() { IfMatch = catalog.ETag });
            if (!await authority.IsCurrentAsync(catalog.AuthorityBinding, cancellationToken))
                return new() { State = "Outcome unverified", Detail = AuthorityChanged + " Read the original environment to confirm the outcome; do not repeat the request." };
            return new() { Succeeded = true, State = "Saved", Detail = "The server saved the Folder change. Refresh to read the current catalog." };
        }
        catch (HonuaAdminApiException exception)
        {
            if (!await authority.IsCurrentAsync(catalog.AuthorityBinding, CancellationToken.None))
                return new() { State = ConsoleWriteOutcome.UnverifiedState, Detail = ConsoleWriteOutcome.UnverifiedDetail };
            return new() { State = MutationState(exception.StatusCode), Detail = Reason(exception) };
        }
        catch (Exception exception) when (exception is HttpRequestException or HonuaAdminOperationException or OperationCanceledException or JsonException)
        {
            return new() { State = "Outcome unverified", Detail = "The response was not confirmed. Read the original environment to check the outcome before making another change." };
        }
    }

    private HttpClient BoundTransport(string binding) => new(new BoundSdkHandler(http, binding)) { BaseAddress = http.BaseAddress };

    private static string Reason(HonuaAdminApiException exception) => string.Join(Environment.NewLine,
        new[] { exception.ProblemDetails?.Detail ?? exception.ProblemDetails?.Title ?? exception.Message }
            .Concat(exception.FailureReceipt?.FieldErrors.Select(error => error.Message) ?? [])
            .Where(message => !string.IsNullOrWhiteSpace(message)).Distinct());

    private static OperateSectionStatus ReadStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound or HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable
    };

    private static string MutationState(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Sign-in required",
        HttpStatusCode.Forbidden => "Forbidden",
        HttpStatusCode.Conflict => "Conflict",
        HttpStatusCode.PreconditionFailed => "Snapshot changed",
        HttpStatusCode.PreconditionRequired => "Snapshot required",
        HttpStatusCode.NotFound => "Missing",
        HttpStatusCode.BadRequest => "Invalid request",
        _ => "Unavailable"
    };

    public void Dispose() => http.Dispose();

    private sealed class BoundSdkHandler(HttpClient target, string binding) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // HttpClient marks a request as sent; forward a separate message through the existing human credential chain.
            using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri) { Content = request.Content };
            foreach (var header in request.Headers) forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            forwarded.Options.Set(ConsoleAuthorityRequestOptions.Expected, binding);
            var response = await target.SendAsync(forwarded, cancellationToken);
            if (ConsoleWriteOutcome.IsUnverified(forwarded))
            {
                response.Dispose();
                throw new HttpRequestException(ConsoleWriteOutcome.UnverifiedDetail);
            }
            return response;
        }
    }
}
