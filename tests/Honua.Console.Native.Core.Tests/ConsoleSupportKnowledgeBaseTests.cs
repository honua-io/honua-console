using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleSupportKnowledgeBaseTests
{
    private const string SampleKb =
        """
        {"id":"kb-faultcatalog-HONUA_GIS_CRS_MISMATCH","title":"Crs Mismatch (HONUA_GIS_CRS_MISMATCH)","symptoms":["layer overlay returns empty geometry because the inputs use different CRS values"],"remediation":["Inspect both CRS values, reproject the working layer to the target CRS, then rerun the overlay."],"fault_code":"HONUA_GIS_CRS_MISMATCH","scope":"guided-fix","tags":["gis"],"source":{"family":"corpus.faultcatalog","ref":"HONUA_GIS_CRS_MISMATCH"},"embedding_text":"Crs Mismatch\nSymptoms: layer overlay returns empty geometry because the inputs use different CRS values\nRemediation: reproject","embedding":null,"embedding_model":null}
        {"id":"kb-faultcatalog-HONUA_GIS_JOIN_CARDINALITY","title":"Join Cardinality (HONUA_GIS_JOIN_CARDINALITY)","symptoms":["spatial join creates duplicate parcel rows"],"remediation":["Choose one-to-one aggregation before joining."],"fault_code":"HONUA_GIS_JOIN_CARDINALITY","scope":"guided-fix","tags":["gis"],"source":{"family":"corpus.faultcatalog","ref":"HONUA_GIS_JOIN_CARDINALITY"},"embedding_text":"Join Cardinality\nSymptoms: spatial join creates duplicate parcel rows\nRemediation: aggregation","embedding":null,"embedding_model":null}
        """;

    [Fact]
    public void LoadsRecordsAndRanksLexicalMatchBestFirst()
    {
        var kb = LoadFrom(SampleKb);

        Assert.True(kb.IsLoaded);
        Assert.Equal(2, kb.RecordCount);

        var matches = kb.Search("spatial join duplicate rows");
        Assert.NotEmpty(matches);
        Assert.Equal("kb-faultcatalog-HONUA_GIS_JOIN_CARDINALITY", matches[0].Record.Id);
        Assert.True(matches[0].Score > 0);
    }

    [Fact]
    public void MatchesOnFaultCodeAndCrsTerms()
    {
        var kb = LoadFrom(SampleKb);

        var matches = kb.Search("overlay returns empty geometry CRS mismatch");

        Assert.Equal("kb-faultcatalog-HONUA_GIS_CRS_MISMATCH", matches[0].Record.Id);
    }

    [Fact]
    public void BlankOrNonMatchingQueryReturnsEmpty()
    {
        var kb = LoadFrom(SampleKb);

        Assert.Empty(kb.Search("   "));
        Assert.Empty(kb.Search("completely unrelated xyzzy"));
    }

    [Fact]
    public void MissingOverridePathFallsBackToBundledArtifactWithoutThrowing()
    {
        // A non-existent override path must not throw; it falls back to the
        // bundled SupportKb/support-kb.jsonl that ships next to the Shell
        // assembly (copied into the test output via the project reference).
        var kb = FileConsoleSupportKnowledgeBase.Load(Path.Combine(Path.GetTempPath(), $"no-such-kb-{Guid.NewGuid():N}.jsonl"));

        Assert.True(kb.IsLoaded);
        Assert.True(kb.RecordCount > 0);
        // The bundled GIS fault KB resolves a CRS-mismatch symptom.
        Assert.NotEmpty(kb.Search("overlay returns empty geometry crs mismatch"));
    }

    [Fact]
    public void EmptyOverrideFileDegradesToEmptyNotThrow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kb-empty-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, string.Empty);
        try
        {
            var kb = FileConsoleSupportKnowledgeBase.Load(path);
            Assert.False(kb.IsLoaded);
            Assert.Equal(0, kb.RecordCount);
            Assert.Empty(kb.Search("anything"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SelfServiceContextRendersTranscriptAndViewedKb()
    {
        var context = new SupportSelfServiceContext
        {
            Transcript =
            [
                new ChatCompletionMessage { Role = "user", Content = "overlay is empty" },
                new ChatCompletionMessage { Role = "assistant", Content = "Reproject the layer." }
            ],
            ViewedKb =
            [
                new SupportKbRecord { Id = "kb-1", Title = "Crs Mismatch", FaultCode = "HONUA_GIS_CRS_MISMATCH" }
            ],
            AssistantTurns = 1
        };

        Assert.True(context.HasActivity);
        var rendered = context.ToSymptomContext();
        Assert.Contains("Assistant transcript:", rendered);
        Assert.Contains("User: overlay is empty", rendered);
        Assert.Contains("Assistant: Reproject the layer.", rendered);
        Assert.Contains("KB articles viewed (1):", rendered);
        Assert.Contains("Crs Mismatch [HONUA_GIS_CRS_MISMATCH]", rendered);
    }

    [Fact]
    public void EmptySelfServiceContextRendersNothing()
    {
        var context = new SupportSelfServiceContext();

        Assert.False(context.HasActivity);
        Assert.Equal(string.Empty, context.ToSymptomContext());
    }

    private static FileConsoleSupportKnowledgeBase LoadFrom(string jsonl)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kb-test-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, jsonl);
        try
        {
            return FileConsoleSupportKnowledgeBase.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
