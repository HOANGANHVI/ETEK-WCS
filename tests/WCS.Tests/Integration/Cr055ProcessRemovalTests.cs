using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Common;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>CR-055: what may be removed from the process configuration, and what is kept because transport orders or approval depend on it.</summary>
[Collection("Database")]
public class Cr055ProcessRemovalTests(DatabaseFixture fixture)
{
    private static Task<T> Admin<T>(RoutingKit kit, Func<ProcessAdminService, Task<T>> call) => kit.With(sp => call(sp.GetRequiredService<ProcessAdminService>()));
    private static VersionDefinition Simple => OneLeg("Request", Dest(1, "B1"));

    private static async Task<(Guid Process, Guid Version)> MakeAsync(RoutingKit kit, string code, bool publish = false)
    {
        var (p, v) = await kit.CreateProcessAsync(code, RequestType.PickupRequest, RackKind.Full, ["A1"], Simple);
        if (publish) await kit.PublishAsync(v);
        return (p, v);
    }

    private static async Task SwitchOffAsync(RoutingKit kit, Guid process)
    {
        var p = await Admin(kit, s => s.GetAsync(process));
        await Admin(kit, s => s.UpdateAsync(process, new ProcessHeader(p.Code, p.Name, p.Description, p.RequestType, p.RackKind, [.. p.TriggerLocationCodes], p.ProductModelId), false));
    }

    private static async Task<string> RefusedAsync<T>(RoutingKit kit, Func<ProcessAdminService, Task<T>> call) => (await Assert.ThrowsAsync<AppException>(() => Admin(kit, call))).Code;

    private static Task<(int Processes, int Versions, int Steps, int Candidates)> Count(RoutingKit kit, Guid process) => kit.H.Db(async d =>
        (await d.BusinessProcesses.CountAsync(p => p.Id == process), await d.BusinessProcessVersions.CountAsync(v => v.ProcessId == process),
         await d.RouteSteps.CountAsync(s => d.BusinessProcessVersions.Any(v => v.Id == s.VersionId && v.ProcessId == process)),
         await d.RouteCandidates.CountAsync(c => d.RouteSteps.Any(s => s.Id == c.StepId && d.BusinessProcessVersions.Any(v => v.Id == s.VersionId && v.ProcessId == process)))));

    [Fact]
    public async Task A_process_that_never_had_an_order_is_deleted_with_its_versions_and_routes()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (process, _) = await MakeAsync(kit, "T-DRAFT");
        var allSteps = await kit.H.Db(d => d.RouteSteps.CountAsync());
        Assert.Equal(ProcessRemovalActions.Delete, (await Admin(kit, s => s.RemovalAsync(process))).Action);
        Assert.Equal("REASON_REQUIRED", await RefusedAsync(kit, s => s.RemoveAsync(process, " ")));
        var result = await Admin(kit, s => s.RemoveAsync(process, "no longer used"));
        Assert.Equal(("T-DRAFT", "Deleted", 1), (result.Code, result.Outcome, result.Versions));
        Assert.Equal((0, 0, 0, 0), await Count(kit, process));
        Assert.Equal(allSteps - 2, await kit.H.Db(d => d.RouteSteps.CountAsync()));          // only its own steps are gone
        await MakeAsync(kit, "T-DRAFT");                                                      // the code is free again
    }

    [Fact]
    public async Task A_published_process_without_orders_must_be_switched_off_first()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (process, _) = await MakeAsync(kit, "T-PUB", publish: true);
        var plan = await Admin(kit, s => s.RemovalAsync(process));
        Assert.Equal((ProcessRemovalActions.None, "PROCESS_DISABLE_FIRST"), (plan.Action, plan.BlockCode));
        Assert.Equal("PROCESS_DISABLE_FIRST", await RefusedAsync(kit, s => s.RemoveAsync(process, "x")));
        await SwitchOffAsync(kit, process);
        Assert.Equal("Deleted", (await Admin(kit, s => s.RemoveAsync(process, "retired"))).Outcome);
        Assert.Equal((0, 0, 0, 0), await Count(kit, process));
    }

    [Fact]
    public async Task A_process_with_orders_is_never_deleted_open_work_blocks_and_finished_work_only_switches_it_off()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (process, _) = await MakeAsync(kit, "T-USED", publish: true);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var order = (await kit.StartAsync("R-1", "A1", RequestType.PickupRequest, RackKind.Full)).Order.Id;
        var plan = await Admin(kit, s => s.RemovalAsync(process));
        Assert.Equal((ProcessRemovalActions.None, "PROCESS_HAS_OPEN_ORDERS", 1, 1), (plan.Action, plan.BlockCode, plan.Orders, plan.OpenOrders));
        Assert.Equal("PROCESS_HAS_OPEN_ORDERS", await RefusedAsync(kit, s => s.RemoveAsync(process, "x")));

        await kit.TickAsync();
        var leg = (await kit.OrderAsync(order)).Legs.Single();
        await kit.PickAsync(leg.Id); await kit.DropAsync(leg.Id); await kit.TickAsync();
        Assert.Equal(OrderState.Completed, (await kit.OrderAsync(order)).State);
        var before = await Count(kit, process);
        Assert.Equal(ProcessRemovalActions.Disable, (await Admin(kit, s => s.RemovalAsync(process))).Action);
        var result = await Admin(kit, s => s.RemoveAsync(process, "replaced"));
        Assert.Equal(("Disabled", 1), (result.Outcome, result.Orders));
        Assert.False((await Admin(kit, s => s.GetAsync(process))).Enabled);
        Assert.Equal(before, await Count(kit, process));                                      // nothing of the history is gone
        Assert.Equal(OrderState.Completed, (await kit.OrderAsync(order)).State);
        Assert.Equal("PROCESS_ALREADY_DISABLED", await RefusedAsync(kit, s => s.RemoveAsync(process, "again")));
    }

    [Theory]
    [InlineData("BP-01")]
    [InlineData("BP-04")]
    public async Task The_approved_reference_processes_are_not_removed(string code)
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var existing = await kit.H.Db(d => d.BusinessProcesses.Where(p => p.Code == code).Select(p => (Guid?)p.Id).SingleOrDefaultAsync());
        var process = existing ?? (await MakeAsync(kit, code)).Process;
        var plan = await Admin(kit, s => s.RemovalAsync(process));
        Assert.Equal((ProcessRemovalActions.None, "PROCESS_PROTECTED"), (plan.Action, plan.BlockCode));
        await SwitchOffAsync(kit, process);
        Assert.Equal("PROCESS_PROTECTED", await RefusedAsync(kit, s => s.RemoveAsync(process, "x")));
        Assert.Equal(1, (await Count(kit, process)).Processes);
    }

    [Fact]
    public async Task The_phase_two_reference_processes_are_not_protected()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var all = ReferenceProcessCatalog.Load();
        Assert.Equal(["BP-01", "BP-02", "BP-03", "BP-04", "BP-05", "BP-06"], all.Where(r => r.Protected).Select(r => r.Code).Order(StringComparer.Ordinal));
        Assert.All(all.Where(r => r.Code.StartsWith("PH2", StringComparison.Ordinal)), r => Assert.False(r.Protected));
    }

    [Fact]
    public async Task Only_an_unpublished_version_that_is_not_the_only_one_is_deleted()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (process, first) = await MakeAsync(kit, "T-VER");
        Assert.Equal("VERSION_ONLY_ONE", await RefusedAsync(kit, s => s.DeleteVersionAsync(first, "x")));
        await kit.PublishAsync(first);
        var draft = await Admin(kit, s => s.NewVersionAsync(process, first, "test"));
        Assert.Equal("VERSION_NOT_DELETABLE", await RefusedAsync(kit, s => s.DeleteVersionAsync(first, "x")));
        Assert.Equal("REASON_REQUIRED", await RefusedAsync(kit, s => s.DeleteVersionAsync(draft.Id, null)));
        var removed = await Admin(kit, s => s.DeleteVersionAsync(draft.Id, "not needed"));
        Assert.Equal(("T-VER", 2), (removed.ProcessCode, removed.Number));
        Assert.Equal((1, 1, 2, 1), await Count(kit, process));                                // the published version and its route are untouched
        await Assert.ThrowsAsync<AppException>(() => Admin(kit, s => s.GetVersionAsync(draft.Id)));
    }
}
