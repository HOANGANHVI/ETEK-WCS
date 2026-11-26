using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Infrastructure.Plc;
using WCS.Tests.Support;

namespace WCS.Tests.Plc;

/// <summary>Reference topology approved in CR-002 (business-rules.md 1.8, integration-requirements.md 5.10).</summary>
public static class Topology
{
    public static readonly (string Plc, string Ip, string[] Stations)[] Expected =
    [
        ("PLC-01", "192.168.10.101", ["A1", "A2", "A3", "A4"]),
        ("PLC-02", "192.168.10.111", ["B1", "B2", "D1", "D2"]),
        ("PLC-03", "192.168.10.121", ["X1", "X2", "X3", "X6"]),
        ("PLC-04", "192.168.10.131", ["E1", "E2", "G3", "G7"]),
    ];

    public static readonly string[] TemplateSignals =
    [
        "Safety_Ready", "Pickup_Request", "Delivery_Request", "Request_ID", "Station_Online", "Station_Ready", "Rack_Present", "Rack_Full", "Rack_Empty",
        "Destination_Available", "Station_Fault", "Transfer_Complete", "Rack_Type", "Heartbeat_SW", "Request_Ack", "AGV_Assigned", "AGV_ID",
        "AGV_Arrived", "Transfer_Permit", "AGV_Departure_Permit", "AGV_Busy", "AGV_Fault", "Error_Code",
    ];
}

public class PlcCatalogTests
{
    private readonly PlcCatalog _catalog = PlcCatalog.LoadEmbedded();

    [Fact]
    public void Catalog_describes_4_PLCs_16_stations_and_the_approved_assignment()
    {
        Assert.Equal(4, _catalog.Plcs.Count);
        Assert.Equal(16, _catalog.Stations.Count);
        foreach (var (plc, ip, stations) in Topology.Expected)
        {
            var p = Assert.Single(_catalog.Plcs, x => x.PlcId == plc);
            Assert.Equal(ip, p.IpAddress);
            Assert.Equal(stations, p.Stations);
            Assert.Equal(stations, _catalog.Stations.Where(s => s.PlcId == plc).OrderBy(s => s.Order).Select(s => s.StationId));
        }
        Assert.Equal(16, _catalog.Stations.Select(s => s.StationId).Distinct().Count());   // a station belongs to exactly one PLC
    }

    [Fact]
    public void The_common_template_has_23_signals_with_the_workbook_names_directions_and_types()
    {
        Assert.Equal(Topology.TemplateSignals, _catalog.Template.Select(t => t.SignalName));
        Assert.Equal(13, _catalog.Template.Count(t => t.Direction == "PlcToSoftware"));
        Assert.Equal(10, _catalog.Template.Count(t => t.Direction == "SoftwareToPlc"));
        Assert.Equal(16, _catalog.Template.Count(t => t.DataType == "Bool"));
        Assert.Equal(6, _catalog.Template.Count(t => t.DataType == "Int"));
        Assert.Equal("String", Assert.Single(_catalog.Template, t => t.SignalName == "Request_ID").DataType);
        // Descriptions are preserved verbatim (including the workbook's truncated Rack_Full text).
        Assert.Equal("Vị trí đang có rack đầy,", _catalog.Template.Single(t => t.SignalName == "Rack_Full").Description);
    }

    [Fact]
    public void The_catalog_carries_no_actual_address_and_no_connection_parameter()
    {
        Assert.Equal(64, _catalog.Source.Sha256.Length);
        var json = System.Text.Json.JsonSerializer.Serialize(_catalog);
        Assert.DoesNotContain("actualAddress", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("port", json, StringComparison.OrdinalIgnoreCase);
        // all handshake rows are still pending: nothing may be derived from them
        Assert.Equal(288, _catalog.Handshake.Count);
        Assert.All(_catalog.Handshake, h => { Assert.Equal("PENDING", h.ApprovalStatus); Assert.Null(h.Trigger); Assert.Null(h.SetCondition); Assert.Null(h.ResetCondition); });
    }
}

public class AddressValidatorTests
{
    private static PointSpec P(string station, string signal, PlcDataType type, string? address, int? words = null) => new(Guid.NewGuid(), station, signal, type, address, words);

    [Theory]
    [InlineData("M9000", true)] [InlineData("d1500", true)] [InlineData("D0", true)] [InlineData("M16777215", true)]
    [InlineData("X10", false)] [InlineData("M", false)] [InlineData("M-1", false)] [InlineData("D12A", false)] [InlineData("M16777216", false)] [InlineData("", false)] [InlineData(null, false)]
    public void Device_addresses_are_parsed_strictly(string? text, bool ok) => Assert.Equal(ok, DeviceAddress.TryParse(text, out _));

    [Fact]
    public void Types_must_match_the_device()
    {
        Assert.Equal("TYPE_DEVICE", AddressValidator.ValidateOne(P("A1", "Safety_Ready", PlcDataType.Bool, "D100"))!.Code);
        Assert.Equal("TYPE_DEVICE", AddressValidator.ValidateOne(P("A1", "Rack_Full", PlcDataType.Int, "M100"))!.Code);
        Assert.Null(AddressValidator.ValidateOne(P("A1", "Safety_Ready", PlcDataType.Bool, "M100")));
        Assert.Null(AddressValidator.ValidateOne(P("A1", "Rack_Full", PlcDataType.Int, "D100")));
        Assert.Equal("STRING_LENGTH", AddressValidator.ValidateOne(P("A1", "Request_ID", PlcDataType.String, "D100"))!.Code);   // length not confirmed
        Assert.Null(AddressValidator.ValidateOne(P("A1", "Request_ID", PlcDataType.String, "D100", 10)));
    }

    [Fact]
    public void Two_stations_on_the_same_PLC_cannot_share_or_overlap_addresses()
    {
        var points = new[]
        {
            P("A1", "Pickup_Request", PlcDataType.Bool, "M100"),
            P("A2", "Pickup_Request", PlcDataType.Bool, "M100"),            // same bit, different station -> conflict
            P("A1", "Request_ID", PlcDataType.String, "D1500", 10),         // D1500..D1509
            P("A2", "Request_ID", PlcDataType.String, "D1505", 10),         // overlaps
            P("A3", "Rack_Full", PlcDataType.Int, "D1509"),                 // inside A1's string
            P("A4", "Rack_Full", PlcDataType.Int, "D1520"),                 // free (A2 string ends at D1514)
        };
        var issues = AddressValidator.Validate(points);
        Assert.Equal(4, issues.Count);   // M100 twice; the two strings; D1509 against both strings
        Assert.All(issues, i => Assert.Equal("ADDRESS_CONFLICT", i.Code));
        Assert.DoesNotContain(issues, i => i.StationId == "A4");
    }

    [Fact]
    public void M_bits_and_D_words_are_separate_namespaces_and_distinct_addresses_are_fine()
    {
        Assert.Empty(AddressValidator.Validate([P("A1", "Safety_Ready", PlcDataType.Bool, "M100"), P("A1", "Rack_Full", PlcDataType.Int, "D100"),
            P("A2", "Safety_Ready", PlcDataType.Bool, "M101"), P("A2", "Rack_Full", PlcDataType.Int, "D101")]));
    }

    [Fact]
    public void Points_without_an_address_are_ignored()
    {
        Assert.Empty(AddressValidator.Validate([P("A1", "Safety_Ready", PlcDataType.Bool, null), P("A2", "Safety_Ready", PlcDataType.Bool, null)]));
    }
}

public class WritePolicyTests
{
    private static IoPointRef Pt(string signal, string group, PlcSignalDirection dir, bool allowed, string? address = "M1") =>
        new(Guid.NewGuid(), "PLC-01", "A1", signal, dir, PlcDataType.Bool, address, null, null, allowed, group);

    [Fact]
    public void Safety_and_permit_signals_are_always_blocked()
    {
        var o = new PlcRuntimeOptions { Mode = PlcRuntimeMode.Simulator };
        foreach (var s in new[] { "Transfer_Permit", "AGV_Departure_Permit", "Safety_Ready" })
            Assert.Equal("WRITE_BLOCKED_SAFETY", WritePolicy.Evaluate(o, Pt(s, "Command", PlcSignalDirection.SoftwareToPlc, true)).Code);
        Assert.Equal("WRITE_BLOCKED_SAFETY", WritePolicy.Evaluate(o, Pt("Anything", "Safety / Interlock", PlcSignalDirection.SoftwareToPlc, true)).Code);
    }

    [Fact]
    public void Read_only_mode_never_allows_a_write_and_read_write_needs_every_key()
    {
        var p = Pt("Request_Ack", "Command", PlcSignalDirection.SoftwareToPlc, true);
        Assert.Equal("READ_ONLY", WritePolicy.Evaluate(new PlcRuntimeOptions { Mode = PlcRuntimeMode.RealReadOnly }, p).Code);

        var rw = new PlcRuntimeOptions { Mode = PlcRuntimeMode.RealReadWrite, AllowLiveConnection = true };
        Assert.Equal(PlcRuntimeMode.RealReadOnly, WritePolicy.EffectiveMode(rw));                      // keys missing: degrades, never upgrades
        Assert.Equal("READ_ONLY", WritePolicy.Evaluate(rw, p).Code);
        rw.Write.Enabled = true;
        rw.Declare(p);   // CR-044: a real write is declared by the deployment
        Assert.Equal(PlcRuntimeMode.RealReadWrite, WritePolicy.EffectiveMode(rw));
        Assert.True(WritePolicy.Evaluate(rw, p).Allowed);
        Assert.Equal("WRITE_NOT_ALLOWED", WritePolicy.Evaluate(rw, Pt("Request_Ack", "Command", PlcSignalDirection.SoftwareToPlc, false)).Code);
        Assert.Equal("NO_ADDRESS", WritePolicy.Evaluate(rw, Pt("Request_Ack", "Command", PlcSignalDirection.SoftwareToPlc, true, null)).Code);
        Assert.Equal("WRITE_DIRECTION", WritePolicy.Evaluate(rw, Pt("Pickup_Request", "Request", PlcSignalDirection.PlcToSoftware, true)).Code);
        rw.AllowLiveConnection = false;
        Assert.Equal(PlcRuntimeMode.RealReadOnly, WritePolicy.EffectiveMode(rw));
    }
}
