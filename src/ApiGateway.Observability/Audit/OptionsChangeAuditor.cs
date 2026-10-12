using ApiGateway.Abstractions.Audit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ApiGateway.Observability.Audit;

/// <summary>
/// Audits hot-reloaded changes of one options type (fingerprints only). Register one per options type whose
/// changes matter operationally.
/// </summary>
/// <typeparam name="TOptions">The options type to watch.</typeparam>
public sealed class OptionsChangeAuditor<TOptions>(IOptionsMonitor<TOptions> monitor, IAuditLog audit) : IHostedService, IDisposable
    where TOptions : class
{
    private readonly Lock _lock = new();
    private IDisposable? _subscription;
    private string? _current;

    public string Target { get; } = "options:" + typeof(TOptions).Name;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _current = ConfigFingerprint.Of(monitor.CurrentValue);
        _subscription = monitor.OnChange(OnChange);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        return Task.CompletedTask;
    }

    public void Dispose() => _subscription?.Dispose();

    private void OnChange(TOptions options, string? name)
    {
        var next = ConfigFingerprint.Of(options);
        lock (_lock)
        {
            if (string.Equals(next, _current, StringComparison.Ordinal))
            {
                return;
            }

            audit.Write(new AuditEvent(ProxyConfigAuditListener.Actor, "options.changed", Target, BeforeHash: _current, AfterHash: next, Details: name));
            _current = next;
        }
    }
}
