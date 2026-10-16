using ApiGateway.Observability.Redaction;
using Serilog.Events;
using Serilog.Parsing;

namespace ApiGateway.Observability.Serilog.Redaction;

/// <summary>
/// Produces a redacted copy of a log event: properties with sensitive names are masked, secret patterns are
/// scrubbed from every string (nested structures, sequences and dictionaries included), from the message template
/// text (for libraries that log pre-formatted strings) and from exception text. Returns the same instance when
/// nothing needed changing, which is the common case.
/// </summary>
public sealed class LogEventRedactor(SensitiveDataClassifier classifier)
{
    private static readonly MessageTemplateParser Parser = new();

    private readonly ScalarValue _mask = new(classifier.Mask);

    public LogEvent Redact(LogEvent logEvent)
    {
        List<LogEventProperty>? properties = null;
        var index = 0;
        foreach (var (name, value) in logEvent.Properties)
        {
            var redacted = classifier.IsSensitiveName(name) ? _mask : RedactValue(value);
            if (!ReferenceEquals(redacted, value) && properties is null)
            {
                properties = new List<LogEventProperty>(logEvent.Properties.Count);
                properties.AddRange(logEvent.Properties.Take(index).Select(static pair => new LogEventProperty(pair.Key, pair.Value)));
            }

            properties?.Add(new LogEventProperty(name, redacted));
            index++;
        }

        var templateText = logEvent.MessageTemplate.Text;
        var scrubbedTemplate = classifier.Scrub(templateText);
        var templateChanged = !ReferenceEquals(scrubbedTemplate, templateText) && !string.Equals(scrubbedTemplate, templateText, StringComparison.Ordinal);

        var exception = logEvent.Exception is { } original ? ScrubbedException.CreateIfNeeded(original, classifier) : null;
        var exceptionChanged = !ReferenceEquals(exception, logEvent.Exception);

        if (properties is null && !templateChanged && !exceptionChanged)
        {
            return logEvent;
        }

        return new LogEvent(
            logEvent.Timestamp,
            logEvent.Level,
            exception,
            templateChanged ? Parser.Parse(scrubbedTemplate) : logEvent.MessageTemplate,
            properties ?? logEvent.Properties.Select(static pair => new LogEventProperty(pair.Key, pair.Value)),
            logEvent.TraceId ?? default,
            logEvent.SpanId ?? default);
    }

    internal LogEventPropertyValue RedactValue(LogEventPropertyValue value) => value switch
    {
        ScalarValue { Value: string text } => Scrubbed(value, text),
        StructureValue structure => RedactStructure(structure),
        SequenceValue sequence => RedactSequence(sequence),
        DictionaryValue dictionary => RedactDictionary(dictionary),
        _ => value,
    };

    private LogEventPropertyValue Scrubbed(LogEventPropertyValue original, string text)
    {
        var scrubbed = classifier.Scrub(text);
        return ReferenceEquals(scrubbed, text) || string.Equals(scrubbed, text, StringComparison.Ordinal) ? original : new ScalarValue(scrubbed);
    }

    private StructureValue RedactStructure(StructureValue structure)
    {
        var changed = false;
        var properties = new LogEventProperty[structure.Properties.Count];
        for (var i = 0; i < properties.Length; i++)
        {
            var property = structure.Properties[i];
            var redacted = classifier.IsSensitiveName(property.Name) ? _mask : RedactValue(property.Value);
            changed |= !ReferenceEquals(redacted, property.Value);
            properties[i] = ReferenceEquals(redacted, property.Value) ? property : new LogEventProperty(property.Name, redacted);
        }

        return changed ? new StructureValue(properties, structure.TypeTag) : structure;
    }

    private SequenceValue RedactSequence(SequenceValue sequence)
    {
        var changed = false;
        var elements = new LogEventPropertyValue[sequence.Elements.Count];
        for (var i = 0; i < elements.Length; i++)
        {
            elements[i] = RedactValue(sequence.Elements[i]);
            changed |= !ReferenceEquals(elements[i], sequence.Elements[i]);
        }

        return changed ? new SequenceValue(elements) : sequence;
    }

    private DictionaryValue RedactDictionary(DictionaryValue dictionary)
    {
        var changed = false;
        var entries = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictionary.Elements.Count);
        foreach (var (key, value) in dictionary.Elements)
        {
            var redacted = key.Value is string name && classifier.IsSensitiveName(name) ? _mask : RedactValue(value);
            changed |= !ReferenceEquals(redacted, value);
            entries.Add(new(key, redacted));
        }

        return changed ? new DictionaryValue(entries) : dictionary;
    }
}
