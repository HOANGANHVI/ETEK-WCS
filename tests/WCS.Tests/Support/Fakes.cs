using Microsoft.Extensions.Logging;
using System.Text;
using WCS.Application.Abstractions;
using WCS.Application.Config;
using WCS.Domain.Common;

namespace WCS.Tests.Support;

/// <summary>In-memory stand-ins so configuration logic can be tested without PostgreSQL.</summary>
/// <summary>CR-026: a Maintenance gate that is never on (for tests that build their own service collection).</summary>
public sealed class OpenMaintenanceGate : IMaintenanceGate
{
    public Task<bool> IsEnabledAsync(CancellationToken ct = default) => Task.FromResult(false);
    public Task EnsureOpenAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> HoldNewWorkAsync(CancellationToken ct = default) => Task.FromResult(false);
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default) => work(ct);
}

public sealed class FakeConnectionRepo : IConnectionSettingRepository
{
    public Dictionary<ConnectionKind, ConnectionSetting> Rows { get; } = [];
    public Task<ConnectionSetting?> GetAsync(ConnectionKind kind, CancellationToken ct = default) =>
        Task.FromResult(Rows.GetValueOrDefault(kind));
    public void Add(ConnectionSetting setting) => Rows[setting.Kind] = setting;
}

/// <summary>Reversible but clearly NOT plaintext ("enc:" + base64), so tests can assert nothing readable is stored.</summary>
public sealed class FakeProtector : ISecretProtector
{
    public string Protect(string plaintext) => "enc:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(new string(plaintext.Reverse().ToArray())));
    public string Unprotect(string protectedValue) =>
        new(Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue["enc:".Length..])).Reverse().ToArray());
}

public sealed class RecordingProbe(ProbeResult? result = null) : IConnectionProbe
{
    public List<(ConnectionKind Kind, string Host, int Port, TimeSpan Timeout)> Calls { get; } = [];
    public Task<ProbeResult> ProbeAsync(ConnectionKind kind, string host, int port, TimeSpan timeout, CancellationToken ct = default)
    {
        Calls.Add((kind, host, port, timeout));
        return Task.FromResult(result ?? new ProbeResult(ConnectionTestOutcome.Simulated, "Simulated", "mô phỏng"));
    }
}

public sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
        if (exception is not null) Messages.Add(exception.ToString());
    }
}
