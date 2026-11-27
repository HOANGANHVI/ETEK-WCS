using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>CR-051: one read-only view of what both engines created and sent to the RCS.</summary>
public class Cr051RcsWorkRulesTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 27, 8, 0, 0, TimeSpan.Zero);
    private static RcsWorkMessage Out(long seq, string type = "Create Leg Task", string result = "Sent") => new(seq, At.AddSeconds(seq), type, true, result, null);
    private static RcsWorkMessage In(long seq, string type, string result, string? error = null) => new(seq, At.AddSeconds(seq), type, false, result, error);
    private static RcsWorkMessage Amr(long seq, string name, string result = "Applied") => In(seq, "Simulated AGV Event: " + name, result);

    [Fact]
    public void Nothing_sent_is_not_sent_and_a_refusal_to_act_is_not_sent()
    {
        Assert.Equal((RcsWorkStates.NotSent, (DateTimeOffset?)null), State(RcsWorkRules.Derive("Ready", null, false, [])));
        Assert.Equal(RcsWorkStates.NotSent, RcsWorkRules.Derive("Ready", null, false, [Out(1), In(2, "Create Leg Task Response", "Blocked")]).State);
        Assert.Equal(RcsWorkStates.NotSent, RcsWorkRules.Derive("Ready", null, false, [Out(1, "Create Task", "NotSent")]).State);
        static (string, DateTimeOffset?) State(RcsWorkStatus s) => (s.State, s.SentAt);
    }

    [Theory]
    [InlineData("InProgress", null, false, "Unknown", RcsWorkStates.NoAnswer)]
    [InlineData("InProgress", null, false, null, RcsWorkStates.NoAnswer)]
    [InlineData("InProgress", null, false, "Timeout", RcsWorkStates.NoAnswer)]
    [InlineData("Failed", null, false, "Rejected", RcsWorkStates.Rejected)]
    [InlineData("Dispatching", null, false, "Rejected", RcsWorkStates.Rejected)]
    [InlineData("InProgress", "T1", false, "Accepted", RcsWorkStates.Accepted)]
    [InlineData("InProgress", "T1", true, "Accepted", RcsWorkStates.Executing)]
    [InlineData("Executing", "T1", false, "Accepted", RcsWorkStates.Executing)]
    [InlineData("Completed", "T1", true, "Accepted", RcsWorkStates.Completed)]
    [InlineData("Failed", "T1", true, "Accepted", RcsWorkStates.Failed)]
    [InlineData("Cancelled", "T1", false, "Accepted", RcsWorkStates.Cancelled)]
    public void The_state_follows_the_answer_and_the_facts_of_WCS(string workState, string? code, bool picked, string? answer, string expected)
    {
        List<RcsWorkMessage> messages = [Out(1)];
        if (answer is not null) messages.Add(In(2, "Create Leg Task Response", answer));
        var status = RcsWorkRules.Derive(workState, code, picked, messages);
        Assert.Equal(expected, status.State);
        Assert.Equal(At.AddSeconds(1), status.SentAt);
    }

    [Fact]
    public void An_interruption_shows_until_the_AMR_goes_on_and_a_refused_report_is_not_progress()
    {
        List<RcsWorkMessage> m = [Out(1), In(2, "Create Leg Task Response", "Accepted"), Amr(3, "Started"), Amr(4, "LegInterrupted", "Recorded")];
        Assert.Equal(RcsWorkStates.Interrupted, RcsWorkRules.Derive("InProgress", "T", false, m).State);
        m.Add(Amr(5, "PickConfirmed", "Rejected"));
        Assert.Equal(RcsWorkStates.Interrupted, RcsWorkRules.Derive("InProgress", "T", false, m).State);
        m.Add(Amr(6, "Recovered"));
        Assert.Equal(RcsWorkStates.Executing, RcsWorkRules.Derive("InProgress", "T", false, m).State);
    }
}

[Collection("Database")]
public class Cr051RcsWorkQueryTests(DatabaseFixture fixture)
{
    private static AmrExecutionSimulator Sim(RoutingKit kit) => kit.H.Services.GetRequiredService<AmrExecutionSimulator>();
    private static Task<IReadOnlyList<RcsWorkItem>> Work(RoutingKit kit, bool sentOnly = true, Guid? order = null) =>
        kit.With(sp => sp.GetRequiredService<IRcsWorkQuery>().ListAsync(sentOnly, order, 100));

    private async Task<RoutingKit> KitAsync()
    {
        var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-WORK", RequestType.PickupRequest, RackKind.Full, ["A1"],
            new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "B1"), Dest(1, "B2", order: 2)], "BeforePick"), Done()]));
        await kit.PublishAsync(v);
        return kit;
    }

    private static async Task<bool> RunAsync(RoutingKit kit, Func<Task<bool>> until, int steps = 150)
    {
        for (var i = 0; i < steps; i++)
        {
            if (await until()) return true;
            await kit.TickAsync();
            kit.H.Clock.Advance(TimeSpan.FromSeconds(1));
            await Sim(kit).StepAsync(TimeSpan.FromSeconds(0.5));
        }
        return await until();
    }

    private static async Task<Guid> StartAsync(RoutingKit kit, string key = "W1") => (await kit.StartAsync(key, "A1", RequestType.PickupRequest, RackKind.Full)).Order.Id;

    [Fact]
    public async Task A_leg_of_an_order_is_followed_from_created_to_completed_without_any_task_being_made_for_it()
    {
        await using var kit = await KitAsync();
        Sim(kit).Configure(true, 2);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var order = await StartAsync(kit);
        var seen = new List<string>();
        Assert.True(await RunAsync(kit, async () =>
        {
            var w = (await Work(kit, false, order)).SingleOrDefault();
            if (w is not null && seen.LastOrDefault() != w.RcsState) seen.Add(w.RcsState);
            return w?.RcsState == RcsWorkStates.Completed;
        }), string.Join(">", seen));
        Assert.Contains(RcsWorkStates.Executing, seen);
        var item = (await Work(kit)).Single();
        var leg = (await kit.OrderAsync(order)).Legs.Single();
        Assert.Equal(("OrderLeg", leg.Id, (Guid?)order, (int?)1, "T-WORK"), (item.Kind, item.Id, item.OrderId, item.LegSequence, item.ProcessCode));
        Assert.Equal((leg.AgvTaskCode, "A1", "B1"), (item.RcsTaskCode, item.Source, item.Destination));
        Assert.NotNull(item.RobotCode); Assert.NotNull(item.AgvCode); Assert.NotNull(item.SentAt);
        Assert.NotNull(item.PickConfirmedAt); Assert.NotNull(item.DropConfirmedAt);
        Assert.Equal(0, await kit.H.Db(d => d.Tasks.CountAsync()));
    }

    [Fact]
    public async Task Work_that_has_not_left_WCS_is_listed_as_not_sent_and_kept_out_of_what_was_sent()
    {
        await using var kit = await KitAsync();
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        await kit.H.Db(async d => { foreach (var a in d.Agvs) a.SetEnabled(false); await d.SaveChangesAsync(); return 0; });
        var order = await StartAsync(kit);
        await kit.TickAsync(); await kit.TickAsync();
        var waiting = (await Work(kit, false, order)).Single();
        Assert.Equal((RcsWorkStates.NotSent, (DateTimeOffset?)null, (string?)null), (waiting.RcsState, waiting.SentAt, waiting.RcsTaskCode));
        Assert.Empty(await Work(kit));
    }

    [Theory]
    [InlineData(AmrFault.RejectDispatch, RcsWorkStates.Rejected)]
    [InlineData(AmrFault.DispatchTimeout, RcsWorkStates.NoAnswer)]
    [InlineData(AmrFault.PickFailure, RcsWorkStates.Interrupted)]
    public async Task A_refusal_a_timeout_and_a_failure_are_told_apart(AmrFault fault, string expected)
    {
        await using var kit = await KitAsync();
        Sim(kit).Configure(true, 5);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        Sim(kit).Arm(fault, fault == AmrFault.DispatchTimeout ? 20 : 1, null);
        var order = await StartAsync(kit);
        Assert.True(await RunAsync(kit, async () => (await Work(kit)).SingleOrDefault()?.RcsState == expected, 60),
            string.Join(",", (await Work(kit, false, order)).Select(w => w.RcsState)));
        if (fault == AmrFault.DispatchTimeout) Sim(kit).ClearFaults();
        if (fault == AmrFault.PickFailure) await Sim(kit).RecoverAsync((await kit.OrderAsync(order)).Legs.Single().AgvRequestCode);
        if (fault != AmrFault.RejectDispatch)
            Assert.True(await RunAsync(kit, async () => (await Work(kit)).Single().RcsState == RcsWorkStates.Completed), (await Work(kit)).Single().RcsState);
        Assert.Single(await Work(kit, false, order));
    }

    [Fact]
    public async Task A_task_of_the_task_engine_and_a_leg_of_an_order_are_in_one_list_each_once()
    {
        await using var kit = await KitAsync();
        Sim(kit).Configure(true, 5);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var order = await StartAsync(kit);
        Assert.True(await RunAsync(kit, async () => (await kit.OrderAsync(order)).State == OrderState.Completed
            && (await kit.TransactionsAsync()).All(t => !t.IsActive)));
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var task = (await kit.H.CreateTaskAsync("Bp01", "A1")).Task.Id;
        var done = false;
        for (var i = 0; i < 200 && !done; i++)
        {
            await kit.H.TickAsync(); kit.H.Clock.Advance(TimeSpan.FromSeconds(1)); await Sim(kit).StepAsync(TimeSpan.FromSeconds(0.5));
            done = (await kit.H.GetTaskAsync(task)).State == TaskState.Completed;
        }
        Assert.True(done);
        var all = await Work(kit);
        Assert.Equal(["OrderLeg", "Task"], all.Select(w => w.Kind).OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(all, w => { Assert.Equal(RcsWorkStates.Completed, w.RcsState); Assert.NotNull(w.RcsTaskCode); Assert.NotNull(w.RobotCode); });
        Assert.Equal(task, all.Single(w => w.Kind == "Task").Id);
    }
}
