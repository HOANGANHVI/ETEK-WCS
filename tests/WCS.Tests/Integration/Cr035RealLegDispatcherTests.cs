using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Config;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-035: routing legs through the REAL RCS client (against the mock RCS of CR-030 - nothing is verified against a real RCS-2000). The leg's own locations are used; the RCS is asked before a send; nothing is resent blindly.</summary>
[Collection("Database")]
public class Cr035RealLegDispatcherTests(DatabaseFixture fixture)
{
    private const string Token = "tok-123";

    private static Dictionary<string, string?> Cfg(bool template) => new()
    {
        ["Rcs:RealDispatch:Enabled"] = "true", ["Rcs:RealDispatch:TimeoutSeconds"] = "1", ["Rcs:RealDispatch:ClientCode"] = "WCS", ["Rcs:Dispatch:Mode"] = "RcsManaged",
        ["Rcs:RealDispatch:Locations:F1:PositionCode"] = "P-F1", ["Rcs:RealDispatch:Locations:F1:Type"] = "00",
        ["Rcs:RealDispatch:Locations:F2:PositionCode"] = "P-F2", ["Rcs:RealDispatch:Locations:F2:Type"] = "00",
        ["Rcs:RealDispatch:Locations:B1:PositionCode"] = "P-B1", ["Rcs:RealDispatch:Locations:B1:Type"] = "00",
        ["Rcs:RealDispatch:LegTemplate:TaskTyp"] = template ? "TPL-LEG" : "",
        ["Rcs:RealDispatch:LegTemplate:PositionPath:0"] = "Source", ["Rcs:RealDispatch:LegTemplate:PositionPath:1"] = "Destination",
    };

    private async Task<(Harness H, MockRcsServer Rcs)> SetupAsync(bool template = true)
    {
        var rcs = await MockRcsServer.StartAsync(Token);
        var h = await Harness.CreateAsync(fixture, null, Cfg(template));
        await h.Scoped(sp => sp.GetRequiredService<ConnectionConfigService>().UpdateRcsAsync(
            new UpdateRcsCommand(true, new RcsSettings("MockRCS", "127.0.0.1", rcs.Port, "http://127.0.0.1", ConnectionOptions.AuthToken, null, 5000), null, false, Token, false, "test")));
        return (h, rcs);
    }

    private static LegDispatchRequest Req(string code, string src = "F1", string? dst = "F2") => new(Guid.NewGuid(), code, null, RackKind.Full, src, dst);
    private static Task<LegDispatchResult> Send(Harness h, LegDispatchRequest r) => h.Scoped(sp => sp.GetRequiredService<ILegDispatcher>().DispatchAsync(r));

    [Fact]
    public async Task Without_a_leg_template_the_leg_is_blocked_and_nothing_reaches_the_RCS()
    {
        var (h, rcs) = await SetupAsync(template: false); await using var _h = h; await using var _r = rcs;
        var r = await Send(h, Req("L-1"));
        Assert.Equal(LegDispatchOutcome.Blocked, r.Outcome);
        Assert.Equal(0, rcs.Count("genAgvSchedulingTask")); Assert.Equal(0, rcs.Count("queryTaskStatus"));
    }

    [Fact]
    public async Task The_task_uses_the_locations_of_the_leg_and_a_repeated_dispatch_adopts_the_RCS_task_instead_of_creating_another()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        var first = await Send(h, Req("L-2", "F1", "B1"));
        Assert.Equal(LegDispatchOutcome.Accepted, first.Outcome);
        Assert.Equal(1, rcs.Count("genAgvSchedulingTask"));
        var body = rcs.Bodies("genAgvSchedulingTask").Single().RootElement;
        Assert.Equal("TPL-LEG", body.GetProperty("taskTyp").GetString());                       // only the type comes from the template
        Assert.Equal("L-2", body.GetProperty("taskCode").GetString());
        var path = body.GetProperty("positionCodePath").EnumerateArray().Select(p => p.GetProperty("positionCode").GetString()).ToArray();
        Assert.Equal(new[] { "P-F1", "P-B1" }, path);                                          // source and destination of the LEG, not of the template

        var again = await Send(h, Req("L-2", "F1", "B1"));                                      // e.g. the answer of the first send was lost
        Assert.Equal(LegDispatchOutcome.Accepted, again.Outcome);
        Assert.Equal(1, rcs.Count("genAgvSchedulingTask")); Assert.Equal(1, rcs.TaskCount);     // no duplicate RCS task
    }

    [Fact]
    public async Task When_the_RCS_cannot_be_asked_nothing_is_sent_and_an_incomplete_leg_is_never_sent()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        var incomplete = await Send(h, Req("L-3", "F1", null));
        Assert.Equal(LegDispatchOutcome.Blocked, incomplete.Outcome); Assert.Equal("LEG_LOCATIONS_INCOMPLETE", incomplete.Reason);
        Assert.Equal(0, rcs.Count("genAgvSchedulingTask"));

        await rcs.StopAsync();                                                                  // the RCS is unreachable: the earlier outcome (if any) is unknown
        var down = await Send(h, Req("L-4"));
        Assert.Equal(LegDispatchOutcome.Blocked, down.Outcome); Assert.Equal("RCS_STATE_UNKNOWN_NOT_RESENT", down.Reason);
    }

    [Fact]
    public async Task A_location_without_an_RCS_mapping_is_rejected_before_anything_is_sent()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        var r = await Send(h, Req("L-5", "F1", "ZZ"));                                          // ZZ has no Rcs:RealDispatch:Locations entry
        Assert.Equal(LegDispatchOutcome.Rejected, r.Outcome);
        Assert.Equal(0, rcs.Count("genAgvSchedulingTask"));
    }
}
