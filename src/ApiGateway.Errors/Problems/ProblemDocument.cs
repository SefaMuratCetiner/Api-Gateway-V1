using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace ApiGateway.Errors.Problems;

/// <summary>
/// An RFC 9457 problem document in the gateway's fixed shape. Member order is part of the contract:
/// <c>type</c>, <c>title</c>, <c>status</c>, <c>detail</c>, <c>instance</c>, <c>code</c>, <c>traceId</c>, then extensions.
/// </summary>
public sealed record ProblemDocument(
    string Type,
    string Title,
    int Status,
    string? Detail,
    string? Instance,
    string Code,
    string TraceId,
    IReadOnlyDictionary<string, object?>? Extensions = null)
{
    public const string ContentType = "application/problem+json";

    /// <summary>Members an extension may not overwrite.</summary>
    public static readonly IReadOnlySet<string> ReservedMembers =
        new HashSet<string>(StringComparer.Ordinal) { "type", "title", "status", "detail", "instance", "code", "traceId" };

    /// <summary>
    /// Writes the document. Extension values are limited to JSON primitives and sequences of them, so nothing
    /// that could carry internal state (exceptions, arbitrary objects) is ever serialized.
    /// </summary>
    public void WriteTo(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("type", Type);
        writer.WriteString("title", Title);
        writer.WriteNumber("status", Status);
        if (Detail is not null)
        {
            writer.WriteString("detail", Detail);
        }

        if (Instance is not null)
        {
            writer.WriteString("instance", Instance);
        }

        writer.WriteString("code", Code);
        writer.WriteString("traceId", TraceId);

        if (Extensions is not null)
        {
            foreach (var (name, value) in Extensions)
            {
                if (ReservedMembers.Contains(name))
                {
                    continue;
                }

                writer.WritePropertyName(name);
                WriteValue(writer, value);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case TimeSpan span:
                writer.WriteNumberValue((long)Math.Ceiling(span.TotalSeconds));
                break;
            case DateTimeOffset moment:
                writer.WriteStringValue(moment);
                break;
            case IEnumerable sequence:
                writer.WriteStartArray();
                foreach (var item in sequence)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}
