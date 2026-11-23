using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WCS.Application.Config;
using WCS.Application.Fleet;
using WCS.Application.RcsDiagnostics;
using WCS.Domain.Common;
using WCS.Domain.RcsDiagnostics;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.RcsDiagnostics;

/// <summary>
/// CR-032: the HTTP / TCP side of the RCS Diagnostics module. The destination is the saved RCS connection (host, port, http/https) plus a path that comes from the catalog; the browser can never supply a URL, host or port.
/// A separate HttpClient is used so a manual request can never queue behind, cancel or otherwise disturb the fleet monitoring poll. No credential is added: the only body is what the Admin typed (tokenCode, if the Admin chose to type one).
/// </summary>
internal sealed class RcsDiagnosticsTransport(IConnectionSettingRepository settings, IOptions<RcsFleetOptions> fleet) : IRcsDiagnosticsTransport
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2), ConnectTimeout = TimeSpan.FromSeconds(10), AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] SafeHeaders = ["content-type", "content-length", "date", "server", "cache-control", "connection", "transfer-encoding", "content-encoding"];

    public async Task<RcsEndpointInfo> ResolveAsync(bool dpsPath, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ConnectionKind.Rcs, ct);
        if (s is null) return new(false, false, null, null, null, null, "", 0, "The RCS connection is not saved.");
        RcsSettings? r;
        try { r = JsonSerializer.Deserialize<RcsSettings>(s.SettingsJson, Json); } catch (JsonException) { r = null; }
        if (r is null || string.IsNullOrWhiteSpace(r.Host)) return new(true, s.Enabled, null, null, null, r?.BaseUrl, r?.AuthMode ?? "", r?.TimeoutMs ?? 0, "The RCS address (host) is not set.");
        var scheme = Uri.TryCreate(r.BaseUrl, UriKind.Absolute, out var b) && b.Scheme == Uri.UriSchemeHttps ? "https" : "http";
        var port = dpsPath && fleet.Value.Real.Port > 0 ? fleet.Value.Real.Port : r.Port ?? 0;
        var timeout = r.TimeoutMs is > 0 ? Math.Clamp(r.TimeoutMs.Value, 1000, 60000) : 10000;
        if (!s.Enabled) return new(true, false, scheme, r.Host.Trim(), port > 0 ? port : null, r.BaseUrl, r.AuthMode, timeout, "The RCS connection is switched off.");
        if (port is < 1 or > 65535) return new(true, true, scheme, r.Host.Trim(), null, r.BaseUrl, r.AuthMode, timeout, "The RCS port is not set; no port is assumed.");
        return new(true, true, scheme, r.Host.Trim(), port, r.BaseUrl, r.AuthMode, timeout, null);
    }

    public async Task<RcsTransportResult> SendAsync(RcsEndpointInfo ep, string path, string jsonBody, int maxBytes, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ep.TimeoutMs);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{ep.Scheme}://{ep.Host}:{ep.Port}{path}") { Content = new StringContent(jsonBody, Encoding.UTF8, new MediaTypeHeaderValue("application/json")) };
            using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var headers = new Dictionary<string, string>();
            foreach (var h in res.Headers.Concat(res.Content.Headers))
                if (SafeHeaders.Contains(h.Key.ToLowerInvariant())) headers[h.Key] = string.Join(", ", h.Value);
            await using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
            var buf = new byte[Math.Max(1024, maxBytes) + 1]; var read = 0; int n;
            while (read < buf.Length && (n = await stream.ReadAsync(buf.AsMemory(read), cts.Token)) > 0) read += n;
            var truncated = read > maxBytes; if (truncated) read = maxBytes;
            return new(RcsTransportFailure.None, (int)res.StatusCode, headers, Encoding.UTF8.GetString(buf, 0, read), truncated, sw.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(RcsTransportFailure.Timeout, null, new Dictionary<string, string>(), null, false, sw.ElapsedMilliseconds, $"No answer within {ep.TimeoutMs / 1000.0:0.#} s."); }
        catch (HttpRequestException ex) { return new(RcsTransportFailure.Connection, null, new Dictionary<string, string>(), null, false, sw.ElapsedMilliseconds, Cut(ex.InnerException?.Message ?? ex.Message)); }
    }

    public async Task<RcsTcpResult> TcpCheckAsync(RcsEndpointInfo ep, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Math.Min(ep.TimeoutMs, 10000));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(ep.Host!, ep.Port!.Value, cts.Token);
            return new(true, sw.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, sw.ElapsedMilliseconds, "TCP connect timed out."); }
        catch (Exception ex) when (ex is SocketException or IOException) { return new(false, sw.ElapsedMilliseconds, Cut(ex.Message)); }
    }

    private static string Cut(string s) => s.Length > 300 ? s[..300] : s;
}

internal sealed class RcsDiagnosticsRepository(WcsDbContext db) : IRcsDiagnosticsRepository
{
    public void Add(RcsApiTest test) => db.RcsApiTests.Add(test);
    public async Task<IReadOnlyList<RcsDiagParam>> ListParamsAsync(CancellationToken ct = default) => await db.RcsDiagParams.AsNoTracking().OrderBy(p => p.Key).ToListAsync(ct);
    public Task<RcsDiagParam?> GetParamForUpdateAsync(string key, CancellationToken ct = default) => db.RcsDiagParams.FirstOrDefaultAsync(p => p.Key == key, ct);
    public void AddParam(RcsDiagParam p) => db.RcsDiagParams.Add(p);
    public void RemoveParam(RcsDiagParam p) => db.RcsDiagParams.Remove(p);

    private IQueryable<RcsApiTest> Filter(RcsHistoryQuery q)
    {
        var x = db.RcsApiTests.AsNoTracking().AsQueryable();
        if (q.From is { } f) x = x.Where(t => t.ExecutedAt >= f);
        if (q.To is { } to) x = x.Where(t => t.ExecutedAt <= to);
        if (!string.IsNullOrWhiteSpace(q.Api)) { var a = q.Api.Trim(); x = x.Where(t => t.Api == a); }
        if (!string.IsNullOrWhiteSpace(q.Username)) { var u = q.Username.Trim(); x = x.Where(t => t.Username == u); }
        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            var s = q.Status.Trim().ToUpperInvariant();
            x = s switch { "SUCCESS" => x.Where(t => t.Success), "FAILURE" => x.Where(t => !t.Success), _ => x.Where(t => t.Outcome == s) };
        }
        return x;
    }

    public async Task<IReadOnlyList<RcsApiTest>> QueryAsync(RcsHistoryQuery q, CancellationToken ct = default) => await Filter(q).OrderByDescending(t => t.ExecutedAt).Skip(q.Skip).Take(q.Take).ToListAsync(ct);
    public Task<int> CountAsync(RcsHistoryQuery q, CancellationToken ct = default) => Filter(q).CountAsync(ct);
    public Task<RcsApiTest?> GetAsync(Guid id, CancellationToken ct = default) => db.RcsApiTests.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task TrimAsync(int keep, CancellationToken ct = default)
    {
        var cutoff = await db.RcsApiTests.OrderByDescending(t => t.ExecutedAt).Skip(Math.Max(1, keep)).Select(t => (DateTimeOffset?)t.ExecutedAt).FirstOrDefaultAsync(ct);
        if (cutoff is { } c) await db.RcsApiTests.Where(t => t.ExecutedAt <= c).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<RcsCallbackLogEntry>> ListCallbacksAsync(string? callback, int skip, int take, CancellationToken ct = default)
    {
        var x = db.RcsCallbackLog.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(callback)) { var c = callback.Trim(); x = x.Where(e => e.Callback == c); }
        return await x.OrderByDescending(e => e.ReceivedAt).Skip(skip).Take(take).ToListAsync(ct);
    }
}
