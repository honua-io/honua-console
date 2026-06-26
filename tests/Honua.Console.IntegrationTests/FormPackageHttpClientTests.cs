using System.Net;
using System.Net.Http.Json;
using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for <see cref="HonuaFormPackageHttpClient"/> transport semantics. Caller-requested
/// cancellation must propagate (it cancels the calling operation rather than masquerading as an Unavailable
/// endpoint issue the caller would mistake for a server failure), while an HttpClient timeout still surfaces
/// as Unavailable. Mirrors the cancellation contract the adjacent Studio package lifecycle client guarantees.
/// </summary>
public sealed class FormPackageHttpClientTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task ListPackages_WhenCallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var client = CreateClient(new BlockUntilCancelledHandler());

        // The caller's token is already cancelled, so the call must throw rather than swallow the
        // cancellation into an Unavailable result the caller would read as a transport failure.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ListPackagesAsync(cts.Token));
    }

    [Fact]
    public async Task ListPackages_OnHttpClientTimeout_ReturnsUnavailable()
    {
        using var client = CreateClient(new BlockUntilCancelledHandler(), TimeSpan.FromMilliseconds(50));

        // The HttpClient timeout fires (not the caller's token, which is None), which is a transport failure,
        // so it must map to Unavailable instead of propagating as a cancellation the caller never requested.
        var result = await client.ListPackagesAsync(CancellationToken.None);

        Assert.NotNull(result.Issue);
        Assert.Equal("Unavailable", result.Issue!.State);
    }

    [Fact]
    public async Task Publish_WhenServerReturnsValidationFailure_PreservesValidationBody()
    {
        var validation = new HonuaFormPackageValidationResult
        {
            IsValid = false,
            Issues =
            [
                new HonuaFormValidationIssue
                {
                    Code = "target.missing",
                    Severity = "error",
                    FieldId = "asset_id",
                    Message = "Target field asset_id is missing."
                }
            ]
        };
        using var client = CreateClient(new StaticResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(validation)
        }));

        var result = await client.PublishAsync("form-1", 2);

        Assert.NotNull(result.Issue);
        Assert.Equal("Rejected", result.Issue!.State);
        Assert.Equal(400, result.Issue.StatusCode);
        Assert.NotNull(result.Data?.Validation);
        Assert.False(result.Data!.Validation!.IsValid);
        var issue = Assert.Single(result.Data.Validation.Issues);
        Assert.Equal("target.missing", issue.Code);
        Assert.Equal("asset_id", issue.FieldId);
    }

    [Fact]
    public async Task ImportXlsForm_PostsWorkbook_ToImportEndpoint_AndReadsResult()
    {
        HttpRequestMessage? captured = null;
        var result = new HonuaFormXlsFormImportResult
        {
            Title = "Survey",
            FieldCount = 1,
            Package = new HonuaFormPackageDocument { Title = "Survey" }
        };
        using var client = CreateClient(new StaticResponseHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
        }));

        var response = await client.ImportXlsFormAsync(new ImportXlsFormRequest
        {
            FileName = "survey.xlsx",
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Content = Convert.ToBase64String([1, 2, 3])
        });

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/api/v1/admin/forms/packages/import-xlsform", captured.RequestUri!.AbsolutePath);
        Assert.Null(response.Issue);
        Assert.Equal("Survey", response.Data!.Title);
    }

    [Fact]
    public async Task ImportXlsForm_WhenServerLacksContract_MapsNotFoundToUnsupported()
    {
        using var client = CreateClient(new StaticResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var response = await client.ImportXlsFormAsync(new ImportXlsFormRequest { FileName = "f.xlsx", Content = "AQ==" });

        Assert.NotNull(response.Issue);
        Assert.Equal("Unsupported", response.Issue!.State);
        Assert.Equal(404, response.Issue.StatusCode);
    }

    private static HonuaFormPackageHttpClient CreateClient(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        if (timeout is { } value)
        {
            httpClient.Timeout = value;
        }

        return new HonuaFormPackageHttpClient(httpClient, new HonuaFormPackageClientOptions(BaseAddress));
    }

    private sealed class BlockUntilCancelledHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Never completes on its own: the request ends only when the linked token (caller cancel or the
            // HttpClient timeout) fires, exactly like a real in-flight request being cancelled.
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StaticResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responseFactory(request));
    }
}
