using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests.Components;

public sealed class JobDiagnosticsTimelineArtifactsTests : ConsoleComponentTestBase
{
    private static ConsoleJobDiagnosticsResponse Diagnostics(ConsoleJobDiagnosticArtifacts? artifacts, params string[] notes) => new()
    {
        Schema = OperateObservabilityMapper.JobDiagnosticsSchema,
        JobId = "gp-job-1",
        Lifecycle = new ConsoleJobDiagnosticLifecycle { Phase = "succeeded", TimingSource = "measured" },
        Artifacts = artifacts,
        Evidence = new ConsoleJobDiagnosticEvidence { Notes = notes },
    };

    [Fact]
    public void Expired_artifacts_are_named_with_their_state_and_recovery_note()
    {
        var view = OperateObservabilityMapper.MapJobDiagnostics(Diagnostics(
            new ConsoleJobDiagnosticArtifacts
            {
                State = "unavailable",
                ExpiredCount = 1,
                Items = [new ConsoleJobArtifact { ArtifactId = "a-1", Label = "buffer.geojson", Availability = "Expired" }],
            },
            "artifacts: expired results cannot be fetched; resubmit the job to produce them again"));

        var cut = Render<JobDiagnosticsTimeline>(p => p.Add(c => c.View, view));

        Assert.Equal("unavailable", cut.Find("[data-job-diagnostics-artifacts]").GetAttribute("data-job-diagnostics-artifacts"));
        Assert.Equal("0 available, 1 expired, 0 unavailable.", cut.Find("[data-job-diagnostics-artifacts-summary]").TextContent);
        var item = cut.Find("[data-artifact-availability='Expired']");
        Assert.Contains("buffer.geojson", item.TextContent, StringComparison.Ordinal);
        Assert.Contains("resubmit", cut.Find("[data-job-diagnostics-notes]").TextContent, StringComparison.Ordinal);
        Assert.Equal("job-diagnostics-artifacts-heading",
            cut.Find("[data-job-diagnostics-artifacts]").GetAttribute("aria-labelledby"));
        Assert.Equal("H4", cut.Find("#job-diagnostics-artifacts-heading").TagName);
        Assert.Equal("Recovery notes", cut.Find("[data-job-diagnostics-notes]").GetAttribute("aria-label"));
    }

    [Fact]
    public void A_server_without_the_artifacts_section_is_never_shown_as_complete()
    {
        var cut = Render<JobDiagnosticsTimeline>(p => p.Add(c => c.View,
            OperateObservabilityMapper.MapJobDiagnostics(Diagnostics(null))));

        Assert.Equal("unsupported", cut.Find("[data-job-diagnostics-artifacts]").GetAttribute("data-job-diagnostics-artifacts"));
        Assert.Contains("does not report", cut.Find("[data-job-diagnostics-artifacts-summary]").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-job-diagnostics-artifact-items]"));
        Assert.Empty(cut.FindAll("[data-job-diagnostics-notes]"));
    }

    [Fact]
    public void Complete_artifacts_say_so_with_the_count()
    {
        var cut = Render<JobDiagnosticsTimeline>(p => p.Add(c => c.View,
            OperateObservabilityMapper.MapJobDiagnostics(Diagnostics(new ConsoleJobDiagnosticArtifacts
            {
                State = "complete",
                AvailableCount = 2,
                Items =
                [
                    new ConsoleJobArtifact { ArtifactId = "a-1", Availability = "Available" },
                    new ConsoleJobArtifact { ArtifactId = "a-2", Availability = "Redacted" },
                ],
            }))));

        Assert.Equal("All 2 artifact(s) are accounted for; 1 redacted for your access.",
            cut.Find("[data-job-diagnostics-artifacts-summary]").TextContent);
        Assert.Equal(2, cut.FindAll("[data-job-diagnostics-artifact-items] li").Count);
    }

    [Fact]
    public void Reading_the_next_event_page_keeps_the_newest_artifact_state()
    {
        var first = OperateObservabilityMapper.MapJobDiagnostics(Diagnostics(new ConsoleJobDiagnosticArtifacts
        {
            State = "complete",
            AvailableCount = 1,
            Items = [new ConsoleJobArtifact { ArtifactId = "a-1", Availability = "Available" }],
        }));
        var later = OperateObservabilityMapper.MapJobDiagnostics(Diagnostics(new ConsoleJobDiagnosticArtifacts
        {
            State = "unavailable",
            ExpiredCount = 1,
            Items = [new ConsoleJobArtifact { ArtifactId = "a-1", Availability = "Expired" }],
        }, "artifacts: expired results cannot be fetched; resubmit the job to produce them again"));

        var merged = first.AppendEvents(later);

        Assert.Equal("unavailable", merged.Artifacts.State);
        Assert.Equal(1, merged.Artifacts.ExpiredCount);
        Assert.Single(merged.EvidenceNotes);
    }

    [Theory]
    [InlineData("none", 0, "The job recorded no artifacts.")]
    [InlineData("complete", 0, "0 available, 0 expired, 0 unavailable.")]
    [InlineData("complete", 1, "All 1 artifact(s) are available.")]
    [InlineData("partial", 1, "1 available, 0 expired, 1 unavailable.")]
    public void Summaries_never_claim_success_without_artifacts(string state, int available, string expected)
    {
        ConsoleJobArtifact[] items = state switch
        {
            "complete" when available == 1 => [new ConsoleJobArtifact { ArtifactId = "a-1", Availability = "Available" }],
            "partial" =>
            [
                new ConsoleJobArtifact { ArtifactId = "a-1", Availability = "Available" },
                new ConsoleJobArtifact { ArtifactId = "a-2", Availability = "Missing" },
            ],
            _ => [],
        };
        var cut = Render<JobDiagnosticsTimeline>(p => p.Add(c => c.View,
            OperateObservabilityMapper.MapJobDiagnostics(Diagnostics(new ConsoleJobDiagnosticArtifacts
            {
                State = state,
                AvailableCount = available,
                UnavailableCount = state == "partial" ? 1 : 0,
                Items = items,
            }))));

        Assert.Equal(expected, cut.Find("[data-job-diagnostics-artifacts-summary]").TextContent);
    }

    [Fact]
    public void Holding_events_keeps_the_held_page_evidence_together()
    {
        var held = OperateObservabilityMapper.MapJobDiagnostics(Diagnostics(new ConsoleJobDiagnosticArtifacts
        {
            State = "complete",
            AvailableCount = 1,
            Items = [new ConsoleJobArtifact { ArtifactId = "a-1", Availability = "Available" }],
        }));
        var fresh = OperateObservabilityMapper.MapJobDiagnostics(Diagnostics(new ConsoleJobDiagnosticArtifacts
        {
            State = "unavailable",
            ExpiredCount = 1,
            Items = [new ConsoleJobArtifact { ArtifactId = "a-1", Availability = "Expired" }],
        }, "artifacts: expired results cannot be fetched; resubmit the job to produce them again"));

        var view = fresh.HoldingEventsFrom(held);

        Assert.Equal("complete", view.Artifacts.State);
        Assert.Empty(view.EvidenceNotes);
    }

    [Fact]
    public void Unnamed_artifacts_get_a_readable_label()
    {
        var view = OperateObservabilityMapper.MapJobDiagnostics(Diagnostics(new ConsoleJobDiagnosticArtifacts
        {
            State = "unavailable",
            ExpiredCount = 1,
            Items = [new ConsoleJobArtifact { Availability = "Expired" }],
        }));

        Assert.Equal("(unnamed artifact)", view.Artifacts.Items[0].Label);
    }
}
