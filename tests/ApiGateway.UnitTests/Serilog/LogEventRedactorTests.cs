using ApiGateway.Observability.Options;
using ApiGateway.Observability.Redaction;
using ApiGateway.Observability.Serilog.Redaction;
using Serilog.Events;
using Serilog.Parsing;

namespace ApiGateway.UnitTests.Serilog;

public sealed class LogEventRedactorTests
{
    private static readonly MessageTemplateParser Parser = new();
    private readonly LogEventRedactor _redactor = new(new SensitiveDataPolicy(new RedactionOptions()));

    [Fact]
    public void Masks_properties_with_sensitive_names()
    {
        var logEvent = Event("Calling {Url}", ("Url", new ScalarValue("/orders")), ("Authorization", new ScalarValue("Bearer abcdefghijkl")));

        var redacted = _redactor.Redact(logEvent);

        Scalar(redacted, "Authorization").ShouldBe("***");
        Scalar(redacted, "Url").ShouldBe("/orders");
    }

    [Fact]
    public void Scrubs_secrets_inside_nested_values()
    {
        var headers = new DictionaryValue(
        [
            new(new ScalarValue("Cookie"), new ScalarValue("session=abc")),
            new(new ScalarValue("User-Agent"), new ScalarValue("curl")),
        ]);
        var request = new StructureValue([new LogEventProperty("Password", new ScalarValue("hunter2")), new LogEventProperty("Path", new ScalarValue("/x?token=abc123"))]);
        var logEvent = Event("Request {Request} {Headers}", ("Request", request), ("Headers", headers));

        var rendered = _redactor.Redact(logEvent).RenderMessage(System.Globalization.CultureInfo.InvariantCulture);

        rendered.ShouldNotContain("hunter2");
        rendered.ShouldNotContain("session=abc");
        rendered.ShouldNotContain("abc123");
        rendered.ShouldContain("curl");
    }

    [Fact]
    public void Scrubs_preformatted_message_templates_and_exceptions()
    {
        var exception = new InvalidOperationException("Request to https://idp/token?client_secret=topsecret failed");
        var logEvent = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Error, exception, Parser.Parse("Auth header was Bearer abcdefghijklmnop"), []);

        var redacted = _redactor.Redact(logEvent);

        redacted.MessageTemplate.Text.ShouldNotContain("abcdefghijklmnop");
        redacted.Exception.ShouldBeOfType<ScrubbedException>();
        redacted.Exception!.ToString().ShouldNotContain("topsecret");
        ((ScrubbedException)redacted.Exception).OriginalType.ShouldBe(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public void Clean_events_are_returned_as_is()
    {
        var logEvent = Event("Handled {Count} requests", ("Count", new ScalarValue(3)));

        _redactor.Redact(logEvent).ShouldBeSameAs(logEvent);
    }

    private static LogEvent Event(string template, params (string Name, LogEventPropertyValue Value)[] properties) =>
        new(DateTimeOffset.UtcNow, LogEventLevel.Information, null, Parser.Parse(template), properties.Select(static p => new LogEventProperty(p.Name, p.Value)));

    private static object? Scalar(LogEvent logEvent, string name) => ((ScalarValue)logEvent.Properties[name]).Value;
}
