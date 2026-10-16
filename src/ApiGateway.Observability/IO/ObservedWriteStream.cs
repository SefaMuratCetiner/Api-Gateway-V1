namespace ApiGateway.Observability.IO;

/// <summary>
/// Write-side pass-through stream for the response body: counts bytes and optionally keeps the first
/// <c>captureLimit</c> bytes. Flushes are passed through, so server-sent events and downloads are not delayed.
/// </summary>
public sealed class ObservedWriteStream(Stream inner, int captureLimit = 0) : Stream
{
    private readonly BoundedCapture _capture = new(captureLimit);

    public long BytesWritten { get; private set; }

    public bool CaptureTruncated => _capture.Truncated;

    public ReadOnlyMemory<byte> Captured => _capture.Data;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Observe(buffer.AsSpan(offset, count));
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Observe(buffer);
        inner.Write(buffer);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Observe(buffer.Span);
        return inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    private void Observe(ReadOnlySpan<byte> data)
    {
        BytesWritten += data.Length;
        _capture.Append(data);
    }
}
