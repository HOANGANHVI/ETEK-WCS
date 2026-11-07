using System.Text.Json.Serialization;
using Serilog;
using WCS.Api.Api;
using WCS.Application;
using WCS.Application.Abstractions;
using WCS.Application.Flows;
using WCS.Infrastructure;
using WCS.Runtime;

var builder = WebApplication.CreateBuilder(args);

// Structured logging (Serilog). Never log secrets: connection strings are built in code and not logged.
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext());

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

// CR-006: behind the Nginx container the real client IP/scheme arrive in X-Forwarded-*. Trusted ONLY when Deployment:TrustForwardedHeaders=true, which is
// safe solely because the backend port is not published (only the Nginx container can reach it).
if (app.Configuration.GetValue("Deployment:TrustForwardedHeaders", false))
{
    var fh = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions { ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto, ForwardLimit = 1 };
    fh.KnownIPNetworks.Clear(); fh.KnownProxies.Clear();
    app.UseForwardedHeaders(fh);
}

app.UseExceptionHandler(ExceptionMapping.Configure);
app.UseSerilogRequestLogging();
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
    app.Logger.LogWarning("Plc:AllowSimulatorInProduction=true: the PLC layer runs on the in-memory SIMULATOR (no real PLC is read) and RCS is a simulator adapter. This is not an operating configuration.");

app.Run();

public partial class Program;
