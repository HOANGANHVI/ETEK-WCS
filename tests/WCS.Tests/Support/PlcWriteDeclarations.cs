using WCS.Application.Plc;

namespace WCS.Tests.Support;

/// <summary>CR-044: declares the points a test writes in a real mode (always against the mock PLC), the way a deployment declares them.</summary>
public static class PlcWriteDeclarations
{
    public static Dictionary<string, string?> Declare(this Dictionary<string, string?> config, params (string Plc, string Station, string Signal, string Address)[] points)
    {
        for (var i = 0; i < points.Length; i++)
        {
            var key = $"Plc:Write:AllowList:{i}:";
            config[key + "PlcId"] = points[i].Plc; config[key + "StationId"] = points[i].Station; config[key + "Signal"] = points[i].Signal; config[key + "Address"] = points[i].Address;
        }
        return config;
    }

    public static PlcRuntimeOptions Declare(this PlcRuntimeOptions options, params IoPointRef[] points)
    {
        options.Write.AllowList.AddRange(points.Select(p => new PlcWriteDeclaration { PlcId = p.PlcId, StationId = p.StationId, Signal = p.SignalName, Address = p.Address!, Words = p.WordLength }));
        return options;
    }
}
