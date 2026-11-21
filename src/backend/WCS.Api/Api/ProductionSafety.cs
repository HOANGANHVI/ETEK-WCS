namespace WCS.Api.Api;

/// <summary>
/// CR-006 start-up guard. In the Production environment the PLC layer must not silently run on the in-memory simulator:
/// start-up is refused unless <c>Plc:AllowSimulatorInProduction=true</c> is set explicitly (never defaulted). Even with that exception the PLC
/// handshake/integration stays off, so no automatic write path exists. RCS currently has only a simulator adapter (project baseline); this is documented
/// and shown in the UI.
/// </summary>
public static class ProductionSafety
{
    public const string AllowKey = "Plc:AllowSimulatorInProduction";

    public const string FleetAllowKey = "Rcs:Fleet:AllowSimulatorInProduction";

    /// <summary>
    /// CR-031: in Production the AMR list must come from the REAL RCS (Rcs:Fleet:Source=Real). A simulated fleet is refused at start unless <c>Rcs:Fleet:AllowSimulatorInProduction=true</c> is set explicitly
    /// (never defaulted). Returns true when that explicit exception is active.
    /// </summary>
    public static bool ValidateFleet(IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsProduction()) return false;
        if (string.Equals(config["Rcs:Fleet:Source"], "Real", StringComparison.OrdinalIgnoreCase)) return false;
        if (!config.GetValue(FleetAllowKey, false))
            throw new InvalidOperationException("Production refuses to start with a simulated AMR fleet (Rcs:Fleet:Source is not 'Real'). Set Rcs:Fleet:Source=Real (RCS_FLEET_SOURCE=Real) and save the RCS connection, " +
                $"or - only for a deliberate, supervised start without an RCS - set {FleetAllowKey}=true.");
        return true;
    }

    /// <summary>Throws when the configuration is unsafe for Production. Returns true when the explicit simulator exception is active (caller logs a warning).</summary>
    public static bool Validate(IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsProduction()) return false;
        var mode = config["Plc:Mode"];
        var simulator = string.IsNullOrWhiteSpace(mode) || mode.Trim().Equals("Simulator", StringComparison.OrdinalIgnoreCase);
        if (!simulator) return false;
        if (!config.GetValue(AllowKey, false))
            throw new InvalidOperationException(
                "Production refuses to start with Plc:Mode=Simulator. Configure a real PLC mode (RealReadOnly first) once the PLC engineer has confirmed the " +
                $"parameters, or - only for a deliberate, supervised start without a PLC - set {AllowKey}=true.");
        if (config.GetValue("Plc:Integration:Enabled", false))
            throw new InvalidOperationException("Plc:Integration:Enabled=true is not allowed together with the simulator in Production (no automatic handshake on simulated data).");
        return true;
    }
}
