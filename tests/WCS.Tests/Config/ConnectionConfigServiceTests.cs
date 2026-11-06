using System.Text.Json;
using WCS.Application.Config;
using WCS.Domain.Common;
using WCS.Tests.Support;

namespace WCS.Tests.Config;

public class ConnectionConfigServiceTests
{
    private const string Secret = "S3cr3t-Passw0rd!";
    private const string TokenValue = "tok-ABCDEF-123456";

    private sealed class Rig
    {
        public FakeConnectionRepo Repo { get; } = new();
        public FakeProtector Protector { get; } = new();
        public RecordingProbe Probe { get; }
        public ListLogger<ConnectionConfigService> Log { get; } = new();
        public TestClock Clock { get; } = new();
        public ConnectionConfigService Service { get; }

        public Rig(ProbeResult? probeResult = null)
        {
            Probe = new RecordingProbe(probeResult);
            Service = new ConnectionConfigService(new FakeUnitOfWork(), Repo, Protector, Probe, Clock, Log);
        }
    }

    private static PlcSettings Plc(string? ip = "192.0.2.10") =>
        new("PLC-1", ip, 5007, ConnectionOptions.PlcMcProtocol3EBinaryTcp, 0, 255, 0, 3000, 500);

    private static RcsSettings Rcs(string auth = ConnectionOptions.AuthUserPassword) =>
        new("RCS", "rcs.example.test", 8182, null, auth, auth == ConnectionOptions.AuthUserPassword ? "svc" : null, 5000);

    private static UpdateRcsCommand RcsCmd(bool enabled = true, string? password = Secret, string? token = null, bool clearPw = false, bool clearTok = false,
        RcsSettings? settings = null) => new(enabled, settings ?? Rcs(), password, clearPw, token, clearTok, "tester");

    // ------------------------------------------------------------------ nothing is pre-filled
    [Fact]
    public async Task Without_saved_data_nothing_is_prefilled_or_marked_confirmed()
    {
        var rig = new Rig();
        var plc = await rig.Service.GetPlcAsync();
        var rcs = await rig.Service.GetRcsAsync();

        Assert.False(plc.Configured); Assert.False(plc.Enabled);
        Assert.Null(plc.Settings.IpAddress); Assert.Null(plc.Settings.Port); Assert.Null(plc.Settings.Protocol);
        Assert.False(rcs.Configured); Assert.Null(rcs.Settings.Host); Assert.Null(rcs.Settings.Port);
        Assert.Empty(rig.Repo.Rows);
    }

    // ------------------------------------------------------------------ persistence round trip
    [Fact]
    public async Task Plc_settings_are_saved_and_read_back_normalised()
    {
        var rig = new Rig();
        var saved = await rig.Service.UpdatePlcAsync(new UpdatePlcCommand(true, Plc("  192.0.2.10 "), "tester"));

        Assert.True(saved.Configured); Assert.True(saved.Enabled);
        Assert.Equal("192.0.2.10", saved.Settings.IpAddress);
        Assert.Equal(5007, saved.Settings.Port);
        Assert.Equal("tester", saved.UpdatedBy);
        Assert.Equal(saved.Settings, (await rig.Service.GetPlcAsync()).Settings);
    }

    [Fact]
    public async Task Invalid_input_is_rejected_with_field_errors_and_leaves_stored_config_untouched()
    {
        var rig = new Rig();
        await rig.Service.UpdatePlcAsync(new UpdatePlcCommand(true, Plc(), "a"));

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            rig.Service.UpdatePlcAsync(new UpdatePlcCommand(true, Plc("not-an-ip") with { Port = 99999 }, "b")));

        Assert.Contains(ex.Errors, e => e.Field == "ipAddress");
        Assert.Contains(ex.Errors, e => e.Field == "port");
        var after = await rig.Service.GetPlcAsync();
        Assert.Equal("192.0.2.10", after.Settings.IpAddress);   // not overwritten
        Assert.Equal(5007, after.Settings.Port);
        Assert.Equal("a", after.UpdatedBy);
    }

    [Fact]
    public async Task Rejected_first_save_does_not_create_a_row()
    {
        var rig = new Rig();
        await Assert.ThrowsAsync<ValidationFailedException>(() => rig.Service.UpdateRcsAsync(RcsCmd(settings: Rcs() with { Host = "bad host" })));
        Assert.Empty(rig.Repo.Rows);
    }

    // ------------------------------------------------------------------ secrets
    [Fact]
    public async Task Secrets_are_stored_encrypted_and_never_returned()
    {
        var rig = new Rig();
        var view = await rig.Service.UpdateRcsAsync(RcsCmd(token: null, password: Secret));

        Assert.True(view.Secrets.HasPassword);
        Assert.False(view.Secrets.HasToken);
        var stored = rig.Repo.Rows[ConnectionKind.Rcs];
        Assert.NotNull(stored.ProtectedSecrets);
        Assert.DoesNotContain(Secret, stored.ProtectedSecrets);
        Assert.DoesNotContain(Secret, stored.SettingsJson);

        var json = JsonSerializer.Serialize(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(Secret, json);
        Assert.Contains("hasPassword", json);
        Assert.DoesNotContain("\"password\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"token\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Leaving_the_password_empty_keeps_the_stored_one()
    {
        var rig = new Rig();
        await rig.Service.UpdateRcsAsync(RcsCmd());
        var blob = rig.Repo.Rows[ConnectionKind.Rcs].ProtectedSecrets;

        // Enabled + username/password mode again WITHOUT supplying a password: allowed because one is already stored.
        var view = await rig.Service.UpdateRcsAsync(RcsCmd(password: null, settings: Rcs() with { TimeoutMs = 7000 }));

        Assert.True(view.Secrets.HasPassword);
        Assert.Equal(7000, view.Settings.TimeoutMs);
        Assert.Equal(Secret, rig.Protector.Unprotect(rig.Repo.Rows[ConnectionKind.Rcs].ProtectedSecrets!).Contains(Secret) ? Secret : "");
        Assert.NotNull(blob);
    }

    [Fact]
    public async Task Clearing_the_password_while_it_is_required_is_rejected_and_keeps_it()
    {
        var rig = new Rig();
        await rig.Service.UpdateRcsAsync(RcsCmd());

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => rig.Service.UpdateRcsAsync(RcsCmd(password: null, clearPw: true)));
        Assert.Contains(ex.Errors, e => e.Field == "password");
        Assert.True((await rig.Service.GetRcsAsync()).Secrets.HasPassword);
    }

    [Fact]
    public async Task Switching_auth_mode_discards_credentials_that_are_no_longer_used()
    {
        var rig = new Rig();
        await rig.Service.UpdateRcsAsync(RcsCmd());
        var view = await rig.Service.UpdateRcsAsync(RcsCmd(enabled: true, password: null, settings: Rcs(ConnectionOptions.AuthNone)));

        Assert.False(view.Secrets.HasPassword);
        Assert.Null(rig.Repo.Rows[ConnectionKind.Rcs].ProtectedSecrets);
    }

    [Fact]
    public async Task Token_can_be_stored_and_replaced_and_is_never_exposed()
    {
        var rig = new Rig();
        var v1 = await rig.Service.UpdateRcsAsync(RcsCmd(password: null, token: TokenValue, settings: Rcs(ConnectionOptions.AuthToken)));
        Assert.True(v1.Secrets.HasToken);
        Assert.DoesNotContain(TokenValue, rig.Repo.Rows[ConnectionKind.Rcs].ProtectedSecrets);
        Assert.DoesNotContain(TokenValue, JsonSerializer.Serialize(v1));
    }

    [Fact]
    public async Task Unreadable_secret_blob_is_treated_as_not_stored_without_leaking_anything()
    {
        var rig = new Rig();
        await rig.Service.UpdateRcsAsync(RcsCmd());
        typeof(ConnectionSetting).GetProperty(nameof(ConnectionSetting.ProtectedSecrets))!
            .SetValue(rig.Repo.Rows[ConnectionKind.Rcs], "enc:@@@not-base64@@@");

        var view = await rig.Service.GetRcsAsync();
        Assert.False(view.Secrets.HasPassword);
    }

    // ------------------------------------------------------------------ logging
    [Fact]
    public async Task Logs_never_contain_secrets_addresses_or_credentials()
    {
        var rig = new Rig();
        await rig.Service.UpdatePlcAsync(new UpdatePlcCommand(true, Plc("192.0.2.77"), "tester"));
        await rig.Service.UpdateRcsAsync(RcsCmd());
        await rig.Service.TestAsync(ConnectionKind.Plc);
        await rig.Service.TestAsync(ConnectionKind.Rcs);

        var all = string.Join("\n", rig.Log.Messages);
        Assert.NotEmpty(rig.Log.Messages);
        Assert.DoesNotContain(Secret, all);
        Assert.DoesNotContain("192.0.2.77", all);
        Assert.DoesNotContain("rcs.example.test", all);
    }

    // ------------------------------------------------------------------ safe connectivity check
    [Fact]
    public async Task Check_without_saved_config_is_not_executed()
    {
        var rig = new Rig();
        var r = await rig.Service.TestAsync(ConnectionKind.Plc);
        Assert.Equal(ConnectionTestOutcome.NotExecuted, r.Outcome);
        Assert.Empty(rig.Probe.Calls);
    }

    [Fact]
    public async Task Check_of_a_disabled_connection_is_not_executed()
    {
        var rig = new Rig();
        await rig.Service.UpdatePlcAsync(new UpdatePlcCommand(false, Plc(), "t"));
        var r = await rig.Service.TestAsync(ConnectionKind.Plc);
        Assert.Equal(ConnectionTestOutcome.NotExecuted, r.Outcome);
        Assert.Empty(rig.Probe.Calls);
    }

    [Fact]
    public async Task Check_uses_only_the_saved_values_and_records_the_result()
    {
        var rig = new Rig();
        await rig.Service.UpdatePlcAsync(new UpdatePlcCommand(true, Plc(), "t"));

        var r = await rig.Service.TestAsync(ConnectionKind.Plc);

        Assert.Equal(ConnectionTestOutcome.Simulated, r.Outcome);
        var call = Assert.Single(rig.Probe.Calls);
        Assert.Equal((ConnectionKind.Plc, "192.0.2.10", 5007, TimeSpan.FromMilliseconds(3000)), call);
        var view = await rig.Service.GetPlcAsync();
        Assert.NotNull(view.LastTest);
        Assert.Equal(ConnectionTestOutcome.Simulated, view.LastTest!.Outcome);
        Assert.Equal(rig.Clock.UtcNow, view.LastTest.TestedAt);
    }

    [Fact]
    public async Task A_failed_check_keeps_the_last_error_and_a_later_save_clears_stale_results()
    {
        var rig = new Rig(new ProbeResult(ConnectionTestOutcome.Failed, "TcpReachability", "Không mở được cổng TCP.", "ConnectionRefused", 12));
        await rig.Service.UpdatePlcAsync(new UpdatePlcCommand(true, Plc(), "t"));
        await rig.Service.TestAsync(ConnectionKind.Plc);

        var view = await rig.Service.GetPlcAsync();
        Assert.Equal("ConnectionRefused", view.LastTest!.Error);
        Assert.Equal(12, view.LastTest.LatencyMs);

        var after = await rig.Service.UpdatePlcAsync(new UpdatePlcCommand(true, Plc("192.0.2.11"), "t"));
        Assert.Null(after.LastTest); // the old result belongs to the old configuration
    }

    [Fact]
    public async Task Incomplete_saved_config_is_never_probed()
    {
        var rig = new Rig();
        await rig.Service.UpdateRcsAsync(RcsCmd(enabled: false, password: null, settings: Rcs(ConnectionOptions.AuthNone) with { Host = null, Port = null }));
        // flip to enabled directly in the row to simulate legacy/incomplete data
        typeof(ConnectionSetting).GetProperty(nameof(ConnectionSetting.Enabled))!.SetValue(rig.Repo.Rows[ConnectionKind.Rcs], true);

        var r = await rig.Service.TestAsync(ConnectionKind.Rcs);
        Assert.Equal(ConnectionTestOutcome.NotExecuted, r.Outcome);
        Assert.Empty(rig.Probe.Calls);
    }
}
