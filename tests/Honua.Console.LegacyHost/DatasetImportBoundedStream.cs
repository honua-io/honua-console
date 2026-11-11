namespace Honua.Console.Web;

// Reads at most limit + one probe byte; never buffers a complete file or disposes
// the host-owned request body. The probe is never forwarded upstream.
internal sealed class DatasetImportBoundedStream(Stream inner, long limit) : Stream
{
    private long _read;
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, ProbeLength(count));
        return Count(read);
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer[..ProbeLength(buffer.Length)], cancellationToken);
        return Count(read);
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    private int ProbeLength(int requested) => (int)Math.Min(requested, limit - _read + 1);
    private int Count(int count)
    {
        if (count > limit - _read) throw new DatasetImportBodyTooLargeException();
        _read += count;
        return count;
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class DatasetImportBodyTooLargeException : IOException;
