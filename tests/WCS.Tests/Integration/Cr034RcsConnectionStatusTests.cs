using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Fleet;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-034: ONE rule for the link to the RCS, taken from the fleet polling (the same view the AMR monitoring shows; the header reads it too).
/// Connected = last poll ok and fresh; Degraded = last poll failed but the last success is still fresh; Disconnected = no fresh success; NoData = no poll yet.
/// Freshness = Rcs:Fleet:StaleAfterSeconds (default three polling intervals, at least 15 s). The manual RCS Diagnostics never feeds this state.
/// </summary>
[Collection("Database")]
public class Cr034RcsConnectionStatusTests(DatabaseFixture fixture)
{
    private static Task<bool> Refresh(Harness h) => h.Scoped(sp => sp.GetRequiredService<FleetService>().RefreshAsync(CancellationToken.None, true));
    private static Task<FleetView> View(Harness h) => h.Scoped(sp => sp.GetRequiredService<FleetService>().GetAsync(true));

    [Fact]
    public async Task No_poll_yet_is_NoData_and_a_successful_fresh_poll_is_Connected()
    {
        await using var h = await Harness.CreateAsync(fixture);
        Assert.Equal(FleetStates.NoData, (await View(h)).Connection);                  // the header shows "not checked", never "connected"
        h.Rcs.SetFleetSize(2); await Refresh(h);
        var v = await View(h);
        Assert.Equal(FleetStates.Connected, v.Connection); Assert.Equal(0, v.ConsecutiveFailures); Assert.Null(v.LastErrorCode);
    }

    [Fact]
    public async Task A_temporary_failure_is_Degraded_a_long_one_is_Disconnected_and_the_next_success_is_Connected_again_with_the_threshold_from_the_configured_interval()
    {
        await using var h = await Harness.CreateAsync(fixture);
        h.Rcs.SetFleetSize(2); await Refresh(h);
        Assert.Equal(FleetStates.Connected, (await View(h)).Connection);
        var stale = (await View(h)).StaleAfterSeconds;
        Assert.Equal(15, stale);                                                         // 5 s interval: three intervals, at least 15 s (taken from the configuration, not a literal of the screen)

        h.Rcs.FleetFailure = RcsFleetResult.Fail(FleetErrorCodes.Timeout, "t");
        h.Clock.Advance(TimeSpan.FromSeconds(6)); await Refresh(h);                       // one missed poll, last success 6 s ago
        var degraded = await View(h);
        Assert.Equal(FleetStates.Degraded, degraded.Connection); Assert.Equal(1, degraded.ConsecutiveFailures);
        Assert.Equal(2, degraded.Amrs.Count);                                            // the last snapshot is kept (shown as not updated)

        h.Clock.Advance(TimeSpan.FromSeconds(6)); await Refresh(h);                       // 12 s: still inside the window
        Assert.Equal(FleetStates.Degraded, (await View(h)).Connection);
        h.Clock.Advance(TimeSpan.FromSeconds(6)); await Refresh(h);                       // 18 s: past the freshness window
        var down = await View(h);
        Assert.Equal(FleetStates.Disconnected, down.Connection); Assert.Equal(3, down.ConsecutiveFailures); Assert.Equal(2, down.Amrs.Count);

        h.Rcs.FleetFailure = null;
        h.Clock.Advance(TimeSpan.FromSeconds(6)); await Refresh(h);                       // recovery
        var back = await View(h);
        Assert.Equal(FleetStates.Connected, back.Connection); Assert.Equal(0, back.ConsecutiveFailures); Assert.Null(back.LastErrorCode);
    }

    [Fact]
    public async Task The_threshold_follows_the_configured_polling_interval()
    {
        await using var h = await Harness.CreateAsync(fixture, s => s.Configure<RcsFleetOptions>(o => o.IntervalSeconds = 20));
        h.Rcs.SetFleetSize(1); await Refresh(h);
        Assert.Equal(60, (await View(h)).StaleAfterSeconds);                             // three 20 s intervals
        h.Rcs.FleetFailure = RcsFleetResult.Fail(FleetErrorCodes.Timeout, "t");
        h.Clock.Advance(TimeSpan.FromSeconds(30)); await Refresh(h);
        Assert.Equal(FleetStates.Degraded, (await View(h)).Connection);                  // 30 s without a success is still inside 60 s
        h.Clock.Advance(TimeSpan.FromSeconds(40)); await Refresh(h);
        Assert.Equal(FleetStates.Disconnected, (await View(h)).Connection);
    }

    [Fact]
    public async Task The_service_has_one_answer_for_every_reader_so_the_header_and_the_monitoring_cannot_disagree()
    {
        await using var h = await Harness.CreateAsync(fixture);
        h.Rcs.SetFleetSize(2); await Refresh(h);
        h.Rcs.FleetFailure = RcsFleetResult.Fail(FleetErrorCodes.Timeout, "t");
        h.Clock.Advance(TimeSpan.FromSeconds(6)); await Refresh(h);
        var header = await View(h); var monitoring = await h.Scoped(sp => sp.GetRequiredService<FleetService>().GetAsync(false));   // Admin and Operator read the same state
        Assert.Equal(header.Connection, monitoring.Connection); Assert.Equal(header.LastSuccessAt, monitoring.LastSuccessAt); Assert.Equal(header.ConsecutiveFailures, monitoring.ConsecutiveFailures);
        Assert.Equal(FleetStates.Degraded, header.Connection);
    }

    [Fact]
    public async Task The_manual_diagnostics_never_feeds_the_fleet_link_state()
    {
        // a manual RCS call goes through RcsDiagnosticsService (its own history table); it writes nothing to the fleet state, so it can never keep "Connected" alive (see also
        // Cr032RcsDiagnosticsTests: after executed APIs the fleet state table is empty). Here: with the polling failing for longer than the window, the link is Disconnected whatever else happens.
        await using var h = await Harness.CreateAsync(fixture);
        h.Rcs.SetFleetSize(1); await Refresh(h);
        h.Rcs.FleetFailure = RcsFleetResult.Fail(FleetErrorCodes.ConnectionFailed, "down");
        h.Clock.Advance(TimeSpan.FromSeconds(20)); await Refresh(h);
        Assert.Equal(FleetStates.Disconnected, (await View(h)).Connection);
        var inputs = typeof(FleetService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name);
        Assert.DoesNotContain(inputs, n => n.Contains("Diagnostics"));                    // the fleet service has no dependency on the diagnostics module
    }
}
