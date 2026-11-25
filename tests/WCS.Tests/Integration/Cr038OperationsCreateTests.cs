using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Common;
using WCS.Domain.Rcs;
using WCS.Domain.Tasks;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;
using Xunit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-038 fix: creating a transport request in a Production environment. A fresh database is "Simulator, not chosen" and refuses tasks; the Administrator must be able to CHOOSE
/// Simulator (the mode it is already in), after which the one task pipeline used by both pages (POST /api/tasks) accepts requests and the task runs its whole simulated life.
/// </summary>
[Collection("Database")]
public class Cr038OperationsCreateTests(DatabaseFixture fixture)
{
    private sealed class ProdEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "WCS.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private async Task<Harness> ProductionAsync()
    {
        var h = await Harness.CreateAsync(fixture, s => s.AddSingleton<IHostEnvironment>(new ProdEnv()));
        await h.SeedStandardAsync(bufferCapacity: 5);
        await h.StockAsync("F1", RackKind.Full, 3);
        await Svc(h, s => s.InitializeAsync().ContinueWith(_ => 0));   // what the host does at start-up
        return h;
    }

    private static Task<T> Svc<T>(Harness h, Func<IExecutionModeService, Task<T>> f) => h.Scoped(sp => f(sp.GetRequiredService<IExecutionModeService>()));
    private static ModeTransitionRequest To(ExecutionMode m, string? id = null) => new(id ?? Guid.NewGuid().ToString("N"), m, "commissioning in Simulator", false);
    private static RcsDispatchStatus Guard(Harness h) => h.Services.GetRequiredService<IRcsDispatchGuard>().Check();

    [Fact]
    public async Task A_fresh_Production_database_refuses_tasks_until_a_mode_is_chosen()
    {
        await using var h = await ProductionAsync();
        Assert.False(Guard(h).Allowed); Assert.Equal("RCS_NOT_CONNECTED", Guard(h).Code);
        var ex = await Assert.ThrowsAsync<AppException>(() => h.CreateTaskAsync("BP05", "F5", "p0"));
        Assert.Equal("RCS_NOT_CONNECTED", ex.Code);
    }

    [Fact]
    public async Task The_Admin_can_choose_Simulator_while_already_in_it_and_the_request_then_runs_to_completion()
    {
        await using var h = await ProductionAsync();
        var readiness = await Svc(h, s => s.ReadinessAsync(ExecutionMode.Simulator));
        Assert.True(readiness.CanSwitch, string.Join(" | ", readiness.Conditions.Where(c => !c.Ok).Select(c => c.Code)));

        var chosen = await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Simulator), "admin"));
        Assert.True(chosen.Succeeded, string.Join(" | ", chosen.Failed.Select(c => c.Code)));
        var view = await Svc(h, s => s.GetAsync());
        Assert.Equal(ExecutionMode.Simulator, view.Mode); Assert.True(view.IsExplicit);
        Assert.True(Guard(h).Allowed);
        Assert.Single(await Svc(h, s => s.HistoryAsync(10)));                                       // audited and kept in the history

        var t = (await h.CreateTaskAsync("BP05", "F5", "p1")).Task;
        Assert.Equal(ExecutionMode.Simulator, (await h.GetTaskAsync(t.Id)).ExecutionMode);
        await h.Scoped(sp => sp.GetRequiredService<SimulationDriver>().RunAsync(t.Id));
        Assert.Equal(TaskState.Completed, (await h.GetTaskAsync(t.Id)).State);
        Assert.Single(await h.RacksAtAsync("F5"));
    }

    [Fact]
    public async Task Choosing_again_after_it_was_chosen_changes_nothing_and_a_restart_keeps_the_choice()
    {
        await using var h = await ProductionAsync();
        await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Simulator), "admin"));
        var again = await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Simulator), "admin"));
        Assert.False(again.Succeeded); Assert.Contains(again.Failed, c => c.Code == "ALREADY_IN_MODE");
        await h.RestartAsync(); await Svc(h, s => s.InitializeAsync().ContinueWith(_ => 0));
        Assert.True(Guard(h).Allowed);
    }

    [Fact]
    public async Task Choosing_Simulator_never_opens_the_real_path_and_choosing_Real_still_needs_every_condition()
    {
        await using var h = await ProductionAsync();
        var real = await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Real), "admin"));
        Assert.False(real.Succeeded); Assert.Contains(real.Failed, c => c.Code == "REAL_DISPATCH_ENABLED");
        Assert.False(Guard(h).Allowed);                                                             // a refused switch opens nothing
        await Assert.ThrowsAsync<AppException>(() => Svc(h, s => s.TransitionAsync(new ModeTransitionRequest("x", ExecutionMode.Simulator, " ", false), "admin")));   // a reason is still required
        Assert.False(Guard(h).Allowed);
    }
}
