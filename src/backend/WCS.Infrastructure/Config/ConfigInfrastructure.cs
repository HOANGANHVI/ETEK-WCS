using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WCS.Application.Config;
using WCS.Domain.Common;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.Config;

internal sealed class ConnectionSettingRepository(WcsDbContext db) : IConnectionSettingRepository
{
    public Task<ConnectionSetting?> GetAsync(ConnectionKind kind, CancellationToken ct = default) =>
        db.ConnectionSettings.FirstOrDefaultAsync(x => x.Kind == kind, ct);

    public void Add(ConnectionSetting setting) => db.ConnectionSettings.Add(setting);
}

/// <summary>
/// Encrypts secrets with ASP.NET Core Data Protection. The key ring location is configurable (DataProtection:KeysPath); on loss of the key ring
/// the stored secrets become unreadable and must be entered again (the service handles that without leaking anything).
/// </summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("ETEK.WCS.ConnectionSecrets.v1");
    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}

/// <summary>Default probe: touches NO network. It only reports that the check was simulated.</summary>
public sealed class SimulatedConnectionProbe : IConnectionProbe
{
    public Task<ProbeResult> ProbeAsync(ConnectionKind kind, string host, int port, TimeSpan timeout, CancellationToken ct = default) =>
        Task.FromResult(new ProbeResult(ConnectionTestOutcome.Simulated, "Simulated",
            "[PROBE_SIMULATED] Kiểm tra MÔ PHỎNG: cấu hình hợp lệ nhưng KHÔNG có kết nối mạng nào tới thiết bị được thực hiện. " +
            "Việc kiểm tra mạng thật đang bị tắt (Integration:AllowNetworkProbe = false)."));
}

/// <summary>
/// Opt-in reachability check (Integration:AllowNetworkProbe = true): opens a TCP connection to host:port and closes it immediately.
/// It sends NOTHING – no MC Protocol frame, no HTTP request, no command – so it cannot affect a PLC or the RCS. It does not prove that the
/// protocol/credentials are right; that needs the real adapters (not implemented).
/// </summary>
public sealed class TcpReachabilityProbe : IConnectionProbe
{
    public async Task<ProbeResult> ProbeAsync(ConnectionKind kind, string host, int port, TimeSpan timeout, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            return new ProbeResult(ConnectionTestOutcome.Success, "TcpReachability",
                kind == ConnectionKind.Rcs
                    ? "[RCS_TCP_OPEN] Cổng TCP đang mở. Chưa gọi API RCS (endpoint/payload chưa được xác nhận) nên chưa kiểm tra được xác thực và giao thức."
                    : "[PLC_TCP_OPEN] Cổng TCP đang mở. Chưa gửi khung MC Protocol nào nên chưa kiểm tra được giao thức và các số hiệu mạng/trạm.",
                null, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ProbeResult(ConnectionTestOutcome.Failed, "TcpReachability", "[TCP_TIMEOUT] Hết thời gian chờ khi mở cổng TCP.", "TIMEOUT", sw.ElapsedMilliseconds);
        }
        catch (SocketException ex)
        {
            return new ProbeResult(ConnectionTestOutcome.Failed, "TcpReachability", "[TCP_UNREACHABLE] Không mở được cổng TCP.", ex.SocketErrorCode.ToString(), sw.ElapsedMilliseconds);
        }
    }
}
