using System.Net;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed partial class ConsoleOperateObservabilityClientTests
{
    [Fact]
    public async Task GetJobActionsReadsTheServerActionGateAndKeepsTheDisabledReason()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/admin/jobs/gp-run/actions" => JsonResponse(
                new ConsoleJobActionsResponse
                {
                    JobId = "gp-run",
                    Actions =
                    [
                        new ConsoleJobActionDescriptor
                        {
                            Name = "cancel",
                            Method = "POST",
                            Href = "/api/v1/admin/jobs/gp-run/cancel",
                            Allowed = false,
                            DisabledReason = "cancellation requested; waiting for the worker to stop"
                        }
                    ]
                },
                OperateObservabilityJsonContext.Default.ConsoleJobActionsResponse),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler);

        var result = await client.GetJobActionsAsync("gp-run");

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var cancel = Assert.Single(result.Value!);
        Assert.Equal("cancel", cancel.Name);
        Assert.False(cancel.IsAllowed);
        Assert.Equal("cancellation requested; waiting for the worker to stop", cancel.Reason);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task GetJobActionsReportsAnUnreadableGateAsDenied()
    {
        var client = CreateClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var result = await client.GetJobActionsAsync("gp-run");

        Assert.False(result.IsAllowed);
        Assert.Null(result.Value);
    }
}
