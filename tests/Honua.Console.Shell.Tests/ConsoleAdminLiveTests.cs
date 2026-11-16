using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Channels;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honua.Console.Shell.Tests;

public sealed class ConsoleAdminLiveTests
{
    [Fact]
    public async Task Frames_HandleFragmentedUtf8CrLfCommentsAndMultilineData()
    {
        using var stream = new FragmentedStream(Encoding.UTF8.GetBytes(
            ": heartbeat\r\nid: admin:17\r\nevent: ProposalPending\r\ndata: café\r\ndata: next\r\n\r\n" +
            "event: reset\rdata: {\"head\":18}\r\rid: admin:19\ndata: incomplete"));
        var frames = new List<ConsoleAdminLiveFrame>();
        await foreach (var frame in ConsoleAdminLiveFrame.ReadAsync(stream, CancellationToken.None)) frames.Add(frame);
        Assert.Collection(frames,
            frame => Assert.Equal(new ConsoleAdminLiveFrame("ProposalPending", 17, "café\nnext"), frame),
            frame => Assert.Equal(new ConsoleAdminLiveFrame("reset", null, "{\"head\":18}"), frame));
    }

    [Theory]
    [InlineData("admin:-1")]
    [InlineData("admin:+1")]
    [InlineData("17")]
    [InlineData("admin:9223372036854775808")]
    public async Task Frames_RejectInvalidCursor(string cursor)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"id: {cursor}\ndata: {{}}\n\n"));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in ConsoleAdminLiveFrame.ReadAsync(stream, CancellationToken.None)) { }
        });
    }

    [Fact]
    public async Task Frames_BoundAnUnterminatedLine()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data:" + new string('x', 262145)));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in ConsoleAdminLiveFrame.ReadAsync(stream, CancellationToken.None)) { }
        });
    }

    [Fact]
    public async Task Reconnect_ResumesFromLastDispatchedCursorAndDeduplicatesReplay()
    {
        var handler = new StreamHandler();
        await using var client = Client(handler);
        var events = Channel.CreateUnbounded<ConsoleProposalEvent>();
        client.ProposalChanged += value => events.Writer.TryWrite(value);
        await client.StartAsync();
        var first = await handler.NextAsync();
        Assert.Equal("?topics=proposals", first.Query);
        Assert.Null(first.Cursor);
        Assert.Equal("binding-1", first.Binding);
        first.Stream.Send(Status(10) + Proposal(11, "first") + Proposal(11, "duplicate"));
        first.Stream.Complete();
        Assert.Equal("first", (await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).ProposalId);
        var resumed = await handler.NextAsync();
        Assert.Equal("admin:11", resumed.Cursor);
        resumed.Stream.Send(Status(11) + Proposal(11, "duplicate") + Proposal(12, "second"));
        Assert.Equal("second", (await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).ProposalId);
        Assert.False(events.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Reset_WaitsForEveryRestProjectionBeforeResumingAtHead()
    {
        var handler = new StreamHandler();
        await using var client = Client(handler);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondProjectionRead = false;
        client.RefreshRequired += async () => { entered.SetResult(); await release.Task; };
        client.RefreshRequired += () => { secondProjectionRead = true; return Task.CompletedTask; };
        await client.StartAsync();
        var first = await handler.NextAsync();
        first.Stream.Send(Status(5) + "event: reset\ndata: {\"reason\":\"cursor-expired\",\"head\":40}\n\n");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ConsoleRealtimeConnectionState.Connecting, client.ConnectionState);
        Assert.Equal(1, handler.RequestCount);
        Assert.False(secondProjectionRead);
        release.SetResult();
        var resumed = await handler.NextAsync();
        Assert.True(secondProjectionRead);
        Assert.Equal("admin:40", resumed.Cursor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reset_WithoutSuccessfulRestReconciliationStopsInManualMode(bool hasProjection)
    {
        var handler = new StreamHandler();
        await using var client = Client(handler);
        var fallback = WaitForState(client, ConsoleRealtimeConnectionState.FallbackEngaged);
        if (hasProjection) client.RefreshRequired += () => throw new InvalidOperationException("REST unavailable");
        await client.StartAsync();
        var first = await handler.NextAsync();
        first.Stream.Send(Status(5) + "event: reset\ndata: {\"head\":40}\n\n");
        await fallback.WaitAsync(TimeSpan.FromSeconds(5));
        await client.StopAsync();
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthorityOrEnvironmentChange_ClosesStreamAndDoesNotTransferCursor(bool changeProfile)
    {
        var handler = new StreamHandler();
        var authority = new Authority();
        var profiles = Profiles();
        await using var client = Client(handler, authority, profiles);
        var connected = WaitForState(client, ConsoleRealtimeConnectionState.Connected);
        await client.StartAsync();
        var first = await handler.NextAsync();
        first.Stream.Send(Status(9));
        await connected.WaitAsync(TimeSpan.FromSeconds(5));
        var stopped = WaitForState(client, ConsoleRealtimeConnectionState.NotConfigured);
        if (changeProfile) await profiles.ActivateProfileAsync("other");
        else authority.Invalidate();
        await stopped.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, handler.RequestCount);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task MissingAuthorityBinding_DoesNotSendARequest()
    {
        var handler = new StreamHandler();
        await using var client = new SseConsoleProposalRealtimeClient(new HttpClient(handler), Profiles(), null,
            NullLogger<SseConsoleProposalRealtimeClient>.Instance);
        await client.StartAsync();
        Assert.Equal(ConsoleRealtimeConnectionState.NotConfigured, client.ConnectionState);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task DeployTransition_UsesNativeOccurredAtTimestamp()
    {
        var handler = new StreamHandler();
        await using var client = new SseConsoleDeployOperationRealtimeClient(new HttpClient(handler), Profiles(), new Authority(),
            NullLogger<SseConsoleDeployOperationRealtimeClient>.Instance);
        var received = new TaskCompletionSource<ConsoleDeployOperationRealtimeEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OperationChanged += value => received.TrySetResult(value);
        await client.StartAsync();
        var request = await handler.NextAsync();
        Assert.Equal("?topics=deploy-operations", request.Query);
        request.Stream.Send(Status(1) + "id: admin:2\nevent: DeployOperationTransition\ndata: {\"operationId\":\"op-1\",\"transitionKind\":\"Completed\",\"status\":\"Succeeded\",\"occurredAt\":\"2026-11-16T18:00:00Z\"}\n\n");
        var value = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DateTimeOffset.Parse("2026-11-16T18:00:00Z"), value.GeneratedAt);
    }

    private static Task WaitForState(SseConsoleRealtimeClient client, ConsoleRealtimeConnectionState expected)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionStateChanged += state => { if (state == expected) ready.TrySetResult(); };
        return ready.Task;
    }

    private static string Status(long head) => $"id: admin:{head}\nevent: status\ndata: {{}}\n\n";
    private static string Proposal(long position, string id) =>
        $"id: admin:{position}\nevent: ProposalPending\ndata: {{\"proposalId\":\"{id}\",\"kind\":\"DataImport\",\"status\":\"AwaitingApproval\"}}\n\n";

    private static SseConsoleProposalRealtimeClient Client(StreamHandler handler, Authority? authority = null,
        InMemoryConsoleEnvironmentProfileStore? profiles = null) =>
        new(new HttpClient(handler), profiles ?? Profiles(), authority ?? new Authority(), NullLogger<SseConsoleProposalRealtimeClient>.Instance);

    private static InMemoryConsoleEnvironmentProfileStore Profiles() => new([
        new ConsoleEnvironmentProfile { Id = "selected", ServerBaseUri = new Uri("https://server.example/") },
        new ConsoleEnvironmentProfile { Id = "other", ServerBaseUri = new Uri("https://other.example/") }
    ], activeProfileId: "selected");

    private sealed class Authority : IConsoleAuthorityBindingValidator, IConsoleAuthorityChangeSource
    {
        public event Action? Changed;
        private bool _current = true;
        public Task<string?> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(_current ? "binding-1" : null);
        public Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default) => Task.FromResult(_current && binding == "binding-1");
        public void Invalidate() { _current = false; Changed?.Invoke(); }
    }

    private sealed record Request(string Query, string? Cursor, string? Binding, PushStream Stream);
    private sealed class StreamHandler : HttpMessageHandler
    {
        private readonly Channel<Request> _requests = Channel.CreateUnbounded<Request>();
        public int RequestCount { get; private set; }
        public Task<Request> NextAsync() => _requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var stream = new PushStream();
            request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Expected, out var binding);
            _requests.Writer.TryWrite(new Request(request.RequestUri!.Query,
                request.Headers.TryGetValues("Last-Event-ID", out var values) ? values.Single() : null, binding, stream));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(response);
        }
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }

    private sealed class PushStream : Stream
    {
        private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
        private byte[] _pending = [];
        private int _offset;
        public void Send(string value) => _chunks.Writer.TryWrite(Encoding.UTF8.GetBytes(value));
        public void Complete() => _chunks.Writer.TryComplete();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_offset == _pending.Length)
            {
                if (!await _chunks.Reader.WaitToReadAsync(cancellationToken)) return 0;
                _pending = await _chunks.Reader.ReadAsync(cancellationToken);
                _offset = 0;
            }
            var count = Math.Min(buffer.Length, _pending.Length - _offset);
            _pending.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
