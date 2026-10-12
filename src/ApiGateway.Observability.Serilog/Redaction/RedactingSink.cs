using Serilog.Core;
using Serilog.Events;

namespace ApiGateway.Observability.Serilog.Redaction;

/// <summary>
/// Wraps every real sink so each event is redacted right before it is written. It runs inside the async
/// wrapper, so redaction costs nothing on the request path, and no sink, whether added now or later, can
/// receive an unredacted event.
/// </summary>
public sealed class RedactingSink(ILogEventSink inner, LogEventRedactor redactor) : ILogEventSink, IDisposable, IAsyncDisposable
{
    public void Emit(LogEvent logEvent) => inner.Emit(redactor.Redact(logEvent));

    public void Dispose() => (inner as IDisposable)?.Dispose();

    public ValueTask DisposeAsync() => inner is IAsyncDisposable asyncDisposable ? asyncDisposable.DisposeAsync() : ValueTask.CompletedTask;
}
