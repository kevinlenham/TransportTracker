using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransportTracker.Api.Data;
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

builder.Services.AddScoped<IGtfsSource, TfnswGtfsSource>();
builder.Services.AddScoped<TimetableImporter>();
builder.Services.AddHostedService<TimetableImportService>();

var app = builder.Build();

// Single instance, so applying migrations on startup is safe.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

var v1 = app.MapGroup("/v1");

v1.MapGet("/health", async (AppDbContext db) =>
{
    var timetable = await db.TimetableImports.AsNoTracking()
        .Where(i => i.IsActive)
        .Select(i => new { i.Id, i.ImportedAt, i.SourceLastModified, i.StopTimeCount })
        .SingleOrDefaultAsync();
    return Results.Ok(new { status = "ok", timetable });
});

app.Run();
