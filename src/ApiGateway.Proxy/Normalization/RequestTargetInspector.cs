using System.Text;

namespace ApiGateway.Proxy.Normalization;

/// <summary>Why a request target was rejected. Bounded; logged and used as a span attribute.</summary>
public enum RequestTargetProblem
{
    None,
    MalformedEncoding,
    DoubleEncoding,
    InvalidUtf8,
    ControlCharacter,
    EncodedSlash,
}

/// <summary>
/// Inspects the raw request target (as the client sent it, before any decoding). The path must not be
/// percent-encoded twice (<c>%252e</c>), must decode to strict UTF-8 (no overlong forms such as <c>%c0%ae</c>), must
/// not contain NUL or other control characters, and by default must not hide a slash or backslash in an escape
/// (<c>%2f</c>, <c>%5c</c>) or contain a raw backslash. The query must decode to UTF-8 without control characters.
/// Pure function.
/// </summary>
public static class RequestTargetInspector
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static RequestTargetProblem Inspect(string rawTarget, bool allowEncodedSlashes)
    {
        var (path, query) = Split(rawTarget);

        var problem = InspectComponent(path, isPath: true, allowEncodedSlashes);
        return problem != RequestTargetProblem.None ? problem : InspectComponent(query, isPath: false, allowEncodedSlashes);
    }

    /// <summary>
    /// Checks the decoded request path (<c>HttpRequest.Path</c>). Servers decode it once, keeping only <c>%2F</c>
    /// encoded, so any other <c>%XX</c> left in it was encoded twice by the client. Also catches control characters
    /// and backslashes whatever the server or host passed as the raw target.
    /// </summary>
    public static RequestTargetProblem InspectDecodedPath(string path, bool allowEncodedSlashes)
    {
        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];
            if (IsControl(c))
            {
                return RequestTargetProblem.ControlCharacter;
            }

            if (c == '\\' && !allowEncodedSlashes)
            {
                return RequestTargetProblem.EncodedSlash;
            }

            if (c == '%' && i + 2 < path.Length && char.IsAsciiHexDigit(path[i + 1]) && char.IsAsciiHexDigit(path[i + 2]))
            {
                var isSlash = path[i + 1] == '2' && (path[i + 2] is 'f' or 'F');
                if (!isSlash)
                {
                    return RequestTargetProblem.DoubleEncoding;
                }

                if (!allowEncodedSlashes)
                {
                    return RequestTargetProblem.EncodedSlash;
                }
            }
        }

        return RequestTargetProblem.None;
    }

    /// <summary>Header values must not contain control characters other than horizontal tab.</summary>
    public static bool HasControlCharacter(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (IsControl(c) && c != '\t')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Path and query of an origin-form or absolute-form target; <c>*</c> and authority-form have neither.</summary>
    internal static (string Path, string Query) Split(string rawTarget)
    {
        var target = rawTarget.AsSpan();
        if (target.Length == 0 || target[0] != '/')
        {
            var scheme = target.IndexOf("://", StringComparison.Ordinal);
            if (scheme < 0)
            {
                return ("", "");
            }

            var afterAuthority = target[(scheme + 3)..].IndexOfAny('/', '?');
            target = afterAuthority < 0 ? [] : target[(scheme + 3 + afterAuthority)..];
        }

        var question = target.IndexOf('?');
        return question < 0
            ? (target.ToString(), "")
            : (target[..question].ToString(), target[(question + 1)..].ToString());
    }

    private static RequestTargetProblem InspectComponent(string component, bool isPath, bool allowEncodedSlashes)
    {
        if (component.Length == 0)
        {
            return RequestTargetProblem.None;
        }

        // Raw non-ASCII (some clients send UTF-8 unescaped) is validated like decoded bytes.
        var decoded = new List<byte>(component.Length);
        var problem = Decode(Encoding.UTF8.GetBytes(component), isPath, allowEncodedSlashes, decoded);
        if (problem != RequestTargetProblem.None)
        {
            return problem;
        }

        string text;
        try
        {
            text = StrictUtf8.GetString([.. decoded]);
        }
        catch (DecoderFallbackException)
        {
            return RequestTargetProblem.InvalidUtf8;
        }

        foreach (var c in text)
        {
            if (IsControl(c))
            {
                return RequestTargetProblem.ControlCharacter;
            }
        }

        return RequestTargetProblem.None;
    }

    private static RequestTargetProblem Decode(byte[] raw, bool isPath, bool allowEncodedSlashes, List<byte> decoded)
    {
        for (var i = 0; i < raw.Length; i++)
        {
            var b = raw[i];
            if (b == '%')
            {
                if (i + 2 >= raw.Length || !IsHex(raw[i + 1]) || !IsHex(raw[i + 2]))
                {
                    // Query strings in the wild contain stray '%'; only the path must be strictly encoded.
                    if (isPath)
                    {
                        return RequestTargetProblem.MalformedEncoding;
                    }

                    decoded.Add(b);
                    continue;
                }

                var value = (byte)((HexValue(raw[i + 1]) << 4) | HexValue(raw[i + 2]));
                if (isPath && value == '%' && i + 4 < raw.Length && IsHex(raw[i + 3]) && IsHex(raw[i + 4]))
                {
                    return RequestTargetProblem.DoubleEncoding;
                }

                if (isPath && !allowEncodedSlashes && value is (byte)'/' or (byte)'\\')
                {
                    return RequestTargetProblem.EncodedSlash;
                }

                decoded.Add(value);
                i += 2;
                continue;
            }

            if (isPath && b == '\\' && !allowEncodedSlashes)
            {
                return RequestTargetProblem.EncodedSlash;
            }

            decoded.Add(b);
        }

        return RequestTargetProblem.None;
    }

    private static bool IsControl(char c) => c < 0x20 || c == 0x7F;

    private static bool IsHex(byte b) => char.IsAsciiHexDigit((char)b);

    private static int HexValue(byte b) => b <= '9' ? b - '0' : (b | 0x20) - 'a' + 10;
}
