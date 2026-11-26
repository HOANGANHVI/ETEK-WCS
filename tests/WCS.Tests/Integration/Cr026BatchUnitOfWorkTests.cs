using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Application.Auth;
using WCS.Application.Common;
using WCS.Application.Config;
using WCS.Application.Plc;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Domain.Users;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-026 step 1: <see cref="IUnitOfWork.ExecuteBatchAsync{T}"/> – one transaction in which the existing configuration services (each opens its own
/// ExecuteAsync) participate; rollback of everything on failure; unchanged refusal of nested units of work outside a batch.
/// </summary>
[Collection("Database")]
public class Cr026BatchUnitOfWorkTests(DatabaseFixture fixture)
{
    private sealed class RecordingNotifier : IWcsNotifier
    {
        public List<string> Names { get; } = [];
        public Task PublishAsync(IReadOnlyCollection<WcsEvent> events, CancellationToken ct = default) { lock (Names) Names.AddRange(events.Select(e => e.Name)); return Task.CompletedTask; }
    }

    private static SaveLocationCommand Loc(string code, int cap = 2) => new(code, code + " name", LocationType.Buffer, cap, AllowedRackKind.Any, true, false);

    [Fact]
    public async Task Services_join_one_transaction_and_commit_together_including_audit()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        await kit.With(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var ops = sp.GetRequiredService<OperationalConfigService>();
            var audit = sp.GetRequiredService<IAuditLog>();
            await uow.ExecuteBatchAsync(async ct =>
            {
                await ops.SaveLocationAsync(Loc("N1"), true, ct);
                await ops.SaveLocationAsync(Loc("N1", 5), false, ct);   // sees the row created earlier in the SAME batch (flushed to the open transaction)
                await ops.SaveLocationAsync(Loc("N2"), true, ct);
                audit.Enlist(new AuditRecord("CONFIG_TEST", "admin", "Admin", "Location", "N1", AuditResult.Success, "batch", null, null, null));
                return 0;
            });
            return 0;
        });
        Assert.Equal(5, (await kit.H.LocationAsync("N1")).RackCapacity);
        Assert.Equal(2, (await kit.H.LocationAsync("N2")).RackCapacity);
        Assert.Equal(1, await kit.H.Db(db => db.AuditLog.CountAsync(a => a.Action == "CONFIG_TEST")));
    }

    [Fact]
    public async Task A_failure_in_any_step_rolls_everything_back_including_audit_and_process_data()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var before = await kit.H.Db(db => db.Locations.CountAsync());
        await Assert.ThrowsAsync<ValidationFailedException>(() => kit.With(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var ops = sp.GetRequiredService<OperationalConfigService>();
            var admin = sp.GetRequiredService<ProcessAdminService>();
            var audit = sp.GetRequiredService<IAuditLog>();
            return await uow.ExecuteBatchAsync(async ct =>
            {
                await ops.SaveLocationAsync(Loc("R1"), true, ct);
                var p = await admin.CreateAsync(new ProcessHeader("T-RB", "T-RB", null, "PickupRequest", "Full", ["A1"]), "admin", ct);
                await admin.SaveDefinitionAsync(p.Versions[0].Id, OneLeg("Request", Dest(1, "B1")), ct);
                audit.Enlist(new AuditRecord("CONFIG_TEST_RB", "admin", "Admin", "Location", "R1", AuditResult.Success, "batch", null, null, null));
                await ops.SaveLocationAsync(Loc("R1"), true, ct);   // LOCATION_EXISTS -> the 4th step fails
                return 0;
            });
        }));
        Assert.Equal(before, await kit.H.Db(db => db.Locations.CountAsync()));
        Assert.Equal(0, await kit.H.Db(db => db.BusinessProcesses.CountAsync(p => p.Code == "T-RB")));
        Assert.Equal(0, await kit.H.Db(db => db.AuditLog.CountAsync(a => a.Action == "CONFIG_TEST_RB")));
    }

    [Fact]
    public async Task Process_header_new_draft_version_and_port_link_services_participate()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (processId, v1) = await kit.CreateProcessAsync("T-PART", RequestType.PickupRequest, RackKind.Full, ["A1", "A2"], OneLeg("Request", Dest(1, "B1")));
        await kit.PublishAsync(v1);
        Guid newVersion = Guid.Empty;
        await kit.With(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var admin = sp.GetRequiredService<ProcessAdminService>();
            var plc = sp.GetRequiredService<PlcProvisioningService>();
            await uow.ExecuteBatchAsync(async ct =>
            {
                await admin.UpdateAsync(processId, new ProcessHeader("T-PART", "renamed", "d", "PickupRequest", "Full", ["A1", "A2"]), true, ct);   // CR-042: the triggers of a published process are locked
                var nv = await admin.NewVersionAsync(processId, null, "admin", ct);
                await admin.SaveDefinitionAsync(nv.Id, OneLeg("Request", Dest(1, "C1")), ct);   // the new version is visible to the next service call of the same batch
                newVersion = nv.Id;
                await plc.SetPortLinkAsync("B1", "B1", false, ct);
                return 0;
            });
            return 0;
        });
        var p = await kit.H.Db(db => db.BusinessProcesses.AsNoTracking().SingleAsync(x => x.Id == processId));
        Assert.Equal("renamed", p.Name);
        var versions = await kit.H.Db(db => db.BusinessProcessVersions.AsNoTracking().Where(v => v.ProcessId == processId).OrderBy(v => v.Number).ToListAsync());
        Assert.Equal([ProcessConfigState.Published, ProcessConfigState.Draft], versions.Select(v => v.State).ToArray());   // the Published version is untouched
        Assert.False((await kit.H.Db(db => db.PlcStations.AsNoTracking().SingleAsync(s => s.Id == "B1"))).Enabled);
        Assert.Equal(newVersion, versions[1].Id);
    }

    [Fact]
    public async Task Outside_a_batch_nested_units_of_work_are_still_refused_and_a_batch_cannot_nest()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        await kit.With(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteAsync(async ct => await uow.ExecuteAsync(_ => Task.FromResult(1), ct), CancellationToken.None));
            return 0;
        });
        await kit.With(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteBatchAsync(async ct => await uow.ExecuteBatchAsync(_ => Task.FromResult(1), ct), CancellationToken.None));
            return 0;
        });
    }

    [Fact]
    public async Task Realtime_events_raised_inside_a_batch_wait_for_the_commit_and_are_dropped_on_rollback()
    {
        var rec = new RecordingNotifier();
        await using var kit = await RoutingKit.CreateAsync(fixture, more: s => s.AddSingleton<IWcsNotifier>(new BatchAwareNotifier(rec)));
        // rolled back: nothing published
        await Assert.ThrowsAsync<ValidationFailedException>(() => kit.With(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>(); var ops = sp.GetRequiredService<OperationalConfigService>();
            return await uow.ExecuteBatchAsync(async ct =>
            {
                await ops.SaveLocationAsync(Loc("E1"), true, ct);
                Assert.Empty(rec.Names);                              // held inside the batch
                await ops.SaveLocationAsync(Loc("E1"), true, ct);     // fails
                return 0;
            });
        }));
        Assert.Empty(rec.Names);
        // committed: published once, after the commit
        await kit.With(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>(); var ops = sp.GetRequiredService<OperationalConfigService>();
            await uow.ExecuteBatchAsync(async ct => { await ops.SaveLocationAsync(Loc("E2"), true, ct); Assert.Empty(rec.Names); return 0; });
            return 0;
        });
        Assert.Contains("InventoryChanged", rec.Names);
        // outside a batch the notifier still publishes immediately
        var n = rec.Names.Count;
        await kit.With(async sp => { await sp.GetRequiredService<OperationalConfigService>().SaveLocationAsync(Loc("E3"), true); return 0; });
        Assert.True(rec.Names.Count > n);
    }

    [Fact]
    public async Task Concurrent_modification_re_runs_the_whole_batch()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        await kit.With(async sp => { await sp.GetRequiredService<OperationalConfigService>().SaveLocationAsync(Loc("K1", 2), true); return 0; });
        var attempts = 0;
        await kit.With(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>(); var db = sp.GetRequiredService<WCS.Infrastructure.Persistence.WcsDbContext>();
            await uow.ExecuteBatchAsync(async ct =>
            {
                attempts++;
                var l = await db.Locations.SingleAsync(x => x.Code == "K1", ct);   // read, tracked
                if (attempts == 1)   // another connection commits a change to the same row after our read -> optimistic concurrency failure at the batch flush -> whole batch re-runs
                    await kit.H.Db(async d => { (await d.Locations.SingleAsync(x => x.Code == "K1")).Configure("other", 3, AllowedRackKind.Any, true, false, kit.H.Clock.UtcNow); await d.SaveChangesAsync(); return 0; });
                l.Configure("mine", 4, AllowedRackKind.Any, true, false, kit.H.Clock.UtcNow);
                return 0;
            });
            return 0;
        });
        Assert.Equal(2, attempts);
        var final = await kit.H.LocationAsync("K1");
        Assert.Equal("mine", final.Name); Assert.Equal(4, final.RackCapacity);
    }
}
