using Microsoft.EntityFrameworkCore;
using TransportTracker.Api.Timetable;
using Route = TransportTracker.Api.Timetable.Route;

namespace TransportTracker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TimetableImport> TimetableImports => Set<TimetableImport>();
    public DbSet<Stop> Stops => Set<Stop>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<ServiceCalendar> ServiceCalendars => Set<ServiceCalendar>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<StopTime> StopTimes => Set<StopTime>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Table names are fixed explicitly because the importer writes to them with raw COPY.
        b.Entity<TimetableImport>(e =>
        {
            e.ToTable("timetable_imports");
            e.HasIndex(x => x.IsActive).IsUnique().HasFilter("is_active");
        });

        b.Entity<Stop>(e =>
        {
            e.ToTable("stops");
            e.HasKey(x => new { x.ImportId, x.StopId });
            e.HasIndex(x => new { x.ImportId, x.ParentStation });
        });

        b.Entity<Route>(e =>
        {
            e.ToTable("routes");
            e.HasKey(x => new { x.ImportId, x.RouteId });
        });

        b.Entity<ServiceCalendar>(e =>
        {
            e.ToTable("service_calendars");
            e.HasKey(x => new { x.ImportId, x.ServiceId });
        });

        b.Entity<Trip>(e =>
        {
            e.ToTable("trips");
            e.HasKey(x => new { x.ImportId, x.TripId });
        });

        b.Entity<StopTime>(e =>
        {
            e.ToTable("stop_times");
            e.HasKey(x => new { x.ImportId, x.TripId, x.StopSequence });
            e.HasIndex(x => new { x.ImportId, x.StopId });
        });
    }
}
