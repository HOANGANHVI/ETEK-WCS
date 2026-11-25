using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WCS.Application.Common;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>CR-035: Two-Gate (Publication vs Operational Readiness, D-11/D-12) and the Admin manual PLC simulation guards. The real RCS is not available in tests: nothing here claims an RCS result.</summary>
[Collection("Database")]
public class Cr035ReadinessAndSimulationTests(DatabaseFixture fixture)
{
    private static readonly string[] A = ["A1", "A2"];
    private static readonly IReadOnlyDictionary<string, string?> Enforce = new Dictionary<string, string?> { ["RouteEngine:EnforceReadiness"] = "true" };

    private static async Task<(RoutingKit kit, Guid versionId)> PublishedAsync(DatabaseFixture f, IReadOnlyDictionary<string, string?>? cfg)
    {
        var kit = await RoutingKit.CreateAsync(f, extraConfig: cfg);
        var (_, v) = await kit.CreateProcessAsync("T-RDY", RequestType.PickupRequest, RackKind.Full, A, OneLeg("Request", Dest(1, "B1")));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        return (kit, v);
    }

    [Fact]
    public async Task Publication_does_not_make_a_version_Ready_and_readiness_changes_never_change_the_publication_state()
    {
        var (kit, v) = await PublishedAsync(fixture, null);
        await using var _ = kit;
        var r0 = await kit.With(sp => sp.GetRequiredService<ProcessAdminService>().GetReadinessAsync(v));
        Assert.Equal("Published", r0.PublicationState);
        Assert.Equal("NotReady", r0.Readiness);

        var r1 = await kit.With(sp => sp.GetRequiredService<ProcessAdminService>().SetReadinessAsync(v, "Ready", "commissioned", "admin"));
        Assert.Equal("Ready", r1.Readiness); Assert.Equal("Published", r1.PublicationState);
        var r2 = await kit.With(sp => sp.GetRequiredService<ProcessAdminService>().SetReadinessAsync(v, "Suspended", "PLC fault", "admin"));
        Assert.Equal("Suspended", r2.Readiness); Assert.Equal("Published", r2.PublicationState);
    }

    [Fact]
    public async Task Undecided_readiness_transitions_are_refused_and_a_reason_is_required()
    {
        var (kit, v) = await PublishedAsync(fixture, null);
        await using var _ = kit;
        await kit.With(sp => sp.GetRequiredService<ProcessAdminService>().SetReadinessAsync(v, "Ready", "ok", "admin"));
        var ex = await Assert.ThrowsAsync<AppException>(() => kit.With(sp => sp.GetRequiredService<ProcessAdminService>().SetReadinessAsync(v, "NotReady", "x", "admin")));
        Assert.Equal("READINESS_TRANSITION_NOT_DECIDED", ex.Code);   // OQ-51
        var ex2 = await Assert.ThrowsAsync<AppException>(() => kit.With(sp => sp.GetRequiredService<ProcessAdminService>().SetReadinessAsync(v, "Suspended", " ", "admin")));
        Assert.Equal("REASON_REQUIRED", ex2.Code);
    }

    [Fact]
    public async Task With_enforcement_a_Published_but_NotReady_or_Suspended_process_accepts_no_request_and_a_Ready_one_does()
    {
        var (kit, v) = await PublishedAsync(fixture, Enforce);
        await using var _ = kit;
        var notReady = await Assert.ThrowsAsync<AppException>(() => kit.StartAsync("k1", "A1", RequestType.PickupRequest, RackKind.Full));
        Assert.Equal("PROCESS_NOT_READY", notReady.Code);

        await kit.With(sp => sp.GetRequiredService<ProcessAdminService>().SetReadinessAsync(v, "Ready", "ok", "admin"));
        var ok = await kit.StartAsync("k2", "A1", RequestType.PickupRequest, RackKind.Full);
        Assert.True(ok.Created);

        await kit.With(sp => sp.GetRequiredService<ProcessAdminService>().SetReadinessAsync(v, "Suspended", "pause", "admin"));
        var susp = await Assert.ThrowsAsync<AppException>(() => kit.StartAsync("k3", "A2", RequestType.PickupRequest, RackKind.Full));
        Assert.Equal("PROCESS_NOT_READY", susp.Code);
        // an explicit process code does not bypass Readiness either
        var byCode = await Assert.ThrowsAsync<AppException>(() => kit.StartAsync("k4", "A1", RequestType.PickupRequest, RackKind.Full, "T-RDY"));
        Assert.Equal("PROCESS_NOT_READY", byCode.Code);
    }

    [Fact]
    public async Task Without_the_feature_flag_behaviour_is_unchanged_even_for_a_NotReady_version()
    {
        var (kit, _) = await PublishedAsync(fixture, null);
        await using var _k = kit;
        Assert.True((await kit.StartAsync("k1", "A1", RequestType.PickupRequest, RackKind.Full)).Created);
    }

    [Fact]
    public async Task Simulation_cannot_be_activated_unless_allowed_and_never_with_a_simulated_RCS()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);   // PlcSimulation:Allowed = false, Rcs:RealDispatch:Enabled = false
        var svc = await kit.With(sp => Task.FromResult(sp.GetRequiredService<PlcSimulationService>()));
        var ex = Assert.Throws<AppException>(() => svc.Activate("admin"));
        Assert.Equal("SIMULATION_NOT_ALLOWED", ex.Code);
        Assert.Contains(svc.Status().Blockers, b => b.Contains("REAL RCS"));

        await using var kit2 = await RoutingKit.CreateAsync(fixture, extraConfig: new Dictionary<string, string?> { ["PlcSimulation:Allowed"] = "true" });
        var svc2 = await kit2.With(sp => Task.FromResult(sp.GetRequiredService<PlcSimulationService>()));
        var ex2 = Assert.Throws<AppException>(() => svc2.Activate("admin"));
        Assert.Equal("RCS_REAL_DISPATCH_REQUIRED", ex2.Code);   // no RCS simulator is ever accepted
        Assert.False(svc2.Status().Active);
    }

    [Fact]
    public async Task Simulation_requests_and_confirmations_are_refused_while_it_is_not_active()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var svc = await kit.With(sp => Task.FromResult(sp.GetRequiredService<PlcSimulationService>()));
        var ex = await Assert.ThrowsAsync<AppException>(() => svc.StartRequestAsync("ANY", "A1", true));
        Assert.Equal("SIMULATION_NOT_ACTIVE", ex.Code);
        var ex2 = await Assert.ThrowsAsync<AppException>(() => svc.ConfirmHandoverAsync(Guid.NewGuid(), false, "admin", null));
        Assert.Equal("SIMULATION_NOT_ACTIVE", ex2.Code);
    }

    private const string Before = "20261123042814_AddRcsDiagParams";   // the migration right before AddProcessVersionReadiness
    private static WcsDbContext Context(string cs) => new(new DbContextOptionsBuilder<WcsDbContext>().UseNpgsql(cs).Options);
    private static async Task<string?> ScalarAsync(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c); return (await cmd.ExecuteScalarAsync())?.ToString();
    }

    [Fact]
    public async Task Migration_marks_every_existing_version_NotReady_never_Ready_and_Down_removes_only_the_added_columns()
    {
        var scratch = new DatabaseFixture();   // own database: the shared one is never rolled back
        await scratch.InitializeAsync();
        try
        {
            await using (var kit = await RoutingKit.CreateAsync(scratch))
            {
                var (_, v) = await kit.CreateProcessAsync("T-MIG", RequestType.PickupRequest, RackKind.Full, A, OneLeg("Request", Dest(1, "B1")));
                await kit.PublishAsync(v);
            }
            var cs = scratch.ConnectionString;
            // roll back the readiness migration (data kept), then apply it again over the existing Published version
            await Context(cs).GetService<IMigrator>().MigrateAsync(Before);
            Assert.Equal("0", await ScalarAsync(cs, "SELECT COUNT(*) FROM information_schema.columns WHERE table_name='business_process_versions' AND column_name LIKE 'Readiness%'"));
            Assert.Equal("1", await ScalarAsync(cs, "SELECT COUNT(*) FROM business_process_versions WHERE \"State\" = 'Published'"));   // the version is still there
            await Context(cs).Database.MigrateAsync();
            Assert.Equal("Published", await ScalarAsync(cs, "SELECT \"State\" FROM business_process_versions"));
            Assert.Equal("NotReady", await ScalarAsync(cs, "SELECT \"Readiness\" FROM business_process_versions"));   // Published does not imply Ready
            Assert.Equal("0", await ScalarAsync(cs, "SELECT COUNT(*) FROM business_process_versions WHERE \"Readiness\" <> 'NotReady'"));
        }
        finally { await scratch.DisposeAsync(); }
    }
}
