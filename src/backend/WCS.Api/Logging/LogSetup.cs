using Npgsql;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace WCS.Api.Logging;

/// <summary>CR-007: Serilog pipeline composition (stdout + throttle + optional file), start-up summary and request-log levels. Pure configuration of logging.</summary>
public static class LogSetup
{
    public const string ConsoleTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public static void Configure(IConfiguration configuration, IServiceProvider services, LoggerConfiguration cfg)
    {
        cfg.ReadFrom.Configuration(configuration).Enrich.FromLogContext().Enrich.With<DiagnosticContextEnricher>().Enrich.WithProperty("Host", Environment.MachineName);

        var sinks = new List<ILogEventSink>
        {
            new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Console(outputTemplate: ConsoleTemplate).CreateLogger(),
        };

        // optional local file as a second fallback next to stdout (Logging:File:Path), daily roll, 14 files, never required
        var filePath = configuration["Logging:File:Path"];
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            try
            {
                sinks.Add(new LoggerConfiguration().MinimumLevel.Verbose()
                    .WriteTo.File(filePath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, shared: true, outputTemplate: ConsoleTemplate).CreateLogger());
            }
            catch (Exception ex) { Console.Error.WriteLine($"Logging:File:Path '{filePath}' could not be opened: {ex.Message}"); }
        }

        // CR-007: searchable copy of SYSTEM / DATABASE / RCS_AGV events (best effort; stdout above is always written)
        if (configuration.GetValue("Logging:SystemLog:Enabled", true)) sinks.Add(new SystemLogSink(services));

        var window = TimeSpan.FromSeconds(Math.Max(0, configuration.GetValue("Logging:ThrottleSeconds", 60)));
        cfg.WriteTo.Sink(new ThrottlingSink(new CompositeSink(sinks), window));
    }

    /// <summary>CR-021: a request at least this slow is stored as a warning (Diagnostics:SlowRequestMs, default 1000).</summary>
    public static int SlowRequestMs { get; set; } = 1000;
    public const string RequestStartKey = "wcs.request.start";

    /// <summary>
    /// Request log level. Probes are Debug (not shown). 5xx and exceptions are Error; 401/403 and requests at or above <see cref="SlowRequestMs"/> are Warning (so they are stored and searchable);
    /// every other handled answer (including 400/404/409/422, which are business answers) stays Information and is not stored.
    /// </summary>
    public static LogEventLevel RequestLevel(HttpContext ctx, double elapsedMs, Exception? ex)
    {
        var path = ctx.Request.Path;
        // probes (Docker healthcheck every 15 s) stay out of the log even when the database is down: that state is reported once by the database monitor
        if (path.StartsWithSegments("/healthz") || path.StartsWithSegments("/nginx-health")) return LogEventLevel.Debug;
        if (ex is not null || ctx.Response.StatusCode >= 500) return LogEventLevel.Error;
        if (ctx.Response.StatusCode is 401 or 403 || (SlowRequestMs > 0 && elapsedMs >= SlowRequestMs)) return LogEventLevel.Warning;
        return LogEventLevel.Information;
    }

    /// <summary>CR-021: tags the request event that is stored (5xx / 401 / 403 / slow) with a stable event code and the signed-in user name; never a header, cookie, token or query string.</summary>
    public static void EnrichRequest(Serilog.IDiagnosticContext diag, HttpContext ctx)
    {
        var path = ctx.Request.Path;
        if (path.StartsWithSegments("/healthz") || path.StartsWithSegments("/nginx-health")) return;
        var ms = ctx.Items.TryGetValue(RequestStartKey, out var t) && t is long ts ? System.Diagnostics.Stopwatch.GetElapsedTime(ts).TotalMilliseconds : 0;
        var status = ctx.Response.StatusCode;
        var code = status >= 500 ? "API_REQUEST_FAILED" : status is 401 or 403 ? "API_ACCESS_DENIED" : SlowRequestMs > 0 && ms >= SlowRequestMs ? "API_REQUEST_SLOW" : null;
        if (code is null) return;
        diag.Set("EventCode", code);
        if (ctx.User.Identity is { IsAuthenticated: true, Name: { } name }) diag.Set("User", name);
    }

    /// <summary>One structured line that tells an engineer how this instance was started. Never contains a password, token or connection string.</summary>
    public static void LogStartupSummary(WebApplication app)
    {
        var c = app.Configuration;
        string db;
        try
        {
            var csb = new NpgsqlConnectionStringBuilder(WCS.Infrastructure.DependencyInjection.BuildConnectionString(c));
            db = $"{csb.Host}:{csb.Port}/{csb.Database}";
        }
        catch (Exception) { db = "not configured"; }

        // CR-021: configuration that is missing or unsafe for the environment is a diagnostic event of its own (the values themselves are never logged)
        if (app.Environment.IsProduction() && !c.GetValue("Auth:CookieSecure", true)) app.Logger.LogWarning("Auth cookies are not marked Secure in Production (Auth:CookieSecure=false). {EventCode}", "STARTUP_CONFIG_WARNING");
        if (app.Environment.IsProduction() && string.Equals(c["Plc:Mode"] ?? "Simulator", "Simulator", StringComparison.OrdinalIgnoreCase) && !c.GetValue("Plc:AllowSimulatorInProduction", false))
            app.Logger.LogWarning("Plc:Mode is Simulator in Production without Plc:AllowSimulatorInProduction: the PLC runtime will refuse to start. {EventCode}", "STARTUP_CONFIG_WARNING");
        if (!string.IsNullOrWhiteSpace(c["DeploymentLogs:Directory"]) && !Directory.Exists(c["DeploymentLogs:Directory"]))
            app.Logger.LogWarning("DeploymentLogs:Directory is set but the folder does not exist: deployment events cannot be imported. {EventCode}", "STARTUP_CONFIG_WARNING");

        var version = Environment.GetEnvironmentVariable("WCS_BUILD") ?? typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        app.Logger.LogInformation(
            "WCS backend starting: environment {Environment}, version {Version}, PLC mode {PlcMode} (live connection allowed: {AllowLive}), integration {IntegrationMode}, " +
            "cookie secure policy {CookieSecure}, database {Database}, deployment log import {DeploymentLogs}, system log storage {SystemLog}. {EventCode}",
            app.Environment.EnvironmentName, version, c["Plc:Mode"] ?? "Simulator", c.GetValue("Plc:AllowLiveConnection", false), c["Integration:Mode"] ?? "Simulator",
            c["Auth:CookieSecure"] ?? "default", db, string.IsNullOrWhiteSpace(c["DeploymentLogs:Directory"]) ? "off" : "on", c.GetValue("Logging:SystemLog:Enabled", true) ? "on" : "off",
            "STARTUP_SUMMARY");
    }
}
