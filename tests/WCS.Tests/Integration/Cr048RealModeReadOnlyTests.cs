using System.Buffers.Binary;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WCS.Application.Plc;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Infrastructure.Plc.Mc;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-048: with the default addresses in place a real mode READS them and sends no write frame - in REAL_READ_ONLY and in REAL_READ_WRITE with the point declared. The "PLC" is the
/// MC protocol mock on the loopback interface; no device is involved.
/// </summary>
[Collection("Database")]
public class Cr048RealModeReadOnlyTests(DatabaseFixture fixture)
{
    private static readonly McRoute Route = new(0, 255, 1023, 0);

    [Theory]
    [InlineData("RealReadOnly")]
    [InlineData("RealReadWrite")]
    public async Task Default_addresses_are_read_and_nothing_is_ever_written(string mode)
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        plc.SetWord(1500, 1); plc.SetWord(1600, 7);
        await using var h = await Harness.CreateAsync(fixture);
        var cfg = new Dictionary<string, string?>
        {
            ["Plc:Mode"] = mode, ["Plc:AllowLiveConnection"] = "true", ["Plc:ReconnectInitialDelayMs"] = "50", ["Plc:ReconnectMaxDelayMs"] = "200",
            ["Plc:Write:Enabled"] = "true", ["Plc:Write:AllowListText"] = "PLC-01|A1|Mission_Feedback|D1601",
        };
        using var f = new DbApiFactory(fixture).WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(cfg));
            b.ConfigureTestServices(s => { s.RemoveAll<PlcCatalog>(); s.AddSingleton(PlcCatalog.LoadEmbedded()); });
        });
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Add(TestUsers.Make("admin1", UserRole.Admin));
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>().SeedAsync();
        }
        var rt = f.Services.GetRequiredService<IPlcRuntime>();
        await rt.StartAsync();
        var admin = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await admin.LoginAsync("admin1"));
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, "/api/plc/connections/PLC-01", new
        {
            ipAddress = "127.0.0.1", port = plc.Port, protocol = "McProtocol3EBinaryTcp", networkNumber = 0, pcNumber = 255, moduleIoNumber = 1023, plcStationNumber = 0,
            monitoringTimer = 16, timeoutMs = 300, pollingIntervalMs = 50, enabled = true,
        })).Status);

        object? Value(string signal) => rt.Station("A1", true)!.Points.Single(p => p.SignalName == signal).Value;
        for (var i = 0; i < 200 && !Equals(Convert.ToInt32(Value("AGV_TranferCompleted") ?? 0), 7); i++) await Task.Delay(30);
        Assert.Equal(1, Convert.ToInt32(Value("PLC_MissionRequest")));
        Assert.Equal(7, Convert.ToInt32(Value("AGV_RequestTranfer")));       // both signals of the shared register read it
        Assert.Equal(7, Convert.ToInt32(Value("AGV_TranferCompleted")));

        foreach (var signal in new[] { "AGV_RequestTranfer", "AGV_TranferCompleted", "Mission_Feedback" })
        {
            var direct = await rt.WriteAsync("PLC-01", "A1", signal, PointValue.OfInt(3), "test");
            Assert.False(direct.Success);
            var api = await admin.SendJsonAsync(HttpMethod.Post, $"/api/plc/stations/A1/signals/{signal}/test-write", new { value = 3 });
            Assert.DoesNotContain("\"success\":true", api.Raw);
        }
        await Task.Delay(200);

        // every frame the PLC received is a batch read (command 0x0401, at offset 11 of the 3E request); not one write
        Assert.NotEmpty(plc.ReceivedFrames);
        Assert.All(plc.ReceivedFrames, frame => Assert.Equal(McFrame.CommandBatchRead, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(11))));
        Assert.Equal((ushort)7, plc.GetWord(1600));
        Assert.Equal((ushort)0, plc.GetWord(1601));
        await rt.StopAsync();
    }
}
