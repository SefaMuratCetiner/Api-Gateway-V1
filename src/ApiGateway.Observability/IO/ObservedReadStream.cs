namespace ApiGateway.Observability.IO;

/// <summary>
/// Read-side pass-through stream: counts bytes and optionally keeps a copy of the first <c>captureLimit</c>
/// bytes. Never buffers beyond that, so streaming uploads stay streaming.
/// </summary>
public sealed class ObservedReadStream(Stream inner, int captureLimit = 0) : Stream
{
    private readonly BoundedCapture _capture = new(captureLimit);

    public long BytesRead { get; private set; }

    public bool CaptureTruncated => _capture.Truncated;

    public ReadOnlyMemory<byte> Captured => _capture.Data;

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Observe(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        return Observe(buffer[..read]);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        return Observe(buffer.Span[..read]);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Observe(ReadOnlySpan<byte> data)
    {
        BytesRead += data.Length;
        _capture.Append(data);
        return data.Length;
    }
}
