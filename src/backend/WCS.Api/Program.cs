using System.Text.Json.Serialization;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
using WCS.Api.Api;
using WCS.Api.Logging;
using WCS.Application;
using WCS.Application.Abstractions;
using WCS.Application.Flows;
using WCS.Infrastructure;
using WCS.Runtime;

// CR-007: a plain console logger exists BEFORE the host is built, so a start-up failure (missing configuration, invalid option, ...) is written as a structured
// Fatal event to stdout instead of an anonymous stack trace. The host replaces it with the full pipeline below.
Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console(outputTemplate: LogSetup.ConsoleTemplate).CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Structured logging (Serilog). Never log secrets: connection strings are built in code and not logged; LogSanitizer masks secret-like text again before storage.
    builder.Host.UseSerilog((ctx, services, cfg) => LogSetup.Configure(ctx.Configuration, services, cfg));

    builder.Services.AddOpenApi();
    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    builder.Services.AddWcsApplication(builder.Configuration);
    builder.Services.AddWcsInfrastructure(builder.Configuration);
    builder.Services.AddWcsRuntime(builder.Configuration);
    builder.Services.AddSingleton<IWcsNotifier, SignalRWcsNotifier>(); // replaces the null notifier from Infrastructure
    builder.Services.AddWcsAuth(builder.Configuration, builder.Environment);

    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    {
        if (origins.Length > 0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    }));

    var app = builder.Build();

    // CR-006: Production must not silently run on the PLC simulator (explicit opt-in only). Checked on the final configuration, before anything starts.
    var simulatorInProduction = ProductionSafety.Validate(app.Configuration, app.Environment);

    // Fail fast on an invalid flow configuration (e.g. a Bp04 entry).
    _ = app.Services.GetRequiredService<IFlowCatalog>();

    // CR-006: behind the Nginx container the real client IP/scheme arrive in X-Forwarded-*. They are honoured ONLY from the configured proxy address(es)
    // (Deployment:TrustedProxyNetworks); the backend port is additionally not published, so only that container can reach it.
    if (ForwardedHeadersSetup.Build(app.Configuration) is { } forwarded) app.UseForwardedHeaders(forwarded);

    LogSetup.LogStartupSummary(app);

    if (app.Environment.IsProduction() && string.Equals(app.Configuration["Auth:CookieSecure"], "SameAsRequest", StringComparison.OrdinalIgnoreCase))
        app.Logger.LogWarning("Auth:CookieSecure=SameAsRequest in Production: session cookies are sent over plain HTTP. Acceptable only on a trusted test LAN; use HTTPS with Auth:CookieSecure=Always for operation. {EventCode}", "CONFIG_COOKIE_INSECURE");

    // CR-007: correlation id for every request (also returned to the caller so a screenshot of an error can be matched to the log), then request logging BEFORE
    // the exception handler so the logged status is the final one (a handled 401/409 is no longer reported as 500).
    app.Use(async (ctx, next) =>
    {
        ctx.Response.OnStarting(() => { ctx.Response.Headers["X-Correlation-Id"] = ctx.TraceIdentifier; return Task.CompletedTask; });
        using (LogContext.PushProperty("CorrelationId", ctx.TraceIdentifier)) await next();
    });
    app.UseSerilogRequestLogging(o => o.GetLevel = LogSetup.RequestLevel);
    app.UseExceptionHandler(ExceptionMapping.Configure);
    app.UseCors();
    app.UseAuthentication();
    app.UseWcsRequestGuards();
    app.UseAuthorization();
    if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();

    app.MapWcsApi();
    if (app.Configuration["Integration:Mode"] is null or "Simulator" && app.Configuration.GetValue("Simulator:ExposeApi", app.Environment.IsDevelopment()))
        app.MapSimulatorApi();
    app.MapHub<WcsHub>("/hubs/wcs");

    // CR-006: anonymous liveness/readiness probe for Docker. Reveals no data: only whether the process is up and whether the database answers.
    app.MapGet("/healthz", async (WCS.Infrastructure.Persistence.WcsDbContext db, CancellationToken ct) =>
    {
        bool dbOk;
        try { using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(TimeSpan.FromSeconds(3)); dbOk = await db.Database.CanConnectAsync(cts.Token); }
        catch { dbOk = false; }
        return Results.Json(new { application = "Up", database = dbOk ? "Up" : "Down" }, statusCode: dbOk ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }).AllowAnonymous();

    if (simulatorInProduction)
        app.Logger.LogWarning("Plc:AllowSimulatorInProduction=true: the PLC layer runs on the in-memory SIMULATOR (no real PLC is read) and RCS is a simulator adapter. This is not an operating configuration. {EventCode}", "CONFIG_SIMULATOR_IN_PRODUCTION");

    app.Lifetime.ApplicationStopping.Register(() => app.Logger.LogInformation("Shutdown requested: the WCS backend is stopping. {EventCode}", "SHUTDOWN"));

    app.Run();
}
catch (Exception ex) when (ex is not Microsoft.Extensions.Hosting.HostAbortedException)
{
    // Start-up (or host) failure: structured Fatal to stdout, flushed, then rethrown so the process still exits with an error (behaviour unchanged).
    Log.Fatal(ex, "WCS backend terminated unexpectedly during start-up or run. {EventCode}", "STARTUP_FATAL");
    Log.CloseAndFlush();
    throw;
}

public partial class Program;
