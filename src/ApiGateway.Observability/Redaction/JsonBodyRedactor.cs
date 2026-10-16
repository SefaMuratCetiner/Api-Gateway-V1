using System.Buffers;
using System.Text;
using System.Text.Json;

namespace ApiGateway.Observability.Redaction;

/// <summary>
/// Redacts a captured body for logging. JSON is rewritten token by token with the values of sensitive
/// properties masked (whole objects/arrays included); the capture may be truncated, so the rewrite stops
/// cleanly at the cut. Non-JSON text is pattern-scrubbed only.
/// </summary>
public sealed class JsonBodyRedactor(SensitiveDataClassifier classifier)
{
    private const string TruncatedMarker = "...(truncated)";

    public string Redact(ReadOnlySpan<byte> body, bool isJson, bool truncated)
    {
        if (body.IsEmpty)
        {
            return string.Empty;
        }

        if (isJson && TryRedactJson(body, truncated, out var json))
        {
            return truncated ? json + TruncatedMarker : json;
        }

        var text = classifier.Scrub(Encoding.UTF8.GetString(body));
        return truncated ? text + TruncatedMarker : text;
    }

    private bool TryRedactJson(ReadOnlySpan<byte> body, bool truncated, out string result)
    {
        var output = new ArrayBufferWriter<byte>(body.Length);
        var reader = new Utf8JsonReader(body, isFinalBlock: !truncated, state: default);

        try
        {
            using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { SkipValidation = true });
            var maskNext = false;
            var maskDepth = -1;

            while (reader.Read())
            {
                // Inside a masked object/array: drop everything until it closes.
                if (maskDepth >= 0)
                {
                    if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray && reader.CurrentDepth == maskDepth)
                    {
                        maskDepth = -1;
                    }

                    continue;
                }

                maskDepth = WriteToken(ref reader, writer, ref maskNext);
            }

            writer.Flush();
            result = Encoding.UTF8.GetString(output.WrittenSpan);
            return true;
        }
        catch (JsonException)
        {
            result = string.Empty;
            return false;
        }
    }

    /// <summary>Copies one token, masking the value after a sensitive property name. Returns the masked container depth, or -1.</summary>
    private int WriteToken(ref Utf8JsonReader reader, Utf8JsonWriter writer, ref bool maskNext)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.PropertyName:
                var name = reader.GetString();
                writer.WritePropertyName(name!);
                maskNext = classifier.IsSensitiveName(name);
                return -1;
            case JsonTokenType.StartObject or JsonTokenType.StartArray when maskNext:
                writer.WriteStringValue(classifier.Mask);
                maskNext = false;
                return reader.CurrentDepth;
            case JsonTokenType.String or JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False or JsonTokenType.Null when maskNext:
                writer.WriteStringValue(classifier.Mask);
                maskNext = false;
                return -1;
            case JsonTokenType.StartObject:
                writer.WriteStartObject();
                break;
            case JsonTokenType.StartArray:
                writer.WriteStartArray();
                break;
            case JsonTokenType.EndObject:
                writer.WriteEndObject();
                break;
            case JsonTokenType.EndArray:
                writer.WriteEndArray();
                break;
            case JsonTokenType.String:
                writer.WriteStringValue(classifier.Scrub(reader.GetString()!));
                break;
            case JsonTokenType.Number:
                writer.WriteRawValue(reader.ValueSpan, skipInputValidation: true);
                break;
            case JsonTokenType.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonTokenType.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonTokenType.Null:
                writer.WriteNullValue();
                break;
        }

        return -1;
    }
}
