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
    private readonly Dictionary<Guid, string> codes = [];
    private static Task<T> Admin<T>(RoutingKit kit, Func<ProcessAdminService, Task<T>> call) => kit.With(sp => call(sp.GetRequiredService<ProcessAdminService>()));
    private static VersionDefinition Simple => OneLeg("Request", Dest(1, "B1"));

    private async Task<(Guid Process, Guid Version)> MakeAsync(RoutingKit kit, string code, bool publish = false)
    {
        var (p, v) = await kit.CreateProcessAsync(code, RequestType.PickupRequest, RackKind.Full, ["A1"], Simple);
        if (publish) await kit.PublishAsync(v);
        codes[p] = code;
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
        Assert.Equal("REASON_REQUIRED", await RefusedAsync(kit, s => s.RemoveAsync(process, " ", codes[process])));
        var result = await Admin(kit, s => s.RemoveAsync(process, "no longer used", codes[process]));
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
        Assert.Equal("PROCESS_DISABLE_FIRST", await RefusedAsync(kit, s => s.RemoveAsync(process, "x", codes[process])));
        await SwitchOffAsync(kit, process);
        Assert.Equal("Deleted", (await Admin(kit, s => s.RemoveAsync(process, "retired", codes[process]))).Outcome);
        Assert.Equal((0, 0, 0, 0), await Count(kit, process));
    }

    [Fact]
    public async Task A_process_with_finished_orders_is_deleted_and_the_orders_with_their_whole_history_stay()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (process, _) = await MakeAsync(kit, "T-USED", publish: true);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var order = (await kit.StartAsync("R-1", "A1", RequestType.PickupRequest, RackKind.Full)).Order.Id;
        var plan = await Admin(kit, s => s.RemovalAsync(process));
        Assert.Equal((ProcessRemovalActions.None, "PROCESS_HAS_OPEN_ORDERS", 1, 1), (plan.Action, plan.BlockCode, plan.Orders, plan.OpenOrders));
        Assert.Equal("PROCESS_HAS_OPEN_ORDERS", await RefusedAsync(kit, s => s.RemoveAsync(process, "x", "T-USED")));

        await kit.TickAsync();
        var leg = (await kit.OrderAsync(order)).Legs.Single();
        await kit.PickAsync(leg.Id); await kit.DropAsync(leg.Id); await kit.TickAsync();
        Assert.Equal(OrderState.Completed, (await kit.OrderAsync(order)).State);
        // the order is finished but the AMR has not left: its ports are still held
        plan = await Admin(kit, s => s.RemovalAsync(process));
        Assert.Equal("PROCESS_HOLDS_PORT", plan.BlockCode);
        Assert.Contains("A1", plan.HeldPorts);
        foreach (var x in (await kit.TransactionsAsync()).Where(x => x.IsActive).ToList())
            await kit.With(sp => sp.GetRequiredService<StationTransactionService>().ConfirmAgvExitedAsync(x.Id));
        Assert.Equal("PROCESS_DISABLE_FIRST", (await Admin(kit, s => s.RemovalAsync(process))).BlockCode);
        await SwitchOffAsync(kit, process);

        Task<(int, int, int, int, int, int)> History() => kit.H.Db(async d => (await d.TransportOrders.CountAsync(), await d.TransportLegs.CountAsync(), await d.RoutingEvents.CountAsync(),
            await d.StationTransactions.CountAsync(), await d.InventoryTransactions.CountAsync(), await d.Racks.CountAsync()));
        var before = await History();
        Assert.Equal((ProcessRemovalActions.Delete, 1), ((await Admin(kit, s => s.RemovalAsync(process))).Action, (await Admin(kit, s => s.RemovalAsync(process))).Orders));
        Assert.Equal("PROCESS_CODE_CONFIRMATION", await RefusedAsync(kit, s => s.RemoveAsync(process, "x", "T-OTHER")));
        var result = await Admin(kit, s => s.RemoveAsync(process, "replaced", "t-used"));
        Assert.Equal(("Deleted", 1, 1), (result.Outcome, result.Orders, result.Versions));
        Assert.Equal((0, 0, 0, 0), await Count(kit, process));
        Assert.Equal(before, await History());                                                // nothing of the history is gone
        var kept = await kit.OrderAsync(order);                                               // and the order still reads on its own
        Assert.Equal(("T-USED", OrderState.Completed, 1), (kept.ProcessCode, kept.State, kept.Legs.Count));
        await kit.TickAsync();                                                                // the engine is not disturbed by an order whose process is gone
        var (again, _) = await MakeAsync(kit, "T-USED", publish: true);                       // the code is free; the old order stays with the old process
        Assert.Equal(0, (await Admin(kit, s => s.RemovalAsync(again))).Orders);
    }

    [Fact]
    public async Task BP_04_under_review_can_be_removed_and_stays_blocked_when_imported_again()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);                          // CR-060
        Assert.Empty(ReferenceProcessCatalog.Load().Where(r => r.Protected));
        var existing = await kit.H.Db(d => d.BusinessProcesses.Where(p => p.Code == "BP-04").Select(p => (Guid?)p.Id).SingleOrDefaultAsync());
        var process = existing ?? (await MakeAsync(kit, "BP-04")).Process;
        Assert.Equal(ProcessRemovalActions.Delete, (await Admin(kit, s => s.RemovalAsync(process))).Action);
        await Admin(kit, s => s.RemoveAsync(process, "not used", "BP-04"));
        Assert.Equal(0, (await Count(kit, process)).Processes);
        Assert.True(ReferenceProcessCatalog.Load().Single(r => r.Code == "BP-04").ReviewBlocked);
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
