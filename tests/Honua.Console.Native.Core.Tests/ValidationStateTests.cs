using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free coverage for the per-editor <see cref="ValidationState"/> container: it must merge the
/// client-computed and server-returned channels keyed by FieldKey, keep them independently replaceable,
/// and answer per-field and summary queries.
/// </summary>
public sealed class ValidationStateTests
{
    private static ConsoleFieldError Client(string field, ConsoleValidationSeverity severity = ConsoleValidationSeverity.Error) =>
        new(field, "client.rule", severity, $"client {field}");

    private static ConsoleFieldError Server(string field, ConsoleValidationSeverity severity = ConsoleValidationSeverity.Blocker) =>
        new(field, "server.rule", severity, $"server {field}", "/" + field);

    [Fact]
    public void Empty_HasNoErrors()
    {
        var state = new ValidationState();

        Assert.False(state.HasErrors);
        Assert.False(state.HasBlockingErrors);
        Assert.Empty(state.All);
        Assert.Empty(state.Errors("anything"));
    }

    [Fact]
    public void HasBlockingClientErrors_IgnoresServerChannel()
    {
        var state = new ValidationState();
        // A stale server blocker must NOT count toward the client-blocking save gate.
        state.SetServerErrors([Server("form.serviceId")]);

        Assert.True(state.HasBlockingErrors);
        Assert.False(state.HasBlockingClientErrors);

        // A client blocker does flip it; clearing the client channel clears it again.
        state.SetClientErrors([Client("form.title", ConsoleValidationSeverity.Blocker)]);
        Assert.True(state.HasBlockingClientErrors);

        state.SetClientErrors([Client("form.title", ConsoleValidationSeverity.Warning)]);
        Assert.False(state.HasBlockingClientErrors);
    }

    [Fact]
    public void MergesClientAndServerKeyedByFieldKey()
    {
        var state = new ValidationState();
        state.SetClientErrors([Client("map.title"), Client("map.initialExtent")]);
        state.SetServerErrors([Server("map.initialExtent"), Server("map.basemap")]);

        Assert.True(state.HasErrors);
        Assert.Equal(4, state.All.Count);

        // map.initialExtent has BOTH a client and a server finding.
        var extentErrors = state.Errors("map.initialExtent");
        Assert.Equal(2, extentErrors.Count);
        Assert.Contains(extentErrors, error => error.Code == "client.rule");
        Assert.Contains(extentErrors, error => error.Code == "server.rule");

        // title only client, basemap only server.
        Assert.Single(state.Errors("map.title"));
        Assert.Single(state.Errors("map.basemap"));
        Assert.Empty(state.Errors("map.unknown"));
    }

    [Fact]
    public void Errors_AreOrderedMostSevereFirst()
    {
        var state = new ValidationState();
        state.SetClientErrors([new ConsoleFieldError("f", "c", ConsoleValidationSeverity.Info, "info")]);
        state.SetServerErrors([new ConsoleFieldError("f", "s", ConsoleValidationSeverity.Blocker, "blocker")]);

        var errors = state.Errors("f");
        Assert.Equal(ConsoleValidationSeverity.Blocker, errors[0].Severity);
        Assert.Equal(ConsoleValidationSeverity.Info, errors[1].Severity);
    }

    [Fact]
    public void SetClientErrors_DoesNotClobberServerChannel()
    {
        var state = new ValidationState();
        state.SetServerErrors([Server("f")]);

        // Re-running the cheap client evaluator must not wipe the authoritative server findings.
        state.SetClientErrors([Client("f")]);
        Assert.Equal(2, state.Errors("f").Count);

        // Replacing client again leaves server intact.
        state.SetClientErrors([]);
        Assert.Single(state.Errors("f"));
        Assert.Equal("server.rule", state.Errors("f")[0].Code);
    }

    [Fact]
    public void HasBlockingErrors_OnlyTrueForErrorOrBlocker()
    {
        var state = new ValidationState();
        state.SetClientErrors([Client("f", ConsoleValidationSeverity.Warning)]);
        Assert.True(state.HasErrors);
        Assert.False(state.HasBlockingErrors);

        state.SetClientErrors([Client("f", ConsoleValidationSeverity.Error)]);
        Assert.True(state.HasBlockingErrors);
    }

    [Fact]
    public void Clear_RemovesBothChannels()
    {
        var state = new ValidationState();
        state.SetClientErrors([Client("a")]);
        state.SetServerErrors([Server("b")]);

        state.Clear();

        Assert.False(state.HasErrors);
        Assert.Empty(state.All);
    }

    [Fact]
    public void Summary_IsSeverityOrdered()
    {
        var state = new ValidationState();
        state.SetClientErrors(
        [
            new ConsoleFieldError("a", "c", ConsoleValidationSeverity.Info, "info"),
            new ConsoleFieldError("b", "c", ConsoleValidationSeverity.Error, "error"),
        ]);
        state.SetServerErrors([new ConsoleFieldError("c", "s", ConsoleValidationSeverity.Warning, "warn")]);

        var summary = state.Summary;
        Assert.Equal(ConsoleValidationSeverity.Error, summary[0].Severity);
        Assert.Equal(ConsoleValidationSeverity.Warning, summary[1].Severity);
        Assert.Equal(ConsoleValidationSeverity.Info, summary[2].Severity);
    }
}
