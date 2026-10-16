using ApiGateway.Observability.Redaction;

namespace ApiGateway.Observability.Serilog.Redaction;

/// <summary>
/// Stand-in for an exception whose text contained secrets (a token in a URL, a connection string). Carries the
/// scrubbed message, stack trace and <see cref="ToString"/> and remembers the original type name, so sinks that
/// render exceptions never see the secret.
/// </summary>
public sealed class ScrubbedException : Exception
{
    private readonly string _toString;
    private readonly string? _stackTrace;

    public ScrubbedException()
        : this("Scrubbed exception", "Scrubbed exception", null, "Exception")
    {
    }

    public ScrubbedException(string message)
        : this(message, message, null, "Exception")
    {
    }

    public ScrubbedException(string message, Exception innerException)
        : base(message, innerException)
    {
        _toString = message;
        OriginalType = innerException.GetType().FullName ?? "Exception";
    }

    private ScrubbedException(string message, string toString, string? stackTrace, string originalType)
        : base(message)
    {
        _toString = toString;
        _stackTrace = stackTrace;
        OriginalType = originalType;
        Data["OriginalType"] = originalType;
    }

    /// <summary>Full name of the exception type that was replaced.</summary>
    public string OriginalType { get; }

    public override string? StackTrace => _stackTrace;

    public override string ToString() => _toString;

    /// <summary>Returns <paramref name="exception"/> itself when its text holds nothing to scrub.</summary>
    public static Exception CreateIfNeeded(Exception exception, SensitiveDataClassifier classifier)
    {
        var text = exception.ToString();
        var scrubbed = classifier.Scrub(text);
        if (string.Equals(text, scrubbed, StringComparison.Ordinal))
        {
            return exception;
        }

        return new ScrubbedException(
            classifier.Scrub(exception.Message),
            scrubbed,
            exception.StackTrace is { } stack ? classifier.Scrub(stack) : null,
            exception.GetType().FullName ?? "Exception");
    }
}
