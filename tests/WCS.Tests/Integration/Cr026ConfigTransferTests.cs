using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Application.Auth;
using WCS.Application.Common;
using WCS.Application.ConfigTransfer;
using WCS.Application.Routing;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Plc;
using WCS.Domain.Routing;
using WCS.Domain.Users;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-026 steps 3-7: export, package validation, preview, conflicts, backup, apply, rollback - between TWO real PostgreSQL databases (different Guids everywhere).
/// </summary>
[Collection("Database")]
public class Cr026ConfigTransferTests(DatabaseFixture fixture)
{
    private static readonly ActorInfo Admin = new(Guid.NewGuid(), "admin", UserRole.Admin);
    private static readonly string[] All = [ConfigGroupIds.Location, ConfigGroupIds.Agv, ConfigGroupIds.PlcStation, ConfigGroupIds.BusinessProcess];
    private static readonly string[] A12 = ["A1", "A2"];

    /// <summary>Two systems (A = source, B = target) and a persistent (non-temporary) folder for B.</summary>
    private sealed class Systems : IAsyncDisposable
    {
        public required RoutingKit A { get; init; }
        public required RoutingKit B { get; init; }
        public required DatabaseFixture SecondDb { get; init; }
        public required string Dir { get; init; }
        public string Incoming => Path.Combine(Dir, "incoming");
        public async ValueTask DisposeAsync()
        {
            await A.DisposeAsync(); await B.DisposeAsync(); await SecondDb.DisposeAsync();
            try { Directory.Delete(Dir, true); } catch (IOException) { /* best effort */ }
        }
    }

    private static async Task<Systems> CreateAsync(DatabaseFixture first, bool withBackupPath = true, bool processOnA = true)
    {
        var second = new DatabaseFixture();
        await second.InitializeAsync();
        var dir = Path.Combine(AppContext.BaseDirectory, "cfgtransfer-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        void Opts(IServiceCollection s) => s.Configure<ConfigTransferOptions>(o => { o.Enabled = true; o.BackupPath = withBackupPath ? dir : null; o.MinFreeMb = 1; });
        var a = await RoutingKit.CreateAsync(first, more: Opts);
        var b = await RoutingKit.CreateAsync(second, more: Opts);
        var sys = new Systems { A = a, B = b, SecondDb = second, Dir = dir };
        if (processOnA)
        {
            var (_, v) = await a.CreateProcessAsync("T-PKG", RequestType.PickupRequest, RackKind.Full, A12, OneLeg("Request", Dest(1, "B1")));
            await a.PublishAsync(v);
        }
        return sys;
    }

    private static Task<T> On<T>(RoutingKit k, Func<ConfigTransferService, Task<T>> f) => k.With(sp => f(sp.GetRequiredService<ConfigTransferService>()));
    private static Task<MaintenanceView> Maintenance(RoutingKit k, bool on) => k.With(sp => sp.GetRequiredService<MaintenanceService>().SetAsync(on, "test", Admin));

    private static async Task<byte[]> ExportAsync(RoutingKit k, params string[] groups) => (await On(k, s => s.ExportAsync(groups.Length == 0 ? All : groups, Admin))).Data;
    private static async Task<(Guid Id, ImportPlan Plan)> UploadAndPreviewAsync(RoutingKit k, byte[] package, Selection? sel = null)
    {
        var up = await On(k, s => s.UploadAsync(package, Admin));
        return (up.ImportId, await On(k, s => s.PreviewAsync(up.ImportId, sel, Admin)));
    }
    private static Task<ApplyResult> ApplyAsync(RoutingKit k, Guid id, ImportPlan plan, bool confirm = true, bool skipConflicts = false) =>
        On(k, s => s.ApplyAsync(id, new ApplyRequest(plan.PreviewToken!, confirm, skipConflicts), Admin));

    /// <summary>Makes B differ from A on every kind of record the MVP transfers.</summary>
    private static async Task DiverseAsync(Systems s)
    {
        await s.A.H.SetCapacityAsync("B1", 4);                                                                 // A: B1 has capacity 4
        await s.B.H.SetCapacityAsync("A1", 5);                                                                 // B: A1 has capacity 5 (A: 1)
        await s.B.H.Db(async db => { (await db.Agvs.SingleAsync(a => a.Code == "AGV1")).SetRobotCode("OTHER-ROBOT"); await db.SaveChangesAsync(); return 0; });
        await s.B.With(sp => sp.GetRequiredService<WCS.Application.Plc.PlcProvisioningService>().SetPortLinkAsync("A2", null, false));   // B: A2 unlinked and disabled
    }

    // ================================================================================================ export + package
    [Fact]
    public async Task Export_contains_only_configuration_with_natural_keys_no_guid_no_secret_no_approval_state_and_verifiable_checksums()
    {
        await using var s = await CreateAsync(fixture);
        var bytes = await ExportAsync(s.A);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal(new[] { "business-processes.json", "agvs.json", "checksums.sha256", "locations.json", "manifest.json", "plc-stations.json", "README.txt" }.Order(), zip.Entries.Select(e => e.FullName).Order());
        // the DATA files only: the README and the manifest legitimately name what is excluded
        var text = string.Join("\n", zip.Entries.Where(e => e.FullName.EndsWith(".json") && e.FullName != "manifest.json").Select(e => { using var r = new StreamReader(e.Open()); return r.ReadToEnd(); }));
        Assert.DoesNotMatch(new Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"), text);      // no database id of any kind
        foreach (var forbidden in new[] { "passwordHash", "securityStamp", "protectedSecrets", "\"token\"", "\"password\"", "writeAllowed", "approvedBy", "writeApproval", "xmin", "dispatchHold", "reconciliationRequired" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        var manifest = JsonNode.Parse(zip.GetEntry("manifest.json")!.Open())!;
        Assert.Equal("1.0", manifest["formatVersion"]!.GetValue<string>());
        Assert.Equal(4, manifest["groups"]!.AsArray().Count);
        Assert.NotEmpty(manifest["fileConfig"]!["sha256"]!.GetValue<string>());                                                  // D3: fingerprint of appsettings, nothing imported
        Assert.Contains(manifest["excluded"]!.AsArray(), x => x!["name"]!.GetValue<string>().Contains("User accounts"));
    }

    [Fact]
    public async Task Selective_export_reports_the_groups_it_depends_on_and_lets_the_user_decide()
    {
        await using var s = await CreateAsync(fixture);
        var report = (await On(s.A, x => Task.FromResult(x.CheckExport([ConfigGroupIds.BusinessProcess]))));
        Assert.Equal([ConfigGroupIds.Location, ConfigGroupIds.PlcStation], report.Missing);                                      // dependency order, derived from the declared dependencies
        var without = await On(s.A, x => x.ExportAsync([ConfigGroupIds.BusinessProcess], Admin));
        Assert.Equal(2, without.Manifest.UnresolvedReferences.Count);                                                             // the package says what it could not carry
        var with = await On(s.A, x => x.ExportAsync([ConfigGroupIds.BusinessProcess, ConfigGroupIds.Location, ConfigGroupIds.PlcStation], Admin));
        Assert.Empty(with.Manifest.UnresolvedReferences);
        Assert.Equal([ConfigGroupIds.Location, ConfigGroupIds.PlcStation, ConfigGroupIds.BusinessProcess], with.Manifest.Groups.Select(g => g.Group).ToArray());
    }

    [Fact]
    public async Task A_damaged_or_hostile_package_is_refused_at_upload()
    {
        await using var s = await CreateAsync(fixture);
        var good = await ExportAsync(s.A);

        static byte[] Rebuild(byte[] source, Func<string, byte[], (string Name, byte[] Data)?> change, params (string, byte[])[] extra)
        {
            using var inZip = new ZipArchive(new MemoryStream(source));
            using var ms = new MemoryStream();
            using (var outZip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var e in inZip.Entries)
                {
                    using var r = new MemoryStream(); e.Open().CopyTo(r);
                    var c = change(e.FullName, r.ToArray());
                    if (c is null) continue;
                    var ne = outZip.CreateEntry(c.Value.Name); using var w = ne.Open(); w.Write(c.Value.Data);
                }
                foreach (var (n, d) in extra) { var ne = outZip.CreateEntry(n); using var w = ne.Open(); w.Write(d); }
            }
            return ms.ToArray();
        }
        async Task Refused(byte[] bytes, string because)
        {
            var ex = await Assert.ThrowsAsync<ConfigTransferException>(() => On(s.B, x => x.UploadAsync(bytes, Admin)));
            Assert.Equal("PACKAGE_INVALID", ex.Code);
            Assert.Contains(because, ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        await Refused(Encoding.UTF8.GetBytes("not a zip"), "ZIP");
        await Refused(Rebuild(good, (n, d) => (n, d), ("..\\evil.json", [1])), "not allowed");
        await Refused(Rebuild(good, (n, d) => (n, d), ("extra.json", [1])), "not part of the package format");
        await Refused(Rebuild(good, (n, d) => n == "locations.json" ? (n, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(d).Replace("\"name\"", "\"nom\""))) : (n, d)), "modified");
        await Refused(Rebuild(good, (n, d) => n == "manifest.json" ? (n, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(d).Replace("\"1.0\"", "\"2.0\""))) : (n, d)), "format");
        await Refused(Rebuild(good, (n, d) => n == "checksums.sha256" ? null : (n, d)), "checksums.sha256");
        // a record the schema check rejects (bad capacity) with correct checksums: the package builder is used so every checksum is valid
        var broken = ConfigPackage.Write(new PackageManifest("1.0", DateTimeOffset.UtcNow, "x", "t", "m", "", [], [], [], [], new FileConfigInfo("", [])),
            [(ConfigGroupIds.Location, "locations.json", [new ConfigRecord("L1", new JsonObject { ["name"] = "n", ["type"] = "Buffer", ["rackCapacity"] = 5000, ["allowedRackKind"] = "Any", ["enabled"] = true, ["requiresPlcPermission"] = false })])]);
        await Refused(broken, "rackCapacity");
        // duplicate key
        var dup = ConfigPackage.Write(new PackageManifest("1.0", DateTimeOffset.UtcNow, "x", "t", "m", "", [], [], [], [], new FileConfigInfo("", [])),
            [(ConfigGroupIds.Location, "locations.json", [Loc("L1"), Loc("L1")])]);
        await Refused(dup, "more than once");
    }

    private static ConfigRecord Loc(string code, int cap = 1, string type = "Buffer") => new(code, new JsonObject { ["name"] = code, ["type"] = type, ["rackCapacity"] = cap, ["allowedRackKind"] = "Any", ["enabled"] = true, ["requiresPlcPermission"] = false });

    // ================================================================================================ preview
    [Fact]
    public async Task Preview_shows_create_update_unchanged_skip_and_conflict_with_the_field_changes_and_changes_nothing()
    {
        await using var s = await CreateAsync(fixture);
        await DiverseAsync(s);
        var pkg = ConfigPackage.Write(new PackageManifest("1.0", DateTimeOffset.UtcNow, "x", "t", "m", "", [], [], [], [], new FileConfigInfo("", [])), [
            (ConfigGroupIds.Location, "locations.json", [Loc("N1"), Loc("A1", 1, "Station"), Loc("F1", 9), Loc("F2", 9)]),          // N1 create; A1 differs from B (5 vs 1); F1/F2 update; F2 will be excluded
            (ConfigGroupIds.Agv, "agvs.json", [new ConfigRecord("AGV1", new JsonObject { ["role"] = "FullRack", ["rcsRobotCode"] = "R-FULL", ["enabled"] = true }),
                                                new ConfigRecord("AGV9", new JsonObject { ["role"] = "FullRack", ["rcsRobotCode"] = null, ["enabled"] = true })]),
        ]);
        var before = await s.B.H.Db(db => db.Locations.AsNoTracking().OrderBy(l => l.Code).Select(l => new { l.Code, l.RackCapacity }).ToListAsync());
        var (_, plan) = await UploadAndPreviewAsync(s.B, pkg, new Selection(null, [new RecordRef(ConfigGroupIds.Location, "F2")]));

        PlanItem Item(string g, string k) => plan.Items.Single(i => i.Group == g && i.Key == k);
        Assert.Equal(PlanAction.Create, Item(ConfigGroupIds.Location, "N1").Action);
        Assert.Equal(PlanAction.Update, Item(ConfigGroupIds.Location, "A1").Action);
        var cap = Item(ConfigGroupIds.Location, "A1").Changes.Single(c => c.Field == "rackCapacity");
        Assert.Equal(("5", "1"), (cap.From, cap.To));
        Assert.Equal(PlanAction.Skip, Item(ConfigGroupIds.Location, "F2").Action);
        Assert.Equal(PlanAction.Update, Item(ConfigGroupIds.Agv, "AGV1").Action);                                          // robot code OTHER-ROBOT -> R-FULL
        var missing = Item(ConfigGroupIds.Agv, "AGV9");
        Assert.Equal(PlanAction.Conflict, missing.Action); Assert.Equal("AGV_NOT_FOUND", missing.ConflictCode);            // never creates an AGV
        Assert.Equal(PlanAction.Update, Item(ConfigGroupIds.Location, "F1").Action);
        Assert.True(plan.Summary.Skip >= 1 && plan.Summary.Conflict == 1 && plan.Summary.Create == 1);
        Assert.NotNull(plan.PreviewToken);
        Assert.Equal(before, await s.B.H.Db(db => db.Locations.AsNoTracking().OrderBy(l => l.Code).Select(l => new { l.Code, l.RackCapacity }).ToListAsync()));   // Preview wrote nothing
        Assert.Equal(1, await s.B.H.Db(db => db.ConfigTransferSessions.CountAsync()));
    }

    [Fact]
    public async Task A_record_that_needs_a_location_the_target_lacks_is_a_missing_dependency_unless_the_location_is_in_the_selected_import()
    {
        await using var s = await CreateAsync(fixture);
        // process uses location Z9 which B does not have
        var def = new VersionDefinition("s1", [Move("s1", 1, "Delivery", "Request", "Candidates", "done", [Dest(1, "Z9")]), Done()]);
        var proc = new ConfigRecord("T-Z9", new JsonObject
        {
            ["name"] = "Z", ["description"] = null, ["requestType"] = "PickupRequest", ["rackKind"] = "Full", ["triggerLocationCodes"] = new JsonArray("A1"), ["enabled"] = true, ["reviewBlocked"] = false,
            ["reviewReason"] = null, ["definitionSource"] = new JsonObject { ["number"] = 1, ["state"] = "Draft" }, ["definition"] = JsonSerializer.SerializeToNode(def, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        });
        var m = new PackageManifest("1.0", DateTimeOffset.UtcNow, "x", "t", "m", "", [], [], [], [], new FileConfigInfo("", []));
        var onlyProcess = ConfigPackage.Write(m, [(ConfigGroupIds.BusinessProcess, "business-processes.json", [proc])]);
        var (_, p1) = await UploadAndPreviewAsync(s.B, onlyProcess);
        Assert.Equal("MISSING_DEPENDENCY", p1.Items.Single().ConflictCode);

        var both = ConfigPackage.Write(m, [(ConfigGroupIds.Location, "locations.json", [Loc("Z9")]), (ConfigGroupIds.BusinessProcess, "business-processes.json", [proc])]);
        var (id2, p2) = await UploadAndPreviewAsync(s.B, both);
        Assert.Equal([ConfigGroupIds.Location, ConfigGroupIds.BusinessProcess], p2.GroupOrder);                                // the import orders itself by dependency
        Assert.All(p2.Items, i => Assert.Equal(PlanAction.Create, i.Action));
        // leave the location out and the process becomes a conflict again
        var (_, p3) = await UploadAndPreviewAsync(s.B, both, new Selection(null, [new RecordRef(ConfigGroupIds.Location, "Z9")]));
        Assert.Equal("MISSING_DEPENDENCY", p3.Items.Single(i => i.Group == ConfigGroupIds.BusinessProcess).ConflictCode);
        _ = id2;
    }

    // ================================================================================================ apply: the whole way
    [Fact]
    public async Task Import_all_between_two_systems_updates_through_the_services_keeps_approvals_and_maintenance_and_publishes_nothing()
    {
        await using var s = await CreateAsync(fixture);
        await DiverseAsync(s);
        var approvedBefore = await s.B.H.Db(db => db.StationIoPoints.AsNoTracking().OrderBy(p => p.StationId).Select(p => new { p.StationId, p.Status, p.WriteAllowed, p.WriteApproval, p.ActualAddress }).ToListAsync());
        var (id, plan) = await UploadAndPreviewAsync(s.B, await ExportAsync(s.A));
        Assert.Equal(PlanAction.Create, plan.Items.Single(i => i.Group == ConfigGroupIds.BusinessProcess).Action);
        Assert.Equal(4, plan.GroupOrder.Count);

        // not allowed without Maintenance
        var noMaint = await Assert.ThrowsAsync<ConfigTransferException>(() => ApplyAsync(s.B, id, plan));
        Assert.Equal("MAINTENANCE_REQUIRED", noMaint.Code);
        Assert.Equal(5, (await s.B.H.LocationAsync("A1")).RackCapacity);                                                      // still untouched

        await Maintenance(s.B, true);
        var result = await ApplyAsync(s.B, id, plan);
        Assert.False(result.AlreadyApplied);
        Assert.True(result.Updated >= 4 && result.Created == 1);

        Assert.Equal(1, (await s.B.H.LocationAsync("A1")).RackCapacity);                                                      // B now equals A
        Assert.Equal(4, (await s.B.H.LocationAsync("B1")).RackCapacity);
        Assert.Equal("R-FULL", (await s.B.H.AgvAsync("AGV1")).RcsRobotCode);
        var a2 = await s.B.H.Db(db => db.PlcStations.AsNoTracking().SingleAsync(x => x.Id == "A2"));
        Assert.True(a2.Enabled); Assert.Equal("A2", a2.LocationCode);
        // the Business Process arrived as a DRAFT: not validated, not published
        var versions = await s.B.H.Db(db => db.BusinessProcessVersions.AsNoTracking().Where(v => db.BusinessProcesses.Any(p => p.Id == v.ProcessId && p.Code == "T-PKG")).ToListAsync());
        Assert.Single(versions); Assert.Equal(ProcessConfigState.Draft, versions[0].State); Assert.Null(versions[0].PublishedAt);
        // approval state of the IO points is exactly what it was: nothing was approved, rejected or reset
        Assert.Equal(approvedBefore, await s.B.H.Db(db => db.StationIoPoints.AsNoTracking().OrderBy(p => p.StationId).Select(p => new { p.StationId, p.Status, p.WriteAllowed, p.WriteApproval, p.ActualAddress }).ToListAsync()));
        // Maintenance is still ON, a backup file exists and is a valid package, the session is Applied, the audit has the entries
        Assert.True((await s.B.With(sp => sp.GetRequiredService<MaintenanceService>().GetAsync())).Enabled);
        Assert.True(File.Exists(Path.Combine(s.Dir, result.BackupFile)));
        var backup = ConfigPackage.Read(File.ReadAllBytes(Path.Combine(s.Dir, result.BackupFile)), new Dictionary<string, string> { [ConfigGroupIds.Location] = "locations.json", [ConfigGroupIds.Agv] = "agvs.json", [ConfigGroupIds.PlcStation] = "plc-stations.json", [ConfigGroupIds.BusinessProcess] = "business-processes.json" },
            new ConfigTransferOptions(), (_, _) => []);
        Assert.Contains(backup.Groups[ConfigGroupIds.Location], r => r.Key == "A1" && r.Fields["rackCapacity"]!.GetValue<int>() == 5);   // the OLD value is in the backup
        Assert.Contains("CREATED_BY_IMPORT: BusinessProcess/T-PKG", backup.Manifest.Warnings);
        Assert.Equal(ConfigImportStatus.Applied, (await s.B.H.Db(db => db.ConfigTransferSessions.AsNoTracking().SingleAsync(x => x.Id == id))).Status);
        var actions = await s.B.H.Db(db => db.AuditLog.AsNoTracking().Select(a => a.Action).ToListAsync());
        Assert.Contains("CONFIG_IMPORT_APPLY", actions); Assert.Contains("CONFIG_LOCATION_UPDATE", actions); Assert.Contains("CONFIG_AGV_UPDATE", actions); Assert.Contains("CONFIG_PROCESS_IMPORT_CREATE", actions);

        // idempotent: the same import is never applied twice
        var again = await ApplyAsync(s.B, id, plan);
        Assert.True(again.AlreadyApplied); Assert.Equal(result.Created, again.Created);
        // a second import of the same package now finds nothing to do
        var (_, plan2) = await UploadAndPreviewAsync(s.B, await ExportAsync(s.A));
        Assert.True(plan2.Summary.Create == 0 && plan2.Summary.Update == 0);
    }

    [Fact]
    public async Task Apply_is_refused_with_the_reasons_while_work_is_active_and_nothing_is_cancelled_or_changed()
    {
        await using var s = await CreateAsync(fixture);
        await DiverseAsync(s);
        await s.B.H.StockAsync("A1", RackKind.Full, 1);
        var (id, plan) = await UploadAndPreviewAsync(s.B, await ExportAsync(s.A));
        var (_, v) = await s.B.CreateProcessAsync("T-ACT", RequestType.PickupRequest, RackKind.Full, A12, OneLeg("Request", Dest(1, "B1"))); await s.B.PublishAsync(v);
        await s.B.StartAsync("active-1", "A1", RequestType.PickupRequest, RackKind.Full);                                     // an active Order + Leg
        await s.B.H.CreateTaskAsync("BP01", "A1", "legacy-1");                                                                 // and a legacy Task (flow may be refused: ignore)
            // (the legacy task is optional here; the order alone must block)
        await Maintenance(s.B, true);

        // the Preview was taken before: its readiness reflects the moment it was built; the new Preview shows the blockers
        var fresh = await On(s.B, x => x.PreviewAsync(id, null, Admin));
        Assert.False(fresh.Readiness.CanApply);
        Assert.Contains(fresh.Readiness.Blockers, b => b.Code == "ACTIVE_ORDERS");
        var ex = await Assert.ThrowsAsync<ConfigTransferException>(() => ApplyAsync(s.B, id, fresh));
        Assert.Equal("APPLY_BLOCKED", ex.Code);
        var blockers = Assert.IsAssignableFrom<IReadOnlyList<Blocker>>(ex.Details);
        Assert.Contains(blockers, b => b.Code == "ACTIVE_ORDERS" && b.Count >= 1);
        Assert.Equal(5, (await s.B.H.LocationAsync("A1")).RackCapacity);                                                      // nothing changed
        Assert.Equal(0, await s.B.H.Db(db => db.TransportOrders.CountAsync(o => o.State == OrderState.Cancelled)));          // nothing was cancelled to pass the check
        _ = plan;
    }

    [Fact]
    public async Task The_preview_token_is_bound_to_the_package_and_to_the_database_state_and_expires()
    {
        await using var s = await CreateAsync(fixture);
        await DiverseAsync(s);
        await Maintenance(s.B, true);
        var (id, plan) = await UploadAndPreviewAsync(s.B, await ExportAsync(s.A));

        var wrong = await Assert.ThrowsAsync<AppException>(() => On(s.B, x => x.ApplyAsync(id, new ApplyRequest("0000", true), Admin)));
        Assert.Equal("PREVIEW_TOKEN_INVALID", wrong.Code);

        await s.B.H.SetCapacityAsync("F1", 7);                                                                                 // the target changes after the Preview
        var stale = await Assert.ThrowsAsync<AppException>(() => ApplyAsync(s.B, id, plan));
        Assert.Equal("PLAN_STALE", stale.Code);
        Assert.Equal(5, (await s.B.H.LocationAsync("A1")).RackCapacity);

        var (id2, plan2) = await UploadAndPreviewAsync(s.B, await ExportAsync(s.A));                                           // preview again: works
        s.B.H.Clock.Advance(TimeSpan.FromMinutes(31));
        var expired = await Assert.ThrowsAsync<AppException>(() => ApplyAsync(s.B, id2, plan2));
        Assert.Equal("PREVIEW_EXPIRED", expired.Code);
    }

    [Fact]
    public async Task Conflicts_must_be_excluded_or_explicitly_skipped_and_header_changes_must_be_confirmed()
    {
        await using var s = await CreateAsync(fixture);
        // target has the process with a different name and trigger set, plus a Published version
        var (pid, v) = await s.B.CreateProcessAsync("T-PKG", RequestType.PickupRequest, RackKind.Full, ["A1"], OneLeg("Request", Dest(1, "B2"))); await s.B.PublishAsync(v);
        var pkg = ConfigPackage.Write(new PackageManifest("1.0", DateTimeOffset.UtcNow, "x", "t", "m", "", [], [], [], [], new FileConfigInfo("", [])), [
            (ConfigGroupIds.Agv, "agvs.json", [new ConfigRecord("AGV9", new JsonObject { ["role"] = "FullRack", ["rcsRobotCode"] = null, ["enabled"] = true })]),
        ]);
        await Maintenance(s.B, true);
        var (idC, planC) = await UploadAndPreviewAsync(s.B, pkg);
        var c = await Assert.ThrowsAsync<ConfigTransferException>(() => ApplyAsync(s.B, idC, planC));
        Assert.Equal("PLAN_HAS_CONFLICTS", c.Code);
        var ok = await ApplyAsync(s.B, idC, planC, skipConflicts: true);                                                       // explicitly skipped: reported, nothing created
        Assert.Equal(1, ok.ConflictsSkipped); Assert.Equal(0, await s.B.H.Db(db => db.Agvs.CountAsync(a => a.Code == "AGV9")));

        // CR-042: while a version is Published its trigger locations are locked, and the Preview says so instead of the Apply failing
        var (_, locked) = await UploadAndPreviewAsync(s.B, await ExportAsync(s.A, ConfigGroupIds.Location, ConfigGroupIds.PlcStation, ConfigGroupIds.BusinessProcess));
        Assert.Equal("PROCESS_HEADER_LOCKED", locked.Items.Single(i => i.Group == ConfigGroupIds.BusinessProcess).ConflictCode);
        await s.B.With(sp => sp.GetRequiredService<ProcessAdminService>().DisableAsync(v));

        var (idH, planH) = await UploadAndPreviewAsync(s.B, await ExportAsync(s.A, ConfigGroupIds.Location, ConfigGroupIds.PlcStation, ConfigGroupIds.BusinessProcess));
        var proc = planH.Items.Single(i => i.Group == ConfigGroupIds.BusinessProcess);
        Assert.Equal(PlanAction.Update, proc.Action); Assert.True(proc.HeaderChange);
        Assert.Contains(proc.Changes, x => x.Field == "triggerLocationCodes");                                                // each header field is listed on its own
        Assert.Contains(proc.Changes, x => x.Field == "definition");
        var unconfirmed = await Assert.ThrowsAsync<ConfigTransferException>(() => ApplyAsync(s.B, idH, planH, confirm: false));
        Assert.Equal("HEADER_CHANGES_NOT_CONFIRMED", unconfirmed.Code);
        await ApplyAsync(s.B, idH, planH, confirm: true);
        var versions = await s.B.H.Db(db => db.BusinessProcessVersions.AsNoTracking().Where(x => x.ProcessId == pid).OrderBy(x => x.Number).ToListAsync());
        Assert.Equal([ProcessConfigState.Disabled, ProcessConfigState.Draft], versions.Select(x => x.State).ToArray());       // the earlier version is kept, a new Draft was added
        Assert.Equal(v, versions[0].Id);
    }

    [Fact]
    public async Task The_review_block_of_a_process_is_never_changed_and_a_review_blocked_process_is_not_created()
    {
        await using var s = await CreateAsync(fixture);
        var (pid, _) = await s.B.CreateProcessAsync("T-PKG", RequestType.PickupRequest, RackKind.Full, A12, OneLeg("Request", Dest(1, "B2")));
        await s.B.H.Db(async db => { (await db.BusinessProcesses.SingleAsync(p => p.Id == pid)).MarkReview(true, "BP-04 review", "slide 8", s.B.H.Clock.UtcNow); await db.SaveChangesAsync(); return 0; });
        await Maintenance(s.B, true);
        var (id, plan) = await UploadAndPreviewAsync(s.B, await ExportAsync(s.A, ConfigGroupIds.Location, ConfigGroupIds.PlcStation, ConfigGroupIds.BusinessProcess));
        await ApplyAsync(s.B, id, plan);
        var p = await s.B.H.Db(db => db.BusinessProcesses.AsNoTracking().SingleAsync(x => x.Id == pid));
        Assert.True(p.ReviewBlocked); Assert.Equal("BP-04 review", p.ReviewReason);                                          // untouched (the source did not have the block)
        // a package whose process carries the block is not created here
        var blocked = new ConfigRecord("T-BLOCK", new JsonObject
        {
            ["name"] = "B", ["description"] = null, ["requestType"] = "PickupRequest", ["rackKind"] = "Full", ["triggerLocationCodes"] = new JsonArray("A1"), ["enabled"] = true, ["reviewBlocked"] = true,
            ["reviewReason"] = "review", ["definitionSource"] = null,
            ["definition"] = JsonSerializer.SerializeToNode(OneLeg("Request", Dest(1, "B1")), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        });
        var pkg = ConfigPackage.Write(new PackageManifest("1.0", DateTimeOffset.UtcNow, "x", "t", "m", "", [], [], [], [], new FileConfigInfo("", [])), [(ConfigGroupIds.BusinessProcess, "business-processes.json", [blocked])]);
        var (_, plan2) = await UploadAndPreviewAsync(s.B, pkg);
        Assert.Equal("PROCESS_REVIEW_BLOCKED_NOT_TRANSFERRABLE", plan2.Items.Single().ConflictCode);
    }

    // ================================================================================================ rollback
    private sealed class FailingGroup : IConfigGroup
    {
        public string Id => "ZzTest"; public string File => "zz-test.json"; public IReadOnlyList<string> DependsOn => [ConfigGroupIds.BusinessProcess]; public bool UpdateOnly => false;
        public IReadOnlyList<string> Validate(ConfigRecord record) => [];
        public Task<IReadOnlyList<ConfigRecord>> ReadAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ConfigRecord>>([]);
        public IReadOnlyList<FieldChange> Diff(ConfigRecord package, ConfigRecord target) => [];
        public Task<(string Code, string Message)?> CheckAsync(ConfigRecord package, ConfigRecord? target, PlanContext ctx, CancellationToken ct) => Task.FromResult<(string, string)?>(null);
        public Task ApplyAsync(IReadOnlyList<(PlanItem Item, ConfigRecord Package, ConfigRecord? Target)> items, ApplyContext ctx, CancellationToken ct) => throw new InvalidOperationException("forced failure after the other groups were written");
    }

    [Fact]
    public async Task A_failure_in_the_last_group_rolls_back_every_group_and_the_audit_and_leaves_maintenance_on()
    {
        var second = new DatabaseFixture(); await second.InitializeAsync();
        var dir = Path.Combine(AppContext.BaseDirectory, "cfgtransfer-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(dir);
        void Opts(IServiceCollection x) { x.Configure<ConfigTransferOptions>(o => { o.Enabled = true; o.BackupPath = dir; o.MinFreeMb = 1; }); x.AddScoped<IConfigGroup, FailingGroup>(); }
        await using var a = await RoutingKit.CreateAsync(fixture, more: Opts);
        await using var b = await RoutingKit.CreateAsync(second, more: Opts);
        try
        {
            await a.H.SetCapacityAsync("B1", 4);
            var (_, v) = await a.CreateProcessAsync("T-PKG", RequestType.PickupRequest, RackKind.Full, A12, OneLeg("Request", Dest(1, "B1"))); await a.PublishAsync(v);
            var export = await ExportAsync(a);
            // the package carries a record for the failing group too
            var content = ConfigPackage.Read(export, new Dictionary<string, string> { [ConfigGroupIds.Location] = "locations.json", [ConfigGroupIds.Agv] = "agvs.json", [ConfigGroupIds.PlcStation] = "plc-stations.json", [ConfigGroupIds.BusinessProcess] = "business-processes.json" }, new ConfigTransferOptions(), (_, _) => []);
            var groups = content.Groups.Select(g => (g.Key, g.Key switch { "Location" => "locations.json", "Agv" => "agvs.json", "PlcStation" => "plc-stations.json", _ => "business-processes.json" }, g.Value)).ToList();
            groups.Add(("ZzTest", "zz-test.json", [new ConfigRecord("boom", new JsonObject { ["x"] = 1 })]));
            var pkg = ConfigPackage.Write(content.Manifest, groups);
            await b.H.SetCapacityAsync("B1", 1);
            await Maintenance(b, true);
            var (id, plan) = await UploadAndPreviewAsync(b, pkg);
            Assert.Equal("ZzTest", plan.GroupOrder.Last());
            await Assert.ThrowsAsync<InvalidOperationException>(() => ApplyAsync(b, id, plan));

            Assert.Equal(1, (await b.H.LocationAsync("B1")).RackCapacity);                                                    // the Location group had been written: rolled back
            Assert.Equal(0, await b.H.Db(db => db.BusinessProcesses.CountAsync(p => p.Code == "T-PKG")));                       // the Process group too
            Assert.Equal(0, await b.H.Db(db => db.AuditLog.CountAsync(x => x.Action.StartsWith("CONFIG_LOCATION") || x.Action.StartsWith("CONFIG_PROCESS"))));   // and the audit entries written with them
            var failed = await b.H.Db(db => db.ConfigTransferSessions.AsNoTracking().SingleAsync(x => x.Id == id));
            Assert.Equal(ConfigImportStatus.Failed, failed.Status); Assert.Null(failed.PreviewToken);                           // a failed write needs a new Preview
            Assert.True(File.Exists(Path.Combine(dir, "backups", $"{id:N}.zip")));                                              // the backup had been taken (and verified) before the first write
            Assert.True((await b.With(sp => sp.GetRequiredService<MaintenanceService>().GetAsync())).Enabled);                  // never switched off by the import
            Assert.Contains(await b.H.Db(db => db.AuditLog.AsNoTracking().Select(x => x.Action + ":" + x.Result).ToListAsync()), x => x == "CONFIG_IMPORT_APPLY:Failed");
        }
        finally { await second.DisposeAsync(); try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    // ================================================================================================ the persistent folder
    [Fact]
    public async Task Without_a_usable_persistent_folder_nothing_is_uploaded_or_applied()
    {
        await using var s = await CreateAsync(fixture, withBackupPath: false);
        var pkg = await ExportAsync(s.A);
        var ex = await Assert.ThrowsAsync<AppException>(() => On(s.B, x => x.UploadAsync(pkg, Admin)));
        Assert.Equal("BACKUP_NOT_READY", ex.Code);
        Assert.Contains("NEEDS_CONFIGURATION", ex.Message);

        var service = s.B.H.Services.GetRequiredService<IServiceScopeFactory>().CreateScope().ServiceProvider.GetRequiredService<ConfigBackupService>();
        Assert.NotNull(service.Problem());
        var temp = new ConfigBackupService(Microsoft.Extensions.Options.Options.Create(new ConfigTransferOptions { BackupPath = Path.GetTempPath() }));
        Assert.Contains("temporary", temp.Problem()!);                                                                          // never a temp folder: it disappears with the container
        var missing = new ConfigBackupService(Microsoft.Extensions.Options.Options.Create(new ConfigTransferOptions { BackupPath = Path.Combine(AppContext.BaseDirectory, "does-not-exist-" + Guid.NewGuid().ToString("N")) }));
        Assert.Contains("does not exist", missing.Problem()!);
        var relative = new ConfigBackupService(Microsoft.Extensions.Options.Options.Create(new ConfigTransferOptions { BackupPath = "relative/path" }));
        Assert.Contains("absolute", relative.Problem()!);
    }

    // ================================================================================================ the module never touches the PLC
    [Fact]
    public void The_module_has_no_dependency_on_the_plc_runtime_the_plc_adapter_or_the_rcs_client()
    {
        var forbidden = new[] { typeof(WCS.Application.Plc.IPlcRuntime), typeof(WCS.Application.Abstractions.IRcsClient), typeof(WCS.Application.Plc.PlcConfigService) };
        var types = typeof(ConfigTransferService).Assembly.GetTypes().Where(t => t.Namespace == "WCS.Application.ConfigTransfer" && !t.IsAbstract && !t.IsInterface);
        foreach (var t in types)
            foreach (var c in t.GetConstructors())
                foreach (var p in c.GetParameters())
                {
                    Assert.DoesNotContain(p.ParameterType, forbidden);
                    Assert.False(p.ParameterType.Name.Contains("PlcAdapter") || p.ParameterType.Name.Contains("PlcRuntime") || p.ParameterType.Name.Contains("Simulator"), $"{t.Name} depends on {p.ParameterType.Name}");
                }
    }
}
