using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.DataProtection;
using WCS.Application.Config;
using WCS.Domain.Common;
using WCS.Infrastructure.Config;

namespace WCS.Tests.Config;

public class ProbeAndProtectorTests
{
    [Fact]
    public async Task Simulated_probe_never_opens_a_connection()
    {
        // Port 1 on a documentation address: if this tried the network it would fail or hang. It must just report "simulated".
        var r = await new SimulatedConnectionProbe().ProbeAsync(ConnectionKind.Plc, "192.0.2.1", 1, TimeSpan.FromSeconds(30));
        Assert.Equal(ConnectionTestOutcome.Simulated, r.Outcome);
        Assert.Contains("MÔ PHỎNG", r.Message);
        Assert.Null(r.LatencyMs);
    }

    [Fact]
    public async Task Tcp_probe_reports_success_for_an_open_loopback_port_and_sends_nothing()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accept = listener.AcceptTcpClientAsync();
            var r = await new TcpReachabilityProbe().ProbeAsync(ConnectionKind.Rcs, "127.0.0.1", port, TimeSpan.FromSeconds(3));
            Assert.Equal(ConnectionTestOutcome.Success, r.Outcome);
            Assert.Contains("Chưa gọi API RCS", r.Message);

            using var server = await accept;
            server.ReceiveTimeout = 300;
            Assert.Equal(0, server.Available); // not a single byte was sent
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Tcp_probe_reports_failure_for_a_closed_loopback_port()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // now nothing listens there

        var r = await new TcpReachabilityProbe().ProbeAsync(ConnectionKind.Plc, "127.0.0.1", port, TimeSpan.FromSeconds(3));
        Assert.Equal(ConnectionTestOutcome.Failed, r.Outcome);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Data_protection_protector_round_trips_and_hides_the_plaintext()
    {
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var blob = protector.Protect("my-password-123");
        Assert.DoesNotContain("my-password-123", blob);
        Assert.Equal("my-password-123", protector.Unprotect(blob));
        Assert.NotEqual(blob, protector.Protect("my-password-123")); // randomised
    }

    [Fact]
    public void A_blob_from_another_key_ring_cannot_be_read()
    {
        var blob = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider()).Protect("x");
        var other = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        Assert.ThrowsAny<Exception>(() => other.Unprotect(blob));
    }
}
