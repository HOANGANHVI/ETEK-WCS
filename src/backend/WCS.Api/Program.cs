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

var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (origins.Length > 0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

var app = builder.Build();

// Fail fast on an invalid flow configuration (e.g. a Bp04 entry).
_ = app.Services.GetRequiredService<IFlowCatalog>();

app.UseExceptionHandler(ExceptionMapping.Configure);
app.UseSerilogRequestLogging();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseCors();

app.MapWcsApi();
if (app.Configuration["Integration:Mode"] is null or "Simulator" && app.Configuration.GetValue("Simulator:ExposeApi", app.Environment.IsDevelopment()))
    app.MapSimulatorApi();
app.MapHub<WcsHub>("/hubs/wcs");

app.Run();

public partial class Program;
