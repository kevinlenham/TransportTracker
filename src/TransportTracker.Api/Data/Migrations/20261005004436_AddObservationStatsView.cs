using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportTracker.Api.Data.Migrations
{
    /// <summary>
    /// The one place the stats rules are written down in SQL: each Observation's Time Band, and whether it
    /// counts as Observed, Late or Cancelled. Station and Line stats both aggregate over this view.
    /// The Time Band CASE must match TimeBands.Of, and 120 must match LateStatsQuery.LateAfterSeconds.
    /// </summary>
    public partial class AddObservationStatsView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIEW observation_stats AS
                SELECT o.station_id, o.line, o.direction_id, o.headsign, o.scheduled_at,
                    CASE
                        WHEN extract(isodow FROM o.local) >= 6 THEN 'Weekend'
                        WHEN o.local::time >= '06:30' AND o.local::time < '09:30' THEN 'AmPeak'
                        WHEN o.local::time >= '15:00' AND o.local::time < '19:00' THEN 'PmPeak'
                        ELSE 'OffPeak'
                    END AS time_band,
                    o.status = 'Passed' AS observed,
                    o.status = 'Passed' AND o.delay_seconds > 120 AS late,
                    o.status <> 'Passed' AS cancelled
                FROM (SELECT *, scheduled_at AT TIME ZONE 'Australia/Sydney' AS local FROM observations) o;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW observation_stats;");
        }
    }
}
