using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace WCS.Api.Api;

/// <summary>
/// CR-006: trust of X-Forwarded-For / X-Forwarded-Proto. Only a sender whose address is inside <c>Deployment:TrustedProxyNetworks</c> (CIDR list, in the
/// compose stack the single Nginx address /32) may set them; from anyone else the headers are ignored. Enabling
/// <c>Deployment:TrustForwardedHeaders</c> without a valid list refuses to start (there is deliberately no "trust everybody" mode).
/// </summary>
public static class ForwardedHeadersSetup
{
    public static ForwardedHeadersOptions? Build(IConfiguration config)
    {
        if (!config.GetValue("Deployment:TrustForwardedHeaders", false)) return null;
        var entries = config.GetSection("Deployment:TrustedProxyNetworks").Get<string[]>()?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray() ?? [];
        if (entries.Length == 0)
            throw new InvalidOperationException("Deployment:TrustForwardedHeaders=true requires Deployment:TrustedProxyNetworks (CIDR list of the reverse proxy, e.g. 172.30.0.10/32).");

        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, ForwardLimit = 1 };
        options.KnownProxies.Clear(); options.KnownIPNetworks.Clear();
        foreach (var e in entries)
        {
            if (e.Contains('/'))
            {
                if (!System.Net.IPNetwork.TryParse(e, out var net)) throw new InvalidOperationException($"Deployment:TrustedProxyNetworks entry '{e}' is not a valid CIDR.");
                if (net.BaseAddress.Equals(IPAddress.Any) && net.PrefixLength == 0 || net.BaseAddress.Equals(IPAddress.IPv6Any) && net.PrefixLength == 0)
                    throw new InvalidOperationException("Deployment:TrustedProxyNetworks must not be 0.0.0.0/0 or ::/0 (that would trust any sender).");
                options.KnownIPNetworks.Add(net);
            }
            else
            {
                if (!IPAddress.TryParse(e, out var ip)) throw new InvalidOperationException($"Deployment:TrustedProxyNetworks entry '{e}' is not a valid IP address.");
                options.KnownProxies.Add(ip);
            }
        }
        return options;
    }
}
