using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Config;
using WCS.Application.Fleet;
using WCS.Application.RcsDiagnostics;
using WCS.Domain.Common;
using WCS.Domain.Fleet;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.Fleet;

internal sealed class RcsFleetRepository(WcsDbContext db) : IRcsFleetRepository
{
    public Task<RcsFleetState?> GetStateAsync(FleetSource source, CancellationToken ct = default) => db.RcsFleetStates.FirstOrDefaultAsync(x => x.Source == source, ct);
    public void AddState(RcsFleetState state) => db.RcsFleetStates.Add(state);
    public async Task<IReadOnlyList<RcsAmrSnapshot>> ListAsync(FleetSource source, CancellationToken ct = default) =>
        await db.RcsAmrSnapshots.Where(x => x.Source == source).OrderBy(x => x.RobotCode).ToListAsync(ct);
    public void Add(RcsAmrSnapshot snapshot) => db.RcsAmrSnapshots.Add(snapshot);
}

/// <summary>The fleet comes from the in-memory RCS simulator (<c>Rcs:Fleet:Source = Simulator</c>, the default). Its rows are stored as Simulator and shown as simulated.</summary>
internal sealed class SimulatorFleetQuery(IRcsClient rcs) : IRcsFleetQuery
{
    public FleetSource Source => FleetSource.Simulator;
    public Task<RcsFleetResult> QueryAsync(CancellationToken ct = default) => rcs.QueryFleetAsync(ct);
}

/// <summary>
/// The REAL fleet query (<c>Rcs:Fleet:Source = Real</c>): ONE read-only <c>POST /rcms-dps/rest/queryAgvStatus</c> to the RCS configured in the RCS connection setting
/// (RCS-2000 API Developer Guide V3.3, 5.2.14). It sends reqCode, reqTime and mapCode (the MAP ID of the installation: the Admin-saved value of the "mapId" parameter, else Rcs:Diagnostics:MapId; never mapShortName): NO credential of any kind (no tokenCode, no username/password) - the RCS provides no authentication for this API (project decision, CR-031). It never sends a task command
/// and is the only RCS call this class can make. NOT verified against a real RCS yet: only against a local test server that answers like the document.
/// </summary>
internal sealed class HikFleetQueryClient(IConnectionSettingRepository settings, IOptions<RcsFleetOptions> options, IClock clock, IRcsDiagnosticsRepository diagnostics, IOptions<RcsDiagnosticsOptions> diagnosticsOptions) : IRcsFleetQuery
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), ConnectTimeout = TimeSpan.FromSeconds(10) }) { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public FleetSource Source => FleetSource.Real;

    private async Task<string?> ResolveMapIdAsync(CancellationToken ct)
    {
        var saved = (await diagnostics.ListParamsAsync(ct)).FirstOrDefault(p => p.Key == "mapId")?.Value;
        var value = !string.IsNullOrWhiteSpace(saved) ? saved : RcsParameterCatalog.ConfiguredDefault(RcsParameterCatalog.Find("mapId")!, diagnosticsOptions.Value);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public async Task<RcsFleetResult> QueryAsync(CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ConnectionKind.Rcs, ct);
        if (s is null || !s.Enabled) return RcsFleetResult.Fail(FleetErrorCodes.NotConfigured, "The RCS connection is not saved or is switched off.");
        RcsSettings? r;
        try { r = JsonSerializer.Deserialize<RcsSettings>(s.SettingsJson, Json); } catch (JsonException) { r = null; }
        if (r is null || string.IsNullOrWhiteSpace(r.Host)) return RcsFleetResult.Fail(FleetErrorCodes.NotConfigured, "The RCS address (host) is not set.");

        var real = options.Value.Real;
        var scheme = Uri.TryCreate(r.BaseUrl, UriKind.Absolute, out var b) && b.Scheme == Uri.UriSchemeHttps ? "https" : "http";
        var port = real.Port > 0 ? real.Port : r.Port ?? 0;
        if (port is < 1 or > 65535) return RcsFleetResult.Fail(FleetErrorCodes.NotConfigured, "The RCS port is not set (RCS connection or Rcs:Fleet:Real:Port); no port is assumed.");
        var url = $"{scheme}://{r.Host}:{port}{(real.Path.StartsWith('/') ? real.Path : "/" + real.Path)}";
        var reqCode = Guid.NewGuid().ToString("N");
        var endpoint = $"{r.Host}:{port}{(real.Path.StartsWith('/') ? real.Path : "/" + real.Path)}";   // host and port only: no credential exists in this call
        RcsFleetResult Ctx(RcsFleetResult x) => x with { Endpoint = endpoint, RequestCode = reqCode };
        var body = new Dictionary<string, string> { ["reqCode"] = reqCode, ["reqTime"] = clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") };
        // the RCS answers code 1 "the map number is mandatory when querying all robots" without a map: the MAP ID is the one the Admin saved in RCS Diagnostics, else the central configuration (Rcs:Diagnostics:MapId)
        var mapId = await ResolveMapIdAsync(ct);
        if (mapId is not null) body["mapCode"] = mapId;
        // no tokenCode: the RCS defines no authentication for this API; polling never depends on a token, a username or a password

        var sw = Stopwatch.StartNew();
        // the client times out slightly BEFORE the caller does, so a timeout is reported with its endpoint and request id (the caller's own timeout stays as the safety net)
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(500, options.Value.TimeoutSeconds * 1000 - 250)));
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, new MediaTypeHeaderValue("application/json")) };
            using var res = await Http.SendAsync(req, cts.Token);
            var text = await res.Content.ReadAsStringAsync(cts.Token);
            if (!res.IsSuccessStatusCode) return Ctx(RcsFleetResult.Fail($"{FleetErrorCodes.Http}:{(int)res.StatusCode}", $"HTTP {(int)res.StatusCode}", sw.ElapsedMilliseconds) with { HttpStatus = (int)res.StatusCode });
            return Ctx(HikQueryAgvStatusParser.Parse(text, sw.ElapsedMilliseconds) with { HttpStatus = (int)res.StatusCode });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Ctx(RcsFleetResult.Fail(FleetErrorCodes.Timeout, "The RCS did not answer in time.", sw.ElapsedMilliseconds)); }
        catch (HttpRequestException ex) { return Ctx(RcsFleetResult.Fail(FleetErrorCodes.ConnectionFailed, FleetService.Sanitize(ex.Message), sw.ElapsedMilliseconds)); }
    }
}
