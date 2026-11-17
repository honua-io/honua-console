using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

// Wire contract of the honua-server XLSForm import and export routes.
public sealed class FormXlsFormHttpClientTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    private const string RejectedWorkbook = """
        {"type":"about:blank","title":"Bad Request","status":400,
         "detail":"The workbook is not an XLSForm survey a Honua form package can represent.",
         "diagnostics":[{"sheet":"survey","row":4,"column":"type","message":"Question type 'range' is not supported."}]}
        """;

    [Fact]
    public async Task Import_PostsTheWorkbookAsMultipartFileWithTheTargetLayer()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Created, """{"formId":"hydrants","version":1,"status":"draft"}"""));
        var client = CreateClient(handler);

        var result = await client.ImportAsync(Upload("inspections & co", "hydrants.xlsx", [80, 75]));

        Assert.Null(result.Issue);
        Assert.Equal(("hydrants", 1), (result.Data!.Version!.FormId, result.Data.Version.Version));
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/v1/admin/forms/packages/import?serviceId=inspections%20%26%20co&layerId=7", handler.PathAndQuery);
        Assert.Equal("multipart/form-data", handler.ContentType);
        Assert.Contains("name=file", handler.Body, StringComparison.Ordinal);
        Assert.Contains("filename=hydrants.xlsx", handler.Body, StringComparison.Ordinal);
        Assert.Contains(HonuaFormXlsFormHttpClient.WorkbookContentType, handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_RejectedWorkbook_ReturnsEveryDiagnosticWithTheServerDetail()
    {
        var client = CreateClient(new RecordingHandler(_ => Json(HttpStatusCode.BadRequest, RejectedWorkbook)));

        var result = await client.ImportAsync(Upload("inspections", "bad.xlsx", [1]));

        Assert.Equal("Rejected", result.Issue!.State);
        Assert.Equal("The workbook is not an XLSForm survey a Honua form package can represent.", result.Issue.Detail);
        Assert.Null(result.Data!.Version);
        var diagnostic = Assert.Single(result.Data.Diagnostics);
        Assert.Equal(("survey", 4, "type"), (diagnostic.Sheet, diagnostic.Row, diagnostic.Column));
    }

    [Fact]
    public async Task Import_RouteMissingOnServer_ReportsUnsupportedWithoutDiagnostics()
    {
        var client = CreateClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await client.ImportAsync(Upload("inspections", "a.xlsx", [1]));

        Assert.Equal(("Unsupported", 404), (result.Issue!.State, result.Issue.StatusCode));
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Export_ReturnsTheWorkbookBytesAndServerFileName()
    {
        var handler = new RecordingHandler(_ =>
        {
            var content = new ByteArrayContent([9, 8, 7]);
            content.Headers.ContentType = new MediaTypeHeaderValue(HonuaFormXlsFormHttpClient.WorkbookContentType);
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "form 1-v2.xlsx", FileNameStar = "form 1-v2.xlsx" };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var client = CreateClient(handler);

        var result = await client.ExportAsync("form 1");

        Assert.Null(result.Issue);
        Assert.Equal("/api/v1/admin/forms/packages/form%201/export", handler.PathAndQuery);
        var workbook = result.Data!.Workbook!;
        Assert.Equal(("form 1-v2.xlsx", HonuaFormXlsFormHttpClient.WorkbookContentType), (workbook.FileName, workbook.ContentType));
        Assert.Equal(new byte[] { 9, 8, 7 }, workbook.Content);
    }

    [Fact]
    public async Task Export_UnrepresentablePackage_ReturnsDiagnostics()
    {
        var body = RejectedWorkbook.Replace("\"status\":400", "\"status\":422", StringComparison.Ordinal);
        var client = CreateClient(new RecordingHandler(_ => Json(HttpStatusCode.UnprocessableEntity, body)));

        var result = await client.ExportAsync("form-1");

        Assert.Equal(("Rejected", 422), (result.Issue!.State, result.Issue.StatusCode));
        Assert.Null(result.Data!.Workbook);
        Assert.Single(result.Data.Diagnostics);
    }

    private static HonuaFormXlsFormUpload Upload(string serviceId, string fileName, byte[] content) =>
        new() { ServiceId = serviceId, LayerId = 7, FileName = fileName, Content = content };

    private static HonuaFormXlsFormHttpClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = BaseAddress }, new HonuaFormPackageClientOptions(BaseAddress));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public string? PathAndQuery { get; private set; }

        public string? ContentType { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            PathAndQuery = request.RequestUri!.PathAndQuery;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}
