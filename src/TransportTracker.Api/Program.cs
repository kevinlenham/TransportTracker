using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransportTracker.Api.Alerts;
using TransportTracker.Api.Data;
using TransportTracker.Api.Lines;
using TransportTracker.Api.Poller;
using TransportTracker.Api.Stations;
using TransportTracker.Api.Stats;
using TransportTracker.Api.Tfnsw;
using TransportTracker.Api.Timetable;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<AppDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))
    .UseSnakeCaseNamingConvention());

builder.Services.Configure<TfnswOptions>(builder.Configuration.GetSection(TfnswOptions.SectionName));
builder.Services.AddHttpClient(TfnswOptions.HttpClientName, (sp, http) =>
{
    var options = sp.GetRequiredService<IOptions<TfnswOptions>>().Value;
    http.BaseAddress = new Uri(options.BaseUrl);
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("apikey", options.ApiKey);
    http.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddHttpClient(TfnswOptions.RealtimeHttpClientName, (sp, http) =>
{
    var options = sp.GetRequiredService<IOptions<TfnswOptions>>().Value;
    http.BaseAddress = new Uri(options.BaseUrl);
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("apikey", options.ApiKey);
    http.Timeout = TimeSpan.FromSeconds(20);
})
    // Connections idle between 30s polls can be silently dropped by the network, and a request sent
    // on one hangs until the timeout. Don't reuse them.
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15) });

builder.Services.AddScoped<IGtfsSource, TfnswGtfsSource>();
builder.Services.AddScoped<TimetableImporter>();
builder.Services.AddHostedService<TimetableImportService>();

builder.Services.AddSingleton<IRealtimeFeed, TfnswRealtimeFeed>();
builder.Services.AddSingleton<PollerStatus>();
builder.Services.AddScoped<ObservationRecorder>();
builder.Services.AddHostedService<PollerService>();

builder.Services.AddSingleton<IAlertsFeed, TfnswAlertsFeed>();
builder.Services.AddSingleton<AlertStore>();
builder.Services.AddHostedService<AlertService>();

builder.Services.AddScoped<LateStatsQuery>();
builder.Services.AddScoped<DepartureBoard>();
builder.Services.AddScoped<LineReports>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// Single instance, so applying migrations on startup is safe.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

var v1 = app.MapGroup("/v1");

// Returns 503 when Observations aren't being recorded (no Timetable, or no successful poll lately),
// so the App Service health check alert fires before a silent gap skews the stats.
v1.MapGet("/health", async (AppDbContext db, PollerStatus poller, TimeProvider time) =>
{
    var timetable = await db.TimetableImports.AsNoTracking()
        .Where(i => i.IsActive)
        .Select(i => new { i.Id, i.ImportedAt, i.SourceLastModified, i.StopTimeCount })
        .SingleOrDefaultAsync();
    var healthy = timetable is not null && poller.IsHealthy(time.GetUtcNow());
    var body = new { status = healthy ? "ok" : "unhealthy", timetable, poller = new { poller.LastPollAt } };
    return healthy ? Results.Ok(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
});

v1.MapStations();
v1.MapLines();
v1.MapAlerts();

app.Run();
