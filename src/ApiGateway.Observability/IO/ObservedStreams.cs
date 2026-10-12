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

/// <summary>Keeps at most <c>limit</c> bytes; remembers whether more were offered.</summary>
internal sealed class BoundedCapture(int limit)
{
    private byte[]? _buffer;
    private int _length;

    public bool Truncated { get; private set; }

    public ReadOnlyMemory<byte> Data => _buffer is null ? ReadOnlyMemory<byte>.Empty : _buffer.AsMemory(0, _length);

    public void Append(ReadOnlySpan<byte> data)
    {
        if (limit <= 0 || data.IsEmpty)
        {
            return;
        }

        var room = limit - _length;
        if (room <= 0)
        {
            Truncated = true;
            return;
        }

        _buffer ??= new byte[limit];
        var take = Math.Min(room, data.Length);
        data[..take].CopyTo(_buffer.AsSpan(_length));
        _length += take;
        if (take < data.Length)
        {
            Truncated = true;
        }
    }
}
