namespace ApiGateway.Observability.IO;

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
