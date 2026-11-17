using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using Serilog.Parsing;
using WCS.Api.Logging;
using WCS.Application.Auth;
using WCS.Application.Config;
using WCS.Application.Logging;
using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Domain.Users;
using WCS.Domain.Logging;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Tests.Integration;
using WCS.Tests.Support;

namespace WCS.Tests.Logging;

/// <summary>
/// Bilingual UI support: every validation error carries a stable code (the UI translates the code, not the sentence), system events keep their message
/// template, and PLC events keep their parameters, so old and new entries can be shown in the language the user selected.
/// </summary>
[Collection("Database")]
public class BilingualLogTests(DatabaseFixture fixture)
{
    // ------------------------------------------------------------------------------------------------------------- field errors carry codes
    [Fact]
    public void Every_connection_validation_error_has_a_code()
    {
        var plc = ConnectionValidator.ValidatePlc(new PlcSettings("x", "999.1.1.1", 70000, "nope", 999, 999, 999, 5, 1), enabled: true);
        var rcs = ConnectionValidator.ValidateRcs(new RcsSettings(new string('x', 200), "bad host!", 0, "ftp://u:p@h", "weird", new string('u', 200), 1), enabled: true, hasPassword: false, hasToken: false);
        var missing = ConnectionValidator.ValidatePlc(new PlcSettings(null, null, null, null, null, null, null, null, null), enabled: true);
        var all = plc.Concat(rcs).Concat(missing).ToList();
        Assert.True(all.Count > 10);
        Assert.All(all, e => Assert.False(string.IsNullOrWhiteSpace(e.Code), $"{e.Field}: {e.Message}"));
        var range = all.First(e => e.Code == "RANGE");
        Assert.True(range.Args!.ContainsKey("min") && range.Args.ContainsKey("max"));   // the numbers travel as data, the label is chosen by the UI
    }

    [Fact]
    public void Password_policy_errors_have_codes_and_parameters()
    {
        var errors = PasswordPolicy.Check("a", "alice");
        Assert.All(errors, e => Assert.False(string.IsNullOrWhiteSpace(e.Code)));
        Assert.Contains(errors, e => e.Code == "PWD_TOO_SHORT" && e.Args!["min"] is int);
        Assert.Equal("PWD_REQUIRED", PasswordPolicy.Check("", "alice").Single().Code);
    }

    [Fact]
    public void Address_issues_carry_a_code_and_arguments_for_every_kind()
    {
        PointSpec Spec(string address, PlcDataType type, PlcSignalDirection dir = PlcSignalDirection.PlcToSoftware) =>
            new(Guid.NewGuid(), "A1", "Sig", type, address, null, dir);
        var format = AddressValidator.ValidateOne(Spec("Q1", PlcDataType.Bool))!;
        Assert.Equal("ADDRESS_FORMAT", format.Code); Assert.Equal("Q1", format.Args!["address"]);
        var radix = AddressValidator.ValidateOne(Spec("X10", PlcDataType.Bool))!;
        Assert.Equal("RADIX_UNCONFIRMED", radix.Code); Assert.Equal("xy", radix.Args!["kind"]);
        var bit = AddressValidator.ValidateOne(Spec("D10", PlcDataType.Bool))!;
        Assert.Equal("TYPE_DEVICE", bit.Code); Assert.Equal("bool", bit.Args!["kind"]);
        var word = AddressValidator.ValidateOne(Spec("M10", PlcDataType.Int))!;
        Assert.Equal("TYPE_DEVICE", word.Code); Assert.Equal("word", word.Args!["kind"]);
    }

    // ------------------------------------------------------------------------------------------------------------- system log template
    [Fact]
    public void A_system_event_keeps_its_message_template_next_to_the_rendered_text()
    {
        var parser = new MessageTemplateParser();
        var e = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Warning, null,
            parser.Parse("AGV {Agv} placed on dispatch hold because of task {TaskId}: {Reason}"),
            [new("SourceContext", new ScalarValue("WCS.Application.Services.TaskOrchestrator")), new("Agv", new ScalarValue("AGV1")), new("TaskId", new ScalarValue("abc")), new("Reason", new ScalarValue("RCS_ENDED_WITHOUT_DROP"))]);
        var entry = LogClassifier.ToEntry(e)!;
        Assert.Equal("AGV {Agv} placed on dispatch hold because of task {TaskId}: {Reason}", entry.MessageTemplate);
        Assert.Contains("AGV1", entry.Message);                                   // the rendered text is still the raw original
        Assert.Contains("\"Agv\":\"AGV1\"", entry.DetailsJson);                   // and the parameters are stored, so any language can be rendered later
    }

    // ------------------------------------------------------------------------------------------------------------- PLC event parameters through the API
    [Fact]
    public async Task Plc_event_parameters_and_the_system_log_template_reach_the_log_api()
    {
        var h = await Harness.CreateAsync(fixture); await using var _h = h;
        await using var f = new DbApiFactory(fixture);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Add(TestUsers.Make("admin1", UserRole.Admin));
            db.SystemLog.Add(new SystemLogEntry(DateTimeOffset.UtcNow.AddMinutes(-1), "RCS_AGV", "Warning", "WCS.Test", "AGV1", null,
                "AGV AGV1 placed on dispatch hold because of task T: X", detailsJson: "{\"Agv\":\"AGV1\",\"TaskId\":\"T\",\"Reason\":\"X\"}",
                messageTemplate: "AGV {Agv} placed on dispatch hold because of task {TaskId}: {Reason}"));
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>().SeedAsync();
        }
        f.Services.GetRequiredService<IPlcEvents>().Log(PlcLogLevel.Warning, "PLC-01", null, "CONNECT_FAILED", "Connection failed: boom. Retrying in 500 ms.",
            LogArgs.Of(("error", "boom"), ("backoffMs", 500)));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && await h.Db(db => db.PlcCommLog.CountAsync(x => x.Event == "CONNECT_FAILED")) < 1) await Task.Delay(100);

        var admin = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await admin.LoginAsync("admin1"));
        var items = (await admin.SendJsonAsync(HttpMethod.Get, "/api/logs?pageSize=50")).Root.GetProperty("data").GetProperty("items").EnumerateArray().ToList();

        var plc = items.First(i => i.GetProperty("event").GetString() == "CONNECT_FAILED");
        var args = JsonDocument.Parse(plc.GetProperty("detailsJson").GetString()!).RootElement.GetProperty("args");
        Assert.Equal("boom", args.GetProperty("error").GetString()); Assert.Equal(500, args.GetProperty("backoffMs").GetInt32());
        Assert.StartsWith("Connection failed", plc.GetProperty("message").GetString());                 // raw text unchanged

        var sys = items.First(i => i.GetProperty("category").GetString() == "RCS_AGV");
        Assert.Equal("AGV {Agv} placed on dispatch hold because of task {TaskId}: {Reason}", sys.GetProperty("messageTemplate").GetString());
        Assert.Contains("\"Agv\":\"AGV1\"", sys.GetProperty("detailsJson").GetString());
    }
}
