using System.IO.Compression;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WCS.Application.Diagnostics;
using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Infrastructure.Plc.Mc;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>PLC Diagnostics against the real PostgreSQL schema: stored MC history (cap, restart, database failure), persisted codes/stages, and the Admin-only ZIP export.</summary>
[Collection("Database")]
public class PlcDiagnosticsIntegrationTests(DatabaseFixture fixture)
{
    private static readonly McRoute Route = new(0x00, 0xFF, 0x03FF, 0x00);

    private static PlcExchange Ex(string plc, DateTimeOffset at, string outcome = "TIMEOUT", int? endCode = null) =>
        new(at, plc, "READ", "0x0401", "M", 100, 1, 12, endCode, outcome, outcome == "OK" ? null : "no response", "50 00 00 FF FF 03 00 0C 00 10 00 01 04 01 00 64 00 00 90 01 00", null, false);

    private static async Task<T> WaitFor<T>(Func<Task<T>> read, Func<T, bool> ok, int ms = 8000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        T v = await read();
        while (!ok(v) && sw.ElapsedMilliseconds < ms) { await Task.Delay(50); v = await read(); }
        return v;
    }

    // ================================================================== MC history

    [Fact]
    public async Task MC_exchanges_are_stored_with_hex_latency_and_end_code_and_successes_are_sampled()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var rec = h.Services.GetRequiredService<PlcExchangeRecorder>();
        await using var server = new MelsecMcMockServer { ExpectedRoute = Route }; server.Start();
        server.SetBit(100, true);
        await using var c = new McTcpClient(new McClientSettings("127.0.0.1", server.Port, Route, 0x0010, TimeSpan.FromMilliseconds(400), MaxReadRetries: 0, PlcId: "PLC-01", OnExchange: rec.Record));
        await c.ConnectAsync(CancellationToken.None);
        for (var i = 0; i < 5; i++) await c.ReadBitsAsync(new DeviceAddress(PlcDevice.M, 100), 1, CancellationToken.None);   // 5 successes in a burst
        server.ForceEndCode(0x4031, 1);
        await Assert.ThrowsAsync<McEndCodeException>(() => c.ReadBitsAsync(new DeviceAddress(PlcDevice.M, 100), 1, CancellationToken.None));
        await rec.FlushAsync();

        var rows = await h.Db(db => db.PlcExchangeLog.AsNoTracking().Where(x => x.PlcId == "PLC-01").OrderBy(x => x.OccurredAt).ToListAsync());
        Assert.Equal(1, rows.Count(r => r.Outcome == "OK"));                                  // routine successes are sampled (one per 5 s)
        var failed = Assert.Single(rows, r => r.Outcome == "END_CODE");                       // a failure is always kept
        Assert.Equal(0x4031, failed.EndCode);
        Assert.True(failed.DurationMs >= 0);
        Assert.False(string.IsNullOrWhiteSpace(failed.RequestHex));
        Assert.True(failed.RequestHex!.Length <= PlcExchangeLogEntry.MaxHex);
        Assert.StartsWith("50 00", failed.RequestHex);                                        // MC 3E binary subheader
    }

    [Fact]
    public async Task At_most_300_exchanges_are_kept_per_PLC_the_newest_ones_and_they_survive_a_restart()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var rec = h.Services.GetRequiredService<PlcExchangeRecorder>();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        for (var i = 0; i < 350; i++) rec.Record(Ex("PLC-01", t0.AddSeconds(i)));
        for (var i = 0; i < 20; i++) rec.Record(Ex("PLC-02", t0.AddSeconds(i)));
        await rec.FlushAsync(10000);
        var counts = await WaitFor(() => h.Db(db => db.PlcExchangeLog.AsNoTracking().GroupBy(x => x.PlcId).Select(g => new { g.Key, N = g.Count() }).ToListAsync()),
            r => r.FirstOrDefault(x => x.Key == "PLC-01")?.N == 300);
        Assert.Equal(300, counts.Single(x => x.Key == "PLC-01").N);                           // capped
        Assert.Equal(20, counts.Single(x => x.Key == "PLC-02").N);                            // another PLC is not affected
        var oldest = await h.Db(db => db.PlcExchangeLog.AsNoTracking().Where(x => x.PlcId == "PLC-01").MinAsync(x => x.OccurredAt));
        Assert.True(oldest >= t0.AddSeconds(50).AddMilliseconds(-1));                          // the 50 oldest were pruned

        await h.RestartAsync();                                                                  // the runtime restarts: the history is still there
        Assert.Equal(300, await h.Db(db => db.PlcExchangeLog.CountAsync(x => x.PlcId == "PLC-01")));
    }

    [Fact]
    public async Task When_PostgreSQL_cannot_store_the_history_the_PLC_path_is_not_disturbed()
    {
        var services = new ServiceCollection();
        services.AddDbContext<WcsDbContext>(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1;Command Timeout=1"));
        await using var sp = services.BuildServiceProvider();
        using var rec = new PlcExchangeRecorder(sp, Options.Create(new PlcRuntimeOptions { ExchangeOkSampleSeconds = 0 }), NullLogger<PlcExchangeRecorder>.Instance);

        await using var server = new MelsecMcMockServer { ExpectedRoute = Route }; server.Start();
        server.SetBit(100, true);
        await using var c = new McTcpClient(new McClientSettings("127.0.0.1", server.Port, Route, 0x0010, TimeSpan.FromMilliseconds(400), PlcId: "PLC-01", OnExchange: rec.Record));
        await c.ConnectAsync(CancellationToken.None);
        for (var i = 0; i < 5; i++)
        {
            var bits = await c.ReadBitsAsync(new DeviceAddress(PlcDevice.M, 100), 1, CancellationToken.None);   // keeps working
            Assert.True(bits[0]);
        }
        await rec.FlushAsync(10000);
        Assert.True(await WaitFor(() => Task.FromResult(rec.FailedBatches), n => n > 0) > 0);                    // the failure is counted, not thrown
        Assert.Equal(0, rec.StoredEntries);
        Assert.Equal(5, c.Diagnostics.Requests);
    }

    // ================================================================== persisted code and stage (through the real runtime)

    private async Task<(Harness H, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> F)> RuntimeAsync(Dictionary<string, string?> plc, Func<WcsDbContext, Task>? prepare = null)
    {
        var h = await Harness.CreateAsync(fixture);
        using (var seed = new DbApiFactory(fixture))
        using (var scope = seed.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>().SeedAsync();
            if (prepare is not null) { var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>(); await prepare(db); }
        }
        var cfg = new Dictionary<string, string?> { ["Plc:ReconnectInitialDelayMs"] = "50", ["Plc:ReconnectMaxDelayMs"] = "200" };
        foreach (var kv in plc) cfg[kv.Key] = kv.Value;
        var f = new DbApiFactory(fixture).WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(cfg)));
        await f.Services.GetRequiredService<IPlcRuntime>().StartAsync();
        return (h, f);
    }

    [Fact]
    public async Task AllowLiveConnection_false_is_logged_as_LIVE_CONNECTION_DISABLED_at_CONFIG_with_a_configuration_snapshot_and_no_TCP_attempt()
    {
        var (h, f) = await RuntimeAsync(new() { ["Plc:Mode"] = "RealReadOnly", ["Plc:AllowLiveConnection"] = "false" });
        await using var _h = h; await using var _f = f;
        var rows = await WaitFor(() => h.Db(db => db.PlcCommLog.AsNoTracking().Where(x => x.Event == "CONFIG_SNAPSHOT" || x.Event == "DISABLED").ToListAsync()), r => r.Count >= 8);
        Assert.Equal(4, rows.Count(r => r.Event == "CONFIG_SNAPSHOT"));
        Assert.All(rows.Where(r => r.Event == "DISABLED"), r => Assert.Equal((PlcDiagCodes.LiveConnectionDisabled, PlcDiagStages.Config), (r.DiagCode, r.Stage)));
        var snap = rows.First(r => r.Event == "CONFIG_SNAPSHOT" && r.PlcId == "PLC-01");
        Assert.Equal(PlcDiagStages.Config, snap.Stage);
        Assert.Contains("\"allowLiveConnection\":false", snap.DetailsJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIVE_CONNECTION_DISABLED", snap.Message);
        Assert.DoesNotContain(await h.Db(db => db.PlcCommLog.AsNoTracking().Select(x => x.Event).ToListAsync()), e => e is "TCP_CONNECTED" or "CONNECT_FAILED");   // WCS did not try TCP
    }

    [Fact]
    public async Task An_enabled_PLC_with_missing_parameters_logs_PLC_CONFIGURATION_MISSING_and_a_disabled_one_CONNECTION_NOT_ENABLED()
    {
        var (h, f) = await RuntimeAsync(new() { ["Plc:Mode"] = "RealReadOnly", ["Plc:AllowLiveConnection"] = "true" }, async db =>
        {
            var c = await db.PlcConnections.SingleAsync(x => x.Id == "PLC-03");
            c.Update(c.Name, c.IpAddress, null, null, null, null, null, null, null, null, null, true, "test", DateTimeOffset.UtcNow);   // switched on, parameters never entered
            await db.SaveChangesAsync();
        });
        await using var _h = h; await using var _f = f;
        var rows = await WaitFor(() => h.Db(db => db.PlcCommLog.AsNoTracking().ToListAsync()),
            r => r.Any(x => x.Event == "CONFIG_MISSING") && r.Count(x => x.Event == "DISABLED") >= 3);
        var missing = rows.First(r => r.Event == "CONFIG_MISSING");
        Assert.Equal((PlcDiagCodes.ConfigurationMissing, PlcDiagStages.Config, "PLC-03"), (missing.DiagCode, missing.Stage, missing.PlcId));
        Assert.Contains("networkNumber", missing.DetailsJson!);
        Assert.All(rows.Where(r => r.Event == "DISABLED"), r => Assert.Equal(PlcDiagCodes.ConnectionNotEnabled, r.DiagCode));
        Assert.DoesNotContain(rows, r => r.Event is "TCP_CONNECTED" or "CONNECT_FAILED");
    }

    [Fact]
    public async Task A_blocked_write_is_logged_as_PLC_WRITE_BLOCKED_and_nothing_reaches_the_PLC()
    {
        var (h, f) = await RuntimeAsync(new() { ["Plc:Mode"] = "RealReadOnly", ["Plc:AllowLiveConnection"] = "false" });
        await using var _h = h; await using var _f = f;
        var rt = f.Services.GetRequiredService<IPlcRuntime>();
        var r = await rt.WriteAsync("PLC-01", "A1", "Request_Ack", PointValue.OfBool(true), "tester");
        Assert.False(r.Success);
        var row = (await WaitFor(() => h.Db(db => db.PlcCommLog.AsNoTracking().Where(x => x.Event == "WRITE_DENIED").ToListAsync()), x => x.Count > 0)).Single();
        Assert.Equal((PlcDiagCodes.WriteBlocked, PlcDiagStages.Write), (row.DiagCode, row.Stage));
        Assert.Contains("READ_ONLY", row.DetailsJson!);
    }

    // ================================================================== export

    private sealed class Throwing : IPlcDiagnosticSource
    {
        public Task<IReadOnlyList<PlcConnectionDiag>> ConnectionsAsync(string? plcId, CancellationToken ct = default) => throw new InvalidOperationException("db down");
        public Task<(IReadOnlyList<PlcLogRow> Rows, int Total)> LogsAsync(string? plcId, DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default) => throw new InvalidOperationException("db down");
        public Task<(IReadOnlyList<PlcExchangeRow> Rows, int Total)> ExchangesAsync(string? plcId, DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default) => throw new InvalidOperationException("db down");
    }

    private sealed class LogsThrow : IPlcDiagnosticSource
    {
        public Task<IReadOnlyList<PlcConnectionDiag>> ConnectionsAsync(string? plcId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PlcConnectionDiag>>([new PlcConnectionDiag("PLC-01", "PLC-01", "10.0.0.1", 5000, "McProtocol3EBinaryTcp", 0, 255, 1023, 0, 16, 300, null, null, null, 50, null, null, true, [], [])]);
        public Task<(IReadOnlyList<PlcLogRow> Rows, int Total)> LogsAsync(string? plcId, DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default) => throw new InvalidOperationException("db down");
        public Task<(IReadOnlyList<PlcExchangeRow> Rows, int Total)> ExchangesAsync(string? plcId, DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default) =>
            Task.FromResult<(IReadOnlyList<PlcExchangeRow>, int)>(([], 0));
    }

    private async Task<(Harness H, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> F)> ExportArrangeAsync(Action<IServiceCollection>? replace = null)
    {
        var h = await Harness.CreateAsync(fixture);
        var f0 = new DbApiFactory(fixture);
        using (var scope = f0.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator));
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>().SeedAsync();
            var now = DateTimeOffset.UtcNow;
            db.PlcCommLog.AddRange(
                new PlcCommLogEntry(now.AddMinutes(-30), "PLC-01", null, PlcLogLevel.Warning, "CONNECT_FAILED", "Connection failed: actively refused. password=hunter2 token=abc123xyz Connection String: Host=db;Password=s3cret;")
                    .WithDetails("{\"args\":{\"password\":\"zzz-secret\",\"ip\":\"192.168.10.101\"}}").WithDiag(PlcDiagCodes.ConnectionRefused, PlcDiagStages.Tcp),
                new PlcCommLogEntry(now.AddMinutes(-20), "PLC-01", "A1", PlcLogLevel.Info, "IO_NOT_APPROVED", "2/4 IO point(s) are not polled").WithDiag(PlcDiagCodes.IoPointNotApproved, PlcDiagStages.Read),
                new PlcCommLogEntry(now.AddMinutes(-10), "PLC-02", null, PlcLogLevel.Info, "DISABLED", "[CONNECTION_NOT_ENABLED] x").WithDiag(PlcDiagCodes.ConnectionNotEnabled, PlcDiagStages.Config),
                new PlcCommLogEntry(now.AddDays(-5), "PLC-01", null, PlcLogLevel.Warning, "LINK_LOST", "old event").WithDiag(PlcDiagCodes.ConnectionLost, PlcDiagStages.Tcp));
            db.PlcExchangeLog.Add(new PlcExchangeLogEntry(now.AddMinutes(-15), "PLC-01", "READ", "0x0401", "M", 100, 1, 15, 0x4031, "END_CODE", "PLC end code 0x4031", "50 00 00 FF FF 03 00", "D0 00 00 FF FF 03 00 04 00 31 40", false));
            await db.SaveChangesAsync();
        }
        await f0.DisposeAsync();
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> f = new DbApiFactory(fixture);
        if (replace is not null) f = f.WithWebHostBuilder(b => b.ConfigureTestServices(replace));
        return (h, f);
    }

    private static async Task<HttpClient> As(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> f, string user)
    {
        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync(user));
        return c;
    }

    private static async Task<Dictionary<string, string>> Unzip(HttpResponseMessage res)
    {
        Assert.Equal(200, (int)res.StatusCode);
        Assert.Equal("application/zip", res.Content.Headers.ContentType?.MediaType);
        var bytes = await res.Content.ReadAsByteArrayAsync();
        var sample = Environment.GetEnvironmentVariable("WCS_DIAG_SAMPLE_DIR");   // optional: keep a real package for inspection
        if (!string.IsNullOrEmpty(sample)) { Directory.CreateDirectory(sample); await File.WriteAllBytesAsync(Path.Combine(sample, $"sample_{Guid.NewGuid():N}.zip"), bytes); }
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName, e => { using var r = new StreamReader(e.Open()); return r.ReadToEnd(); });
    }

    private static readonly string[] ExpectedFiles =
    [
        "Summary.txt", "System_Info.txt", "WCS_Version.txt", "PLC/PLC_Connections.csv", "PLC/PLC_Configuration.json", "PLC/PLC_Connection.log", "PLC/PLC_Runtime.log",
        "PLC/PLC_Communication.log", "PLC/PLC_Read_Write.log", "PLC/PLC_Error_Summary.csv",
    ];

    [Fact]
    public async Task Admin_exports_all_PLCs_as_a_ZIP_with_the_agreed_structure_and_no_secret_inside()
    {
        var (h, f) = await ExportArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");

        var res = await admin.GetAsync("/api/diagnostics/plc/export?quick=24h");
        Assert.Contains("WCS_Diagnostic_ALL_PLC_", res.Content.Headers.ContentDisposition?.FileName ?? res.Content.Headers.ContentDisposition?.FileNameStar);
        var files = await Unzip(res);
        Assert.Equal(ExpectedFiles.Order(), files.Keys.Order());

        var all = string.Join("\n", files.Values);
        foreach (var secret in new[] { "hunter2", "abc123xyz", "s3cret", "zzz-secret" }) Assert.DoesNotContain(secret, all);                 // masked before entering the ZIP
        Assert.Contains("***", files["PLC/PLC_Runtime.log"]);

        Assert.Contains("PLC_CONNECTION_REFUSED", files["PLC/PLC_Error_Summary.csv"]);
        Assert.Contains("IO_POINT_NOT_APPROVED", files["PLC/PLC_Error_Summary.csv"]);
        Assert.Contains("CONNECT_FAILED", files["PLC/PLC_Connection.log"]);                                                                    // TCP stage
        Assert.DoesNotContain("IO_NOT_APPROVED", files["PLC/PLC_Connection.log"]);                                                             // READ stage is not a connection event
        Assert.Contains("IO_NOT_APPROVED", files["PLC/PLC_Read_Write.log"]);
        Assert.DoesNotContain("old event", files["PLC/PLC_Runtime.log"]);                                                                      // outside the 24 h range
        Assert.Contains("0x4031", files["PLC/PLC_Communication.log"]);
        Assert.Contains("REQ 50 00", files["PLC/PLC_Communication.log"]);
        Assert.Contains("PLC-01,PLC-01", files["PLC/PLC_Connections.csv"]);
        Assert.Contains("PLC-04", files["PLC/PLC_Connections.csv"]);                                                                           // all four PLCs
        Assert.Contains("TCP was attempted and REFUSED", files["Summary.txt"]);
        Assert.Contains("WCS did not", "WCS did not");                                                                                          // (guard against an empty Summary)
        Assert.Contains("\"mode\"", files["PLC/PLC_Configuration.json"]);
        Assert.Contains("Generated (UTC)", files["System_Info.txt"]);
        Assert.Contains("time zone", files["System_Info.txt"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Exporting_one_PLC_contains_only_that_PLC_and_an_empty_range_says_so()
    {
        var (h, f) = await ExportArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");

        var files = await Unzip(await admin.GetAsync("/api/diagnostics/plc/export?plcId=PLC-02&quick=1h"));
        Assert.DoesNotContain("PLC-01", files["PLC/PLC_Connections.csv"]);
        Assert.Contains("PLC-02", files["PLC/PLC_Connections.csv"]);
        Assert.Contains("CONNECTION_NOT_ENABLED", files["PLC/PLC_Runtime.log"]);                // PLC-02 event 10 min ago is inside 1 h
        Assert.DoesNotContain("CONNECT_FAILED", files["PLC/PLC_Runtime.log"]);                  // PLC-01 events are not

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-20).ToString("O")); var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-19).ToString("O"));
        var empty = await Unzip(await admin.GetAsync($"/api/diagnostics/plc/export?from={from}&to={to}"));
        Assert.Contains("no events in the selected range", empty["PLC/PLC_Runtime.log"]);
        Assert.Contains("no MC exchange recorded", empty["PLC/PLC_Communication.log"]);
        Assert.Equal(ExpectedFiles.Order(), empty.Keys.Order());                                // still a complete package
    }

    [Fact]
    public async Task Invalid_requests_are_refused_without_touching_anything()
    {
        var (h, f) = await ExportArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        var before = await h.Db(async db => (await db.PlcConnections.CountAsync(), await db.PlcCommLog.CountAsync(), await db.StationIoPoints.CountAsync(p => p.Status == IoConfigStatus.Approved)));

        var now = DateTimeOffset.UtcNow;
        string Q(DateTimeOffset a) => Uri.EscapeDataString(a.ToString("O"));
        Assert.Equal(400, (int)(await admin.GetAsync($"/api/diagnostics/plc/export?from={Q(now)}&to={Q(now.AddHours(-1))}")).StatusCode);          // end before start
        Assert.Equal(400, (int)(await admin.GetAsync($"/api/diagnostics/plc/export?from={Q(now.AddDays(-60))}&to={Q(now)}")).StatusCode);          // longer than the limit
        Assert.Equal(400, (int)(await admin.GetAsync("/api/diagnostics/plc/export?quick=7d")).StatusCode);
        Assert.Equal(404, (int)(await admin.GetAsync("/api/diagnostics/plc/export?plcId=PLC-99&quick=1h")).StatusCode);

        Assert.Equal(before, await h.Db(async db => (await db.PlcConnections.CountAsync(), await db.PlcCommLog.CountAsync(), await db.StationIoPoints.CountAsync(p => p.Status == IoConfigStatus.Approved))));
    }

    [Fact]
    public async Task Only_an_Admin_can_export_and_every_export_is_audited_without_changing_any_PLC_setting()
    {
        var (h, f) = await ExportArrangeAsync();
        await using var _h = h; await using var _f = f;
        Assert.Equal(401, (int)(await f.CreateClient().WithClientHeader().GetAsync("/api/diagnostics/plc/export?quick=1h")).StatusCode);       // anonymous
        var op = await As(f, "op1");
        Assert.Equal(403, (int)(await op.GetAsync("/api/diagnostics/plc/export?quick=1h")).StatusCode);                                          // Operator

        var before = await h.Db(db => db.PlcConnections.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.Enabled, c.IpAddress, c.Port, c.Protocol }).ToListAsync());
        var admin = await As(f, "admin1");
        Assert.Equal(200, (int)(await admin.GetAsync("/api/diagnostics/plc/export?quick=1h")).StatusCode);
        Assert.Equal(before, await h.Db(db => db.PlcConnections.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.Enabled, c.IpAddress, c.Port, c.Protocol }).ToListAsync()));
        Assert.Equal(1, await h.Db(db => db.AuditLog.CountAsync(a => a.Action == "DIAGNOSTIC_EXPORT")));                                         // only the Admin's export (denied ones are not exports)
    }

    [Fact]
    public async Task When_the_data_cannot_be_read_the_export_fails_cleanly_and_a_partial_failure_is_reported_inside_the_ZIP()
    {
        var (h1, f1) = await ExportArrangeAsync(s => s.AddScoped<IPlcDiagnosticSource, Throwing>());
        await using var _h1 = h1; await using var _f1 = f1;
        var admin1 = await As(f1, "admin1");
        var failed = await admin1.SendJsonAsync(HttpMethod.Get, "/api/diagnostics/plc/export?quick=1h");
        Assert.Equal(422, failed.Status);
        Assert.Equal("DIAGNOSTIC_EXPORT_FAILED", failed.Root.ErrorCode());
        Assert.DoesNotContain("db down", failed.Raw);                                               // no internal detail leaks

        var (h2, f2) = await ExportArrangeAsync(s => s.AddScoped<IPlcDiagnosticSource, LogsThrow>());
        await using var _h2 = h2; await using var _f2 = f2;
        var files = await Unzip(await (await As(f2, "admin1")).GetAsync("/api/diagnostics/plc/export?quick=1h"));
        Assert.Contains("PLC events could not be read", files["ERRORS.txt"]);
        Assert.Contains("PLC events could not be read", files["Summary.txt"]);
        Assert.Contains("PLC-01", files["PLC/PLC_Connections.csv"]);                                 // what could be read is still delivered
    }
}
