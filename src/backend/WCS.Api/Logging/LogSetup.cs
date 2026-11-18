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
        cfg.ReadFrom.Configuration(configuration).Enrich.FromLogContext().Enrich.WithProperty("Host", Environment.MachineName);

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

    /// <summary>Request log level: handled 4xx stay Information (they are business answers, not failures), 5xx are Error, probes are Debug (not shown).</summary>
    public static LogEventLevel RequestLevel(HttpContext ctx, double elapsedMs, Exception? ex)
    {
        var path = ctx.Request.Path;
        // probes (Docker healthcheck every 15 s) stay out of the log even when the database is down: that state is reported once by the database monitor
        if (path.StartsWithSegments("/healthz") || path.StartsWithSegments("/nginx-health")) return LogEventLevel.Debug;
        if (ex is not null || ctx.Response.StatusCode >= 500) return LogEventLevel.Error;
        return LogEventLevel.Information;
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

        var version = Environment.GetEnvironmentVariable("WCS_BUILD") ?? typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        app.Logger.LogInformation(
            "WCS backend starting: environment {Environment}, version {Version}, PLC mode {PlcMode} (live connection allowed: {AllowLive}), integration {IntegrationMode}, " +
            "cookie secure policy {CookieSecure}, database {Database}, deployment log import {DeploymentLogs}, system log storage {SystemLog}. {EventCode}",
            app.Environment.EnvironmentName, version, c["Plc:Mode"] ?? "Simulator", c.GetValue("Plc:AllowLiveConnection", false), c["Integration:Mode"] ?? "Simulator",
            c["Auth:CookieSecure"] ?? "default", db, string.IsNullOrWhiteSpace(c["DeploymentLogs:Directory"]) ? "off" : "on", c.GetValue("Logging:SystemLog:Enabled", true) ? "on" : "off",
            "STARTUP_SUMMARY");
    }
}
