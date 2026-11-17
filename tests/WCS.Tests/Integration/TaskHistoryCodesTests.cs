using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Application;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Tasks;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>
/// Bilingual task history: every entry WCS writes carries a stable code + parameters next to the original English text, and entries written before codes
/// existed are recognised only through the exact formats WCS itself produced (anything else keeps its raw text).
/// </summary>
[Collection("Database")]
public class TaskHistoryCodesTests(DatabaseFixture fixture)
{
    // ------------------------------------------------------------------------------------------------------------- reading old entries
    [Theory]
    [InlineData("STATE_CHANGED", "Validating business conditions", "VALIDATING", null, null)]
    [InlineData("STATE_CHANGED", "AGV busy; request queued", "QUEUED_AGV_BUSY", null, null)]
    [InlineData("STATE_CHANGED", "Waiting for PLC permission (Drop)", "WAITING_PLC_PERMISSION", "leg", "Drop")]
    [InlineData("STATE_CHANGED", "Destination assigned: B2", "DESTINATION_ASSIGNED", "location", "B2")]
    [InlineData("STATE_CHANGED", "Cancel requested by tester: stop: now", "CANCEL_REQUESTED", "reason", "stop: now")]
    [InlineData("STATE_CHANGED", "TIMEOUT_Executing", "TIMEOUT", "state", "Executing")]
    [InlineData("STATE_CHANGED", "RCS_REJECTED: busy", "RCS_REJECTED", "message", "busy")]
    [InlineData("STATE_CHANGED", "NO_SOURCE_AVAILABLE", "NO_SOURCE_AVAILABLE", null, null)]
    [InlineData("TASK_CREATED", "flow=Bp01", "TASK_CREATED", "flow", "Bp01")]
    [InlineData("TASK_EDITED", "flow Bp01/A1 -> Bp02/B1", "TASK_EDITED", "station", "B1")]
    [InlineData("SOURCE_RESERVED", "location=A1", "SOURCE_RESERVED", "location", "A1")]
    [InlineData("LATE_EVENT_IGNORED", "DropConfirmed after Failed", "LATE_EVENT_IGNORED", "state", "Failed")]
    [InlineData("AGV_DISPATCH_HOLD", "AGV1 placed on dispatch hold: RCS_ENDED_WITHOUT_DROP", "AGV_DISPATCH_HOLD", "reason", "RCS_ENDED_WITHOUT_DROP")]
    [InlineData("RECONCILED", "actual=A2; by=admin1; checked on site", "RECONCILED", "note", "checked on site")]
    public void An_old_entry_in_a_format_WCS_wrote_is_recognised_with_its_parameters(string ev, string text, string code, string? arg, string? value)
    {
        var d = HistoryDetail.Legacy(ev, text);
        Assert.NotNull(d);
        Assert.Equal(code, d!.Code);
        if (arg is not null) Assert.Equal(value, d.Args![arg]);
    }

    [Theory]
    [InlineData("STATE_CHANGED", "Something an engineer typed")]
    [InlineData("STATE_CHANGED", "Validating business conditions (edited)")]      // not an exact match: never guessed
    [InlineData("SOURCE_RESERVED", "location=")]
    [InlineData("LATE_EVENT_IGNORED", "free text with spaces")]
    [InlineData("RCS_DISPATCH_UNKNOWN", "OK")]                                       // a raw RCS message
    [InlineData("PICK_CONFIRMED", "UNKNOWN_CODE_FROM_ELSEWHERE")]
    public void Text_that_cannot_be_identified_reliably_stays_raw(string ev, string text) => Assert.Null(HistoryDetail.Legacy(ev, text));

    [Fact]
    public void An_event_scoped_format_is_not_applied_to_other_events() =>
        Assert.Null(HistoryDetail.Legacy("PICK_CONFIRMED", "location=A1"));

    // ------------------------------------------------------------------------------------------------------------- new entries
    [Fact]
    public async Task New_entries_carry_a_code_and_parameters_and_keep_the_original_text()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        await h.StockAsync("A1", RackKind.Full, 1);
        h.SchedulerOptions.RcsRetryInitialDelayMs = 1000; h.SchedulerOptions.RcsRetryMaxDelayMs = 4000; h.SchedulerOptions.RcsMaxAttempts = 4;

        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(t.Id, "operator stop", "tester"));
        h.Rcs.Unreachable = true;
        for (var i = 0; i < 30; i++) { h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.TickAsync(); }
        var task = await h.GetTaskAsync(t.Id);

        // every history entry that WCS itself words has a code (raw external messages - RCS answers, exception texts - legitimately have none)
        var own = task.History.Where(x => x.EventName is "TASK_CREATED" or "STATE_CHANGED" or "SOURCE_RESERVED" or "DESTINATION_RESERVED" or "RCS_CANCEL_RETRY_EXHAUSTED"
            or "AGV_DISPATCH_HOLD" or "RECONCILIATION_REQUIRED" or "WAITING_FOR_SOURCE").ToList();
        Assert.True(own.Count >= 5);
        Assert.All(own, x => Assert.False(string.IsNullOrEmpty(x.DetailCode), $"{x.EventName}: {x.Detail}"));
        Assert.All(own.Where(x => x.Detail is not null), x => Assert.False(string.IsNullOrWhiteSpace(x.Detail)));   // the original text is kept

        var cancel = own.First(x => x.DetailCode == "CANCEL_REQUESTED");
        var args = HistoryDetail.ParseArgs(cancel.DetailArgsJson)!;
        Assert.Equal("tester", args["user"]); Assert.Equal("operator stop", args["reason"]);
        Assert.StartsWith("Cancel requested by tester", cancel.Detail);
        Assert.Equal(4, int.Parse(HistoryDetail.ParseArgs(own.First(x => x.EventName == "RCS_CANCEL_RETRY_EXHAUSTED").DetailArgsJson)!["attempts"]));

        // the DTOs expose the codes for the state reason and for every entry
        var dto = task.ToDto();
        Assert.Equal("CANCEL_RETRY_LIMIT_REACHED", dto.StateReasonCode);
        Assert.Equal(task.StateReason, dto.StateReason);
        Assert.Contains(task.History.Select(x => x.ToDto()), x => x.Event == "AGV_DISPATCH_HOLD" && x.DetailCode == "AGV_DISPATCH_HOLD" && x.DetailArgs!["agv"] == "AGV1");
    }

    [Fact]
    public async Task An_entry_written_without_a_code_is_still_exposed_through_the_exact_old_format()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync();
        await h.StockAsync("A1", RackKind.Full, 1);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        var task = await h.GetTaskAsync(t.Id);
        // simulate a row from before the migration: no stored code, original text only
        var legacy = task.History.First(x => x.EventName == "STATE_CHANGED");
        typeof(TaskHistoryEntry).GetProperty(nameof(TaskHistoryEntry.DetailCode))!.SetValue(legacy, null);
        typeof(TaskHistoryEntry).GetProperty(nameof(TaskHistoryEntry.DetailArgsJson))!.SetValue(legacy, null);
        var dto = legacy.ToDto();
        Assert.NotNull(dto.Detail);
        Assert.False(string.IsNullOrEmpty(dto.DetailCode));            // identified from the exact text WCS wrote
    }
}
