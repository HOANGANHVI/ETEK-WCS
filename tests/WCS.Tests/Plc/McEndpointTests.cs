using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Infrastructure.Plc.Mc;
using WCS.Infrastructure.Simulation;

namespace WCS.Tests.Plc;

/// <summary>The point-level driver: grouping, decoding BOOL/INT/STRING, quality of unconfigured points, write encoding. Addresses are TEST values.</summary>
public class McEndpointTests
{
    private static readonly McRoute Route = new(0x00, 0xFF, 0x03FF, 0x00);

    private static PlcEndpointConfig Config(int port) => new("PLC-T", "127.0.0.1", port, "McProtocol3EBinaryTcp", 0, 0xFF, 0x03FF, 0, 0x0010, 400, 100);

    public static IoPointRef Pt(string signal, PlcDataType type, string? address, PlcSignalDirection dir = PlcSignalDirection.PlcToSoftware, int? words = null, string? enc = null, bool write = false, string group = "Status", string station = "A1") =>
        new(Guid.NewGuid(), "PLC-T", station, signal, dir, type, address, words, enc, write, group);

    private static MelsecMcMockServer Server() { var s = new MelsecMcMockServer { ExpectedRoute = Route }; s.Start(); return s; }

    [Fact]
    public void Real_connections_refuse_to_start_with_missing_parameters_instead_of_guessing()
    {
        var ex = Assert.Throws<NeedsConfigurationException>(() => new McPlcEndpoint(new PlcEndpointConfig("PLC-01", "192.168.10.101", null, null, null, null, null, null, null, null, null)));
        foreach (var m in new[] { "port", "protocol", "networkNumber", "pcNumber", "moduleIoNumber", "plcStationNumber", "monitoringTimer", "timeoutMs" }) Assert.Contains(m, ex.Missing);
        Assert.Contains("NEEDS_CONFIGURATION", ex.Message);
        Assert.Throws<NeedsConfigurationException>(() => new McPlcEndpoint(Config(5000) with { Protocol = "Modbus" }));
    }

    [Fact]
    public async Task Adjacent_confirmed_addresses_are_read_in_one_request_and_gaps_are_never_read()
    {
        await using var server = Server();
        server.SetBit(100, true); server.SetBit(102, true); server.SetWord(1510, unchecked((ushort)-5)); server.SetWord(1511, 12);
        await using var ep = new McPlcEndpoint(Config(server.Port));
        await ep.ConnectAsync(default);
        var points = new[]
        {
            Pt("Safety_Ready", PlcDataType.Bool, "M100"), Pt("Pickup_Request", PlcDataType.Bool, "M101"), Pt("Delivery_Request", PlcDataType.Bool, "M102"),   // M100..M102 contiguous
            Pt("Station_Fault", PlcDataType.Bool, "M110"),                                                                                                       // gap: separate request
            Pt("Rack_Full", PlcDataType.Int, "D1510"), Pt("Rack_Empty", PlcDataType.Int, "D1511"),                                                               // D1510..D1511 contiguous
        };
        var r = await ep.ReadStationAsync(points, default);

        Assert.All(r, x => Assert.Equal(PointQuality.Good, x.Quality));
        Assert.Equal(new object[] { true, false, true, false, -5, 12 }, r.Select(x => x.Value!.Value));
        var frames = server.ReceivedFrames.Select(b => McFrame.ParseRequest(b)).ToList();
        Assert.Equal(3, frames.Count);
        Assert.Contains(frames, f => f.Head == new DeviceAddress(PlcDevice.M, 100) && f.Points == 3);
        Assert.Contains(frames, f => f.Head == new DeviceAddress(PlcDevice.M, 110) && f.Points == 1);       // M103..M109 were never read
        Assert.Contains(frames, f => f.Head == new DeviceAddress(PlcDevice.D, 1510) && f.Points == 2);
    }

    [Fact]
    public async Task Points_without_a_confirmed_address_are_never_read_and_report_NotConfigured()
    {
        await using var server = Server();
        await using var ep = new McPlcEndpoint(Config(server.Port));
        var r = await ep.ReadStationAsync([Pt("Safety_Ready", PlcDataType.Bool, null), Pt("Rack_Full", PlcDataType.Int, null)], default);
        Assert.All(r, x => { Assert.Equal(PointQuality.NotConfigured, x.Quality); Assert.Null(x.Value); });
        Assert.Equal(0, server.RequestCount);                         // no traffic at all
    }

    [Fact]
    public async Task STRING_points_are_decoded_as_ASCII_low_byte_first_and_other_encodings_are_refused()
    {
        await using var server = Server();
        server.SetAscii(1500, 5, "REQ-0042");
        await using var ep = new McPlcEndpoint(Config(server.Port));
        var ok = (await ep.ReadStationAsync([Pt("Request_ID", PlcDataType.String, "D1500", words: 5, enc: "ASCII")], default)).Single();
        Assert.Equal("REQ-0042", ok.Value!.Value);
        Assert.Equal((ushort)('R' | ('E' << 8)), server.GetWord(1500));          // first character in the low byte

        var bad = (await ep.ReadStationAsync([Pt("Request_ID", PlcDataType.String, "D1500", words: 5, enc: "UTF-16")], default)).Single();
        Assert.Equal(PointQuality.Bad, bad.Quality); Assert.Contains("ASCII", bad.Error);
        var noLen = (await ep.ReadStationAsync([Pt("Request_ID", PlcDataType.String, "D1500", words: null, enc: "ASCII")], default)).Single();
        Assert.Equal(PointQuality.Bad, noLen.Quality);                          // length unknown: not read
    }

    [Fact]
    public async Task A_PLC_error_on_one_group_marks_only_those_points_Bad_and_the_other_points_stay_Good()
    {
        await using var server = Server();
        server.MaxDeviceNumber = 1000;                                          // D1510 is out of the PLC's range, M100 is not
        server.SetBit(100, true);
        await using var ep = new McPlcEndpoint(Config(server.Port));
        var r = await ep.ReadStationAsync([Pt("Safety_Ready", PlcDataType.Bool, "M100"), Pt("Rack_Full", PlcDataType.Int, "D1510")], default);
        Assert.Equal(PointQuality.Good, r[0].Quality);
        Assert.Equal(PointQuality.Bad, r[1].Quality); Assert.Contains("C056", r[1].Error);
        Assert.Equal(EndpointState.Connected, ep.State);
    }

    [Fact]
    public async Task A_lost_link_marks_the_station_Bad_and_the_endpoint_Faulted_then_recovers()
    {
        await using var server = Server();
        await using var ep = new McPlcEndpoint(Config(server.Port) with { TimeoutMs = 150 });
        server.Mute = true;
        var r = await ep.ReadStationAsync([Pt("Safety_Ready", PlcDataType.Bool, "M1"), Pt("Rack_Full", PlcDataType.Int, "D1")], default);
        Assert.All(r, x => Assert.Equal(PointQuality.Bad, x.Quality));
        Assert.Equal(EndpointState.Faulted, ep.State);
        server.Mute = false; server.SetBit(1, true);
        var again = await ep.ReadStationAsync([Pt("Safety_Ready", PlcDataType.Bool, "M1")], default);
        Assert.Equal(PointQuality.Good, again[0].Quality);
    }

    [Fact]
    public async Task Writes_encode_BOOL_INT_and_STRING_and_refuse_PLC_to_Software_points()
    {
        await using var server = Server();
        await using var ep = new McPlcEndpoint(Config(server.Port));
        await ep.WritePointAsync(Pt("Request_Ack", PlcDataType.Bool, "M200", PlcSignalDirection.SoftwareToPlc), PointValue.OfBool(true), default);
        await ep.WritePointAsync(Pt("AGV_ID", PlcDataType.Int, "D1600", PlcSignalDirection.SoftwareToPlc), PointValue.OfInt(-2), default);
        await ep.WritePointAsync(Pt("Text", PlcDataType.String, "D1700", PlcSignalDirection.SoftwareToPlc, words: 3, enc: "ASCII"), PointValue.OfString("AB1"), default);
        Assert.True(server.GetBit(200));
        Assert.Equal(unchecked((ushort)-2), server.GetWord(1600));
        Assert.Equal((ushort)('A' | ('B' << 8)), server.GetWord(1700)); Assert.Equal((ushort)'1', server.GetWord(1701)); Assert.Equal((ushort)0, server.GetWord(1702));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ep.WritePointAsync(Pt("Pickup_Request", PlcDataType.Bool, "M300"), PointValue.OfBool(true), default));   // inbound signal
        await Assert.ThrowsAsync<InvalidOperationException>(() => ep.WritePointAsync(Pt("Request_Ack", PlcDataType.Bool, null, PlcSignalDirection.SoftwareToPlc), PointValue.OfBool(true), default));
        await Assert.ThrowsAsync<ArgumentException>(() => ep.WritePointAsync(Pt("AGV_ID", PlcDataType.Int, "D1601", PlcSignalDirection.SoftwareToPlc), PointValue.OfBool(true), default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ep.WritePointAsync(Pt("AGV_ID", PlcDataType.Int, "D1601", PlcSignalDirection.SoftwareToPlc), PointValue.OfInt(70000), default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ep.WritePointAsync(Pt("Text", PlcDataType.String, "D1700", PlcSignalDirection.SoftwareToPlc, words: 1, enc: "ASCII"), PointValue.OfString("TOO LONG"), default));
        Assert.Equal(3, server.RequestCount);                                  // refused writes produced no traffic
    }
}
