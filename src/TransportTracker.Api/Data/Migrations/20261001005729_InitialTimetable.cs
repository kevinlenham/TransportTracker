using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TransportTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialTimetable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "routes",
                columns: table => new
                {
                    import_id = table.Column<int>(type: "integer", nullable: false),
                    route_id = table.Column<string>(type: "text", nullable: false),
                    agency_id = table.Column<string>(type: "text", nullable: true),
                    short_name = table.Column<string>(type: "text", nullable: true),
                    long_name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    color = table.Column<string>(type: "text", nullable: true),
                    text_color = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routes", x => new { x.import_id, x.route_id });
                });

            migrationBuilder.CreateTable(
                name: "service_calendars",
                columns: table => new
                {
                    import_id = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<string>(type: "text", nullable: false),
                    monday = table.Column<bool>(type: "boolean", nullable: false),
                    tuesday = table.Column<bool>(type: "boolean", nullable: false),
                    wednesday = table.Column<bool>(type: "boolean", nullable: false),
                    thursday = table.Column<bool>(type: "boolean", nullable: false),
                    friday = table.Column<bool>(type: "boolean", nullable: false),
                    saturday = table.Column<bool>(type: "boolean", nullable: false),
                    sunday = table.Column<bool>(type: "boolean", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_calendars", x => new { x.import_id, x.service_id });
                });

            migrationBuilder.CreateTable(
                name: "stop_times",
                columns: table => new
                {
                    import_id = table.Column<int>(type: "integer", nullable: false),
                    trip_id = table.Column<string>(type: "text", nullable: false),
                    stop_sequence = table.Column<int>(type: "integer", nullable: false),
                    stop_id = table.Column<string>(type: "text", nullable: false),
                    arrival_seconds = table.Column<int>(type: "integer", nullable: true),
                    departure_seconds = table.Column<int>(type: "integer", nullable: true),
                    pickup_type = table.Column<short>(type: "smallint", nullable: false),
                    drop_off_type = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stop_times", x => new { x.import_id, x.trip_id, x.stop_sequence });
                });

            migrationBuilder.CreateTable(
                name: "stops",
                columns: table => new
                {
                    import_id = table.Column<int>(type: "integer", nullable: false),
                    stop_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lon = table.Column<double>(type: "double precision", nullable: false),
                    location_type = table.Column<short>(type: "smallint", nullable: false),
                    parent_station = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stops", x => new { x.import_id, x.stop_id });
                });

            migrationBuilder.CreateTable(
                name: "timetable_imports",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    source_last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    stop_time_count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timetable_imports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trips",
                columns: table => new
                {
                    import_id = table.Column<int>(type: "integer", nullable: false),
                    trip_id = table.Column<string>(type: "text", nullable: false),
                    route_id = table.Column<string>(type: "text", nullable: false),
                    service_id = table.Column<string>(type: "text", nullable: false),
                    headsign = table.Column<string>(type: "text", nullable: true),
                    direction_id = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trips", x => new { x.import_id, x.trip_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_stop_times_import_id_stop_id",
                table: "stop_times",
                columns: new[] { "import_id", "stop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stops_import_id_parent_station",
                table: "stops",
                columns: new[] { "import_id", "parent_station" });

            migrationBuilder.CreateIndex(
                name: "ix_timetable_imports_is_active",
                table: "timetable_imports",
                column: "is_active",
                unique: true,
                filter: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "routes");

            migrationBuilder.DropTable(
                name: "service_calendars");

            migrationBuilder.DropTable(
                name: "stop_times");

            migrationBuilder.DropTable(
                name: "stops");

            migrationBuilder.DropTable(
                name: "timetable_imports");

            migrationBuilder.DropTable(
                name: "trips");
        }
    }
}
