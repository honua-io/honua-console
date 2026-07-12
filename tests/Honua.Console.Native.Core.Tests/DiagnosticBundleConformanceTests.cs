using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Honua.Console.Shell.Diagnostics;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Executes the support-owned, language-neutral diagnostic-bundle conformance corpus
/// (honua-support#54/#57, mirrored read-only under <c>contracts/diagnostics/</c>) against the
/// Console validator, and pins the schema bytes to the published provenance so contract drift
/// fails CI with a clear provenance/hash error (honua-console#307).
/// </summary>
public sealed class DiagnosticBundleConformanceTests
{
    private const string ExpectedSchemaSha256 =
        "4dd7282d17bb417d56f1c3cfa243e03b612a401e5d22be766658849287e431a9";

    [Fact]
    public void PinnedSchema_MatchesPublishedProvenanceAndEmbeddedResource()
    {
        string kitRoot = DiagnosticKit.Root();
        byte[] schemaBytes = File.ReadAllBytes(Path.Combine(kitRoot, "diagnostic-bundle.v1.json"));
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(schemaBytes));

        // The pin itself is the contract: if the schema bytes or the pin drift apart, this fails.
        Assert.Equal(ExpectedSchemaSha256, sha256);

        using JsonDocument schema = JsonDocument.Parse(schemaBytes);
        using JsonDocument provenance =
            JsonDocument.Parse(File.ReadAllBytes(Path.Combine(kitRoot, "diagnostic-bundle.v1.provenance.json")));
        JsonElement published = provenance.RootElement;

        Assert.Equal("honua.public-schema-provenance.v1", published.GetProperty("schema").GetString());
        Assert.Equal("honua-io/honua-support", published.GetProperty("sourceRepository").GetString());
        Assert.Equal("schemas/diagnostic-bundle.v1.json", published.GetProperty("sourcePath").GetString());
        Assert.Equal(schemaBytes.LongLength, published.GetProperty("bytes").GetInt64());
        Assert.Equal(sha256, published.GetProperty("sha256").GetString());
        Assert.Equal(
            schema.RootElement.GetProperty("$id").GetString(),
            published.GetProperty("canonicalUrl").GetString());

        // The bytes embedded into Honua.Console.Shell must be identical to the pinned on-disk bytes,
        // so the validator enforces exactly the published contract.
        byte[] embeddedBytes = Encoding.UTF8.GetBytes(DiagnosticBundleSchema.CanonicalSchemaJson);
        Assert.Equal(schemaBytes, embeddedBytes);
    }

    [Fact]
    public void ConformanceManifest_PinsSchemaAndDeclaresEveryFixtureOnce()
    {
        string kitRoot = DiagnosticKit.Root();
        string suiteRoot = Path.Combine(kitRoot, "diagnostic-bundle.v1.conformance");
        string manifestPath = Path.Combine(suiteRoot, "manifest.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement root = manifest.RootElement;

        using JsonDocument schema =
            JsonDocument.Parse(File.ReadAllBytes(Path.Combine(kitRoot, "diagnostic-bundle.v1.json")));
        Assert.Equal(schema.RootElement.GetProperty("$id").GetString(), root.GetProperty("schemaId").GetString());
        Assert.Equal(ExpectedSchemaSha256, root.GetProperty("schemaSha256").GetString());

        HashSet<string> declaredFiles = new(StringComparer.Ordinal);
        foreach (JsonElement testCase in root.GetProperty("cases").EnumerateArray())
        {
            string relativePath = testCase.GetProperty("path").GetString()!;
            Assert.False(Path.IsPathRooted(relativePath));
            string fixturePath = Path.GetFullPath(Path.Combine(suiteRoot, relativePath));
            Assert.StartsWith(suiteRoot + Path.DirectorySeparatorChar, fixturePath, StringComparison.Ordinal);
            Assert.True(File.Exists(fixturePath), $"Conformance fixture '{relativePath}' does not exist.");
            Assert.True(declaredFiles.Add(fixturePath), $"Fixture '{relativePath}' declared more than once.");
        }

        HashSet<string> fixtureFiles = Directory
            .EnumerateFiles(suiteRoot, "*.json", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, manifestPath, StringComparison.Ordinal))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(
            fixtureFiles.SetEquals(declaredFiles),
            "Every conformance JSON fixture must be declared exactly once in manifest.json.");
    }

    [Fact]
    public void ConformanceCorpus_ValidCasesPass_InvalidCasesFailWithExpectedError()
    {
        string kitRoot = DiagnosticKit.Root();
        string suiteRoot = Path.Combine(kitRoot, "diagnostic-bundle.v1.conformance");
        using JsonDocument manifest =
            JsonDocument.Parse(File.ReadAllBytes(Path.Combine(suiteRoot, "manifest.json")));
        DiagnosticBundleSchema schema = new();

        int caseCount = 0;
        foreach (JsonElement testCase in manifest.RootElement.GetProperty("cases").EnumerateArray())
        {
            caseCount++;
            string id = testCase.GetProperty("id").GetString()!;
            string relativePath = testCase.GetProperty("path").GetString()!;
            bool expectedValid = testCase.GetProperty("valid").GetBoolean();

            using JsonDocument fixture =
                JsonDocument.Parse(File.ReadAllBytes(Path.Combine(suiteRoot, relativePath)));
            IReadOnlyList<string> errors = schema.Validate(fixture.RootElement);

            if (expectedValid)
            {
                Assert.True(errors.Count == 0, $"Case '{id}' should be valid but reported: {string.Join(" | ", errors)}");
            }
            else
            {
                Assert.NotEmpty(errors);
                string expectedError = testCase.GetProperty("expectedErrorContains").GetString()!;
                Assert.Contains(errors, error => error.Contains(expectedError, StringComparison.Ordinal));
            }
        }

        Assert.True(caseCount >= 7, "The conformance corpus regressed: fewer cases than expected.");
    }
}

/// <summary>Locates the pinned diagnostic-bundle kit under <c>contracts/diagnostics/</c>.</summary>
internal static class DiagnosticKit
{
    public static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "contracts", "diagnostics", "diagnostic-bundle.v1.json");
            if (File.Exists(candidate))
                return Path.Combine(directory.FullName, "contracts", "diagnostics");
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate contracts/diagnostics from the test output directory.");
    }
}
