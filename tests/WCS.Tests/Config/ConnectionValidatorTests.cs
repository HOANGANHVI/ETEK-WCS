using WCS.Application.Config;

namespace WCS.Tests.Config;

public class ConnectionValidatorTests
{
    private static PlcSettings Plc(string? ip = "192.0.2.10", int? port = 5007, string? protocol = ConnectionOptions.PlcMcProtocol3EBinaryTcp,
        int? net = 0, int? pc = 255, int? station = 0, int? timeout = 3000, int? poll = 500, string? name = "PLC-1") =>
        new(name, ip, port, protocol, net, pc, station, timeout, poll);

    private static RcsSettings Rcs(string? host = "rcs.example.test", int? port = 8182, string? baseUrl = null, string auth = ConnectionOptions.AuthNone,
        string? user = null, int? timeout = 5000) => new("RCS", host, port, baseUrl, auth, user, timeout);

    private static bool Has(IReadOnlyList<FieldError> e, string field) => e.Any(x => x.Field == field);

    // ------------------------------------------------------------------ PLC
    [Fact]
    public void Valid_plc_settings_have_no_errors() => Assert.Empty(ConnectionValidator.ValidatePlc(Plc(), enabled: true));

    [Fact]
    public void Nothing_is_required_while_the_connection_is_disabled() =>
        Assert.Empty(ConnectionValidator.ValidatePlc(Plc(ip: null, port: null, protocol: null, timeout: null), enabled: false));

    [Theory]
    [InlineData("999.1.1.1")]
    [InlineData("plc.local")]
    [InlineData("192.168.1")]
    public void Plc_ip_must_be_an_ip_address(string ip) => Assert.True(Has(ConnectionValidator.ValidatePlc(Plc(ip: ip), false), "ipAddress"));

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    [InlineData(-1)]
    public void Plc_port_range_is_enforced(int port) => Assert.True(Has(ConnectionValidator.ValidatePlc(Plc(port: port), false), "port"));

    [Theory]
    [InlineData("networkNumber")]
    [InlineData("pcNumber")]
    [InlineData("stationNumber")]
    public void Plc_station_numbers_are_limited_to_0_255(string field)
    {
        var s = field switch { "networkNumber" => Plc(net: 256), "pcNumber" => Plc(pc: -1), _ => Plc(station: 300) };
        Assert.True(Has(ConnectionValidator.ValidatePlc(s, false), field));
    }

    [Fact]
    public void Plc_station_numbers_are_optional_because_the_final_protocol_may_not_need_them() =>
        Assert.Empty(ConnectionValidator.ValidatePlc(Plc(net: null, pc: null, station: null, poll: null), true));

    [Fact]
    public void Plc_unknown_protocol_is_rejected() => Assert.True(Has(ConnectionValidator.ValidatePlc(Plc(protocol: "Modbus"), false), "protocol"));

    [Theory]
    [InlineData(10)]
    [InlineData(70000)]
    public void Plc_timeout_range_is_enforced(int ms) => Assert.True(Has(ConnectionValidator.ValidatePlc(Plc(timeout: ms), false), "timeoutMs"));

    [Fact]
    public void Enabling_an_incomplete_plc_is_rejected_field_by_field()
    {
        var e = ConnectionValidator.ValidatePlc(Plc(ip: null, port: null, protocol: null, timeout: null), enabled: true);
        Assert.All(new[] { "ipAddress", "port", "protocol", "timeoutMs" }, f => Assert.True(Has(e, f), f));
    }

    // ------------------------------------------------------------------ RCS
    [Fact]
    public void Valid_rcs_settings_have_no_errors() => Assert.Empty(ConnectionValidator.ValidateRcs(Rcs(), true, false, false));

    [Theory]
    [InlineData("rcs.example.test")]
    [InlineData("192.0.2.20")]
    [InlineData("rcs-01")]
    public void Rcs_host_accepts_ip_or_hostname(string host) => Assert.Empty(ConnectionValidator.ValidateRcs(Rcs(host: host), true, false, false));

    [Theory]
    [InlineData("bad host")]
    [InlineData("http://rcs")]
    [InlineData("-rcs")]
    public void Rcs_host_rejects_garbage(string host) => Assert.True(Has(ConnectionValidator.ValidateRcs(Rcs(host: host), false, false, false), "host"));

    [Theory]
    [InlineData("ftp://rcs.example.test/")]
    [InlineData("rcs.example.test/api")]
    [InlineData("http://user:pw@rcs.example.test/")]
    [InlineData("http://other.example.test/")]
    public void Rcs_base_url_must_be_a_clean_http_url_that_matches_the_host(string url) =>
        Assert.True(Has(ConnectionValidator.ValidateRcs(Rcs(baseUrl: url), false, false, false), "baseUrl"));

    [Fact]
    public void Rcs_base_url_with_matching_host_is_accepted() =>
        Assert.Empty(ConnectionValidator.ValidateRcs(Rcs(baseUrl: "https://rcs.example.test:8182/base"), true, false, false));

    [Fact]
    public void Rcs_base_url_port_must_match_the_port_field() =>
        Assert.True(Has(ConnectionValidator.ValidateRcs(Rcs(baseUrl: "http://rcs.example.test:9999/"), false, false, false), "baseUrl"));

    [Fact]
    public void Rcs_unknown_auth_mode_is_rejected() => Assert.True(Has(ConnectionValidator.ValidateRcs(Rcs(auth: "Kerberos"), false, false, false), "authMode"));

    [Fact]
    public void Username_password_mode_needs_a_username_and_when_enabled_a_password()
    {
        var e = ConnectionValidator.ValidateRcs(Rcs(auth: ConnectionOptions.AuthUserPassword), enabled: true, hasPassword: false, hasToken: false);
        Assert.True(Has(e, "username"));
        Assert.True(Has(e, "password"));
        Assert.Empty(ConnectionValidator.ValidateRcs(Rcs(auth: ConnectionOptions.AuthUserPassword, user: "svc"), true, hasPassword: true, hasToken: false));
    }

    [Fact]
    public void Token_mode_needs_a_token_when_enabled()
    {
        Assert.True(Has(ConnectionValidator.ValidateRcs(Rcs(auth: ConnectionOptions.AuthToken), true, false, false), "token"));
        Assert.Empty(ConnectionValidator.ValidateRcs(Rcs(auth: ConnectionOptions.AuthToken), true, false, hasToken: true));
        Assert.Empty(ConnectionValidator.ValidateRcs(Rcs(auth: ConnectionOptions.AuthToken), false, false, false)); // disabled: may be saved incomplete
    }

    [Fact]
    public void Clean_trims_and_nulls_blank_values()
    {
        Assert.Null(ConnectionValidator.Clean("   "));
        Assert.Null(ConnectionValidator.Clean(null));
        Assert.Equal("x", ConnectionValidator.Clean(" x "));
    }
}
