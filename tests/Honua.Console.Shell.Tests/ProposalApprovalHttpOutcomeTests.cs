using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed class ProposalApprovalHttpOutcomeTests
{
    [Theory]
    [InlineData("The catalog changed. Submit a fresh proposal.")]
    [InlineData("The requester's authorization changed. Submit a fresh proposal.")]
    public async Task SuccessfulHttpDecisionWithFailedExecutionShowsServerReason(string reason)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var handler = new ApprovalHandler(reason);
        var profiles = new InMemoryConsoleEnvironmentProfileStore([new ConsoleEnvironmentProfile
        {
            Id = "live",
            DisplayName = "Live",
            ServerBaseUri = new Uri("https://server.example"),
            Account = new ConsoleAccountBinding { AuthMode = ConsoleAccountAuthMode.AccountRbac },
        }], activeProfileId: "live");
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession { ProfileId = "live", AccessToken = "test-session" });
        context.Services.AddSingleton<IConsoleProposalsClient>(new HttpConsoleProposalsClient(
            new HttpClient(handler), profiles, sessions));
        ConsoleProposalDetail? actioned = null;
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1")
            .Add(component => component.OnActioned, detail => actioned = detail));

        await panel.Find("[data-proposal-approve]").ClickAsync(new());

        var message = panel.Find("[data-proposal-action-message]");
        Assert.Contains(reason, message.TextContent, StringComparison.Ordinal);
        Assert.Contains("change was not applied", message.TextContent, StringComparison.Ordinal);
        Assert.Contains("console-state-danger", message.ClassList);
        Assert.Equal(ConsoleProposalStatus.Failed, actioned!.Status);
        Assert.Equal("operation-1", actioned.ExecutionOperationId);
        Assert.Equal(reason, actioned.ResolutionReason);
        Assert.Equal(["GET", "GET", "POST"], handler.Methods);
        Assert.Equal("review-capability", handler.ApprovalCapability);
    }

    private sealed class ApprovalHandler(string reason) : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];
        public string? ApprovalCapability { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method.Method);
            var proposal = new
            {
                proposalId = "proposal-1", version = 3, planHash = "reviewed-plan", kind = "MetadataRelease",
                status = request.Method == HttpMethod.Post ? "Failed" : "AwaitingApproval",
                summary = "Create folder", blockingReasons = new[] { reason },
                resolutionReason = request.Method == HttpMethod.Post ? reason : null,
                executionOperationId = request.Method == HttpMethod.Post ? "operation-1" : null,
                createdAt = "2026-11-17T00:00:00Z", updatedAt = "2026-11-17T00:01:00Z",
            };
            object body = proposal;
            if (request.RequestUri!.AbsolutePath.EndsWith("/approval-review", StringComparison.Ordinal))
            {
                body = new { proposal, review = new { proposalId = "proposal-1", proposalVersion = 3,
                    planHash = "reviewed-plan", capability = "review-capability" } };
            }
            if (request.Method == HttpMethod.Post)
            {
                ApprovalCapability = Assert.Single(request.Headers.GetValues("X-Honua-Approval-Capability"));
                Assert.Null(request.Headers.Authorization);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            });
        }
    }
}
