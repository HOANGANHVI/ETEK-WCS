using WCS.Domain.Common;

namespace WCS.Application.Config;

public interface IConnectionSettingRepository
{
    Task<ConnectionSetting?> GetAsync(ConnectionKind kind, CancellationToken ct = default);
    void Add(ConnectionSetting setting);
}

/// <summary>Reversible protection of secrets at rest. Implementations must never log plaintext.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}

public sealed record ProbeResult(ConnectionTestOutcome Outcome, string Mode, string Message, string? Error = null, long? LatencyMs = null);

/// <summary>
/// Safe connectivity check. It must have NO effect on a device: it may only verify that a TCP port accepts a connection (and close it
/// immediately) – it never sends PLC commands, RCS requests or transport commands. The default implementation does not touch the
/// network at all.
/// </summary>
public interface IConnectionProbe
{
    Task<ProbeResult> ProbeAsync(ConnectionKind kind, string host, int port, TimeSpan timeout, CancellationToken ct = default);
}
