using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WCS.Application.Routing;
using WCS.Domain.Agvs;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Seeding;
using WCS.Tests.Support;
using Xunit;

namespace WCS.Tests.Integration;

/// <summary>
/// Default location seed (initial data of a new deployment): the approved Phase 1 locations are created when missing and NOTHING ELSE changes. Real PostgreSQL, real seeder.
/// Capacities (Station 1, Buffer / ManualBuffer 2) are the owner's approved starting values, not measured business data.
/// </summary>
[Collection("Database")]
public class Cr037DefaultLocationSeedTests(DatabaseFixture fixture)
{
    private static readonly string[] Expected = ["A1", "A2", "B1", "B2", "F1", "F2", "F3", "F4", "F5"];

    private static WcsDataSeeder Seeder(WcsDbContext db, Harness h, bool defaults = true) =>
        new(db, h.Clock, Options.Create(new SeedOptions { DefaultLocations = defaults }), NullLogger<WcsDataSeeder>.Instance);

    /// <summary>Catalog version 1 only: these tests describe the Phase 1 locations and the once-per-version mechanism. Version 2 (CR-039) is tested in Cr039DefaultProcessCatalogTests.</summary>
    private static DefaultLocationCatalog Phase1()
    {
        var c = DefaultLocationCatalog.Load(); c.Version = 1; c.Locations.RemoveAll(x => x.SinceVersion != 1); return c;
    }
    private static Task<int> Seed(Harness h, bool defaults = true) => h.Db(db => Seeder(db, h, defaults).SeedDefaultLocationsAsync(Phase1()));
    private static Task<int> SeedWith(Harness h, DefaultLocationCatalog catalog) => h.Db(db => Seeder(db, h).SeedDefaultLocationsAsync(catalog));
    private static DefaultLocationRow Row(string code, int since) => new() { Code = code, Name = code, Type = "Buffer", Capacity = 2, AllowedRackKind = "Any", SinceVersion = since, Source = "test" };
    private static DefaultLocationCatalog Catalog(int version, params DefaultLocationRow[] extra)
    {
        var c = Phase1(); c.Version = version; c.Locations.AddRange(extra); return c;
    }
    private static Task<List<string?>> Markers(Harness h) => h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action == WcsDataSeeder.CatalogAppliedAction).OrderBy(a => a.TargetId).Select(a => a.TargetId).ToListAsync());

    [Fact]
    public async Task A_new_database_gets_exactly_the_approved_default_locations_with_the_approved_attributes()
    {
        await using var h = await Harness.CreateAsync(fixture);
        Assert.Equal(9, await Seed(h));
        var rows = await h.Db(db => db.Locations.AsNoTracking().OrderBy(x => x.Code).ToListAsync());
        Assert.Equal(Expected, rows.Select(x => x.Code).ToArray());
        foreach (var l in rows.Where(x => x.Code is "A1" or "A2" or "B1" or "B2"))
        {
            Assert.Equal(LocationType.Station, l.Type); Assert.Equal(1, l.RackCapacity); Assert.Equal(AllowedRackKind.Any, l.AllowedRackKind); Assert.True(l.RequiresPlcPermission);
        }
        foreach (var l in rows.Where(x => x.Code is "F1" or "F2")) { Assert.Equal(LocationType.Buffer, l.Type); Assert.Equal(2, l.RackCapacity); Assert.Equal(AllowedRackKind.Full, l.AllowedRackKind); }
        foreach (var l in rows.Where(x => x.Code is "F3" or "F4")) { Assert.Equal(LocationType.Buffer, l.Type); Assert.Equal(2, l.RackCapacity); Assert.Equal(AllowedRackKind.Empty, l.AllowedRackKind); }
        var f5 = rows.Single(x => x.Code == "F5");
        Assert.Equal(LocationType.ManualBuffer, f5.Type); Assert.Equal(2, f5.RackCapacity); Assert.Equal(AllowedRackKind.Any, f5.AllowedRackKind);
        Assert.All(rows, l => { Assert.True(l.Enabled); Assert.Null(l.ProductModelId); Assert.Null(l.StationRole); Assert.False(l.RequiresPlcPermission && l.Type != LocationType.Station); });
    }

    [Fact]
    public async Task Locations_that_were_not_approved_are_not_created()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await Seed(h);
        var codes = (await h.Db(db => db.Locations.AsNoTracking().Select(x => x.Code).ToListAsync())).ToHashSet();
        foreach (var c in new[] { "A3", "A4", "D1", "D2", "P1", "P2", "P3", "P4", "M5", "M6" }) Assert.DoesNotContain(c, codes);
    }

    [Fact]
    public async Task Every_location_of_the_Phase_1_reference_processes_exists_after_the_seed()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await Seed(h);
        var codes = (await h.Db(db => db.Locations.AsNoTracking().Select(x => x.Code).ToListAsync())).ToHashSet();
        var phase1 = ReferenceProcessCatalog.Load().Where(p => p.Code.StartsWith("BP-", StringComparison.Ordinal)).ToList();
        Assert.Equal(6, phase1.Count);
        foreach (var p in phase1)
        {
            var used = p.TriggerLocationCodes.Concat(p.Definition.Steps.SelectMany(s => s.Candidates).Select(c => c.LocationCode)).Distinct();
            foreach (var c in used) Assert.True(codes.Contains(c), $"{p.Code} needs location {c}");
        }
    }

    [Fact]
    public async Task BP01_finds_A1_and_A2_and_the_other_Phase_1_flows_find_their_stations()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.Db(db => { db.Agvs.Add(new Agv("AGV1", AgvRole.FullRack, "R-FULL")); db.Agvs.Add(new Agv("AGV2", AgvRole.EmptyRack, "R-EMPTY")); return db.SaveChangesAsync(); });
        await Seed(h);
        foreach (var (flow, station) in new[] { ("Bp01", "A1"), ("Bp01", "A2"), ("Bp02", "A1"), ("Bp03", "B1"), ("Bp05", "F5"), ("Bp06", "F5") })
        {
            var r = await h.CreateTaskAsync(flow, station);   // would throw STATION_NOT_FOUND if the station row were missing
            Assert.NotEqual(Guid.Empty, r.Task.Id);
        }
    }

    [Fact]
    public async Task Running_the_seed_again_creates_nothing_and_adds_no_duplicate()
    {
        await using var h = await Harness.CreateAsync(fixture);
        Assert.Equal(9, await Seed(h));
        Assert.Equal(0, await Seed(h));
        Assert.Equal(0, await Seed(h));
        Assert.Equal(9, await h.Db(db => db.Locations.CountAsync()));
        Assert.Equal(9, await h.Db(db => db.AuditLog.CountAsync(a => a.Action == "SEED_LOCATION_CREATE")));
    }

    [Fact]
    public async Task A_location_an_Admin_changed_is_never_overwritten_and_a_look_alike_code_is_not_duplicated()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.Db(async db =>
        {
            var a1 = new Location("A1", "Admin name", LocationType.Station, 5, AllowedRackKind.Full, false, h.Clock.UtcNow);
            a1.Configure("Admin name", 5, AllowedRackKind.Full, false, false, h.Clock.UtcNow);
            db.Locations.Add(a1);
            db.Locations.Add(new Location("f1", "lowercase by Admin", LocationType.Buffer, 7, AllowedRackKind.Any, false, h.Clock.UtcNow));
            await db.SaveChangesAsync(); return 0;
        });
        Assert.Equal(7, await Seed(h));   // A1 and F1 are left alone
        var a = await h.LocationAsync("A1");
        Assert.Equal("Admin name", a.Name); Assert.Equal(5, a.RackCapacity); Assert.Equal(AllowedRackKind.Full, a.AllowedRackKind); Assert.False(a.Enabled); Assert.False(a.RequiresPlcPermission);
        var codes = await h.Db(db => db.Locations.AsNoTracking().Select(x => x.Code).ToListAsync());
        Assert.Contains("f1", codes); Assert.DoesNotContain("F1", codes);
        Assert.Equal(1, codes.Count(c => c.Equals("A1", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Existing_locations_racks_and_reservations_are_not_changed()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var idA = Guid.Empty;
        await h.Db(async db =>
        {
            var t2 = new Location("123123", "T2", LocationType.ManualBuffer, 4, AllowedRackKind.Any, false, h.Clock.UtcNow);
            var t1 = new Location("A123", "T1", LocationType.Buffer, 3, AllowedRackKind.Any, false, h.Clock.UtcNow);
            db.Locations.AddRange(t2, t1); idA = t1.Id;
            db.Racks.Add(new Rack(RackStatus.Full, t1.Id, "Full", "EXT-1", h.Clock.UtcNow));
            await db.SaveChangesAsync(); return 0;
        });
        string Snapshot(WcsDbContext db) => string.Join("|", db.Locations.AsNoTracking().Where(x => x.Code == "123123" || x.Code == "A123").OrderBy(x => x.Code)
            .Select(x => $"{x.Code}/{x.Name}/{x.Type}/{x.RackCapacity}/{x.Enabled}/{x.Version}").ToList()) + "#" + string.Join("|", db.Racks.AsNoTracking().Select(x => $"{x.ExternalRackId}/{x.Status}/{x.CurrentLocationId}/{x.LastConfirmedAt:O}").ToList())
            + "#" + db.Reservations.Count() + "#" + db.Tasks.Count();
        var before = await h.Db(db => Task.FromResult(Snapshot(db)));
        await Seed(h);
        Assert.Equal(before, await h.Db(db => Task.FromResult(Snapshot(db))));
        Assert.Equal(11, await h.Db(db => db.Locations.CountAsync()));   // 2 existing + 9 defaults
        Assert.Equal(idA, (await h.LocationAsync("A123")).Id);
    }

    [Fact]
    public async Task The_seed_can_be_switched_off_and_every_created_row_is_audited_as_SEED()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.Db(async db => { await Seeder(db, h, defaults: false).SeedAsync(); return 0; });
        Assert.Equal(0, await h.Db(db => db.Locations.CountAsync()));
        Assert.Equal(9, await Seed(h));
        var audit = await h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action == "SEED_LOCATION_CREATE").ToListAsync());
        Assert.Equal(9, audit.Count);
        Assert.All(audit, a => { Assert.Equal("SEED", a.Actor); Assert.Equal("Location", a.TargetType); Assert.Contains(a.TargetId!, Expected); });
    }

    // ================================================================== once per catalog version

    [Fact]
    public async Task First_run_applies_version_1_and_records_the_marker_in_the_same_transaction()
    {
        await using var h = await Harness.CreateAsync(fixture);
        Assert.Equal(9, await Seed(h));
        Assert.Equal(["1"], await Markers(h));
    }

    [Fact]
    public async Task A_restart_does_not_seed_again()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await Seed(h);
        Assert.Equal(0, await Seed(h));
        Assert.Equal(0, await Seed(h));
        Assert.Equal(["1"], await Markers(h));
        Assert.Equal(9, await h.Db(db => db.AuditLog.CountAsync(a => a.Action == "SEED_LOCATION_CREATE")));
    }

    [Fact]
    public async Task A_default_location_the_Admin_deleted_is_not_created_again_after_a_restart()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await Seed(h);
        await h.Db(async db => { db.Locations.Remove(await db.Locations.SingleAsync(x => x.Code == "F5")); await db.SaveChangesAsync(); return 0; });
        Assert.Equal(0, await Seed(h));
        Assert.DoesNotContain("F5", await h.Db(db => db.Locations.AsNoTracking().Select(x => x.Code).ToListAsync()));
        Assert.Equal(8, await h.Db(db => db.Locations.CountAsync()));
    }

    [Fact]
    public async Task A_higher_catalog_version_adds_only_the_new_locations_and_never_the_deleted_or_edited_ones()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await Seed(h);
        await h.Db(async db =>
        {
            db.Locations.Remove(await db.Locations.SingleAsync(x => x.Code == "F5"));
            (await db.Locations.SingleAsync(x => x.Code == "A1")).Configure("Admin A1", 4, AllowedRackKind.Any, true, true, h.Clock.UtcNow);
            await db.SaveChangesAsync(); return 0;
        });
        Assert.Equal(1, await SeedWith(h, Catalog(2, Row("Z1", 2))));
        var codes = await h.Db(db => db.Locations.AsNoTracking().Select(x => x.Code).ToListAsync());
        Assert.Contains("Z1", codes); Assert.DoesNotContain("F5", codes);
        Assert.Equal(9, codes.Count);   // 8 remaining defaults + Z1
        var a1 = await h.LocationAsync("A1");
        Assert.Equal("Admin A1", a1.Name); Assert.Equal(4, a1.RackCapacity);
        Assert.Equal(["1", "2"], await Markers(h));
        Assert.Equal(0, await SeedWith(h, Catalog(2, Row("Z1", 2))));   // version 2 is not applied twice either
    }

    [Fact]
    public async Task A_database_seeded_by_the_earlier_version_of_the_seed_only_gets_the_marker()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 2);   // the nine locations exist, no marker yet (what wcs_db looks like)
        Assert.Equal(0, await Seed(h));
        Assert.Equal(["1"], await Markers(h));
        Assert.Equal(9, await h.Db(db => db.Locations.CountAsync()));
    }

    [Fact]
    public async Task A_failure_in_the_middle_leaves_neither_locations_nor_a_marker_and_the_next_run_completes()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var bad = Catalog(1, Row(new string('X', 60), 1));   // longer than the column: the failure happens while saving, after the earlier rows were added
        await Assert.ThrowsAnyAsync<Exception>(() => SeedWith(h, bad));
        Assert.Equal(0, await h.Db(db => db.Locations.CountAsync()));
        Assert.Empty(await Markers(h));
        Assert.Equal(0, await h.Db(db => db.AuditLog.CountAsync(a => a.Action == "SEED_LOCATION_CREATE")));
        Assert.Equal(9, await Seed(h));   // the real catalog applies cleanly afterwards
        Assert.Equal(["1"], await Markers(h));
    }

    [Fact]
    public async Task A_failure_in_a_later_version_keeps_the_earlier_marker_and_does_not_mark_the_failed_version()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await Seed(h);
        var bad = Catalog(2, Row("Z1", 2), Row(new string('Y', 60), 2));
        await Assert.ThrowsAnyAsync<Exception>(() => SeedWith(h, bad));
        Assert.Equal(["1"], await Markers(h));
        Assert.DoesNotContain("Z1", await h.Db(db => db.Locations.AsNoTracking().Select(x => x.Code).ToListAsync()));
        Assert.Equal(1, await SeedWith(h, Catalog(2, Row("Z1", 2))));   // retried with a good catalog
        Assert.Equal(["1", "2"], await Markers(h));
    }

    [Fact]
    public async Task Several_instances_starting_together_create_every_location_once_and_one_marker()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => Seed(h))));
        Assert.Equal(9, results.Sum());
        Assert.Equal(9, await h.Db(db => db.Locations.CountAsync()));
        Assert.Equal(["1"], await Markers(h));
        Assert.Equal(9, await h.Db(db => db.AuditLog.CountAsync(a => a.Action == "SEED_LOCATION_CREATE")));
    }
}
