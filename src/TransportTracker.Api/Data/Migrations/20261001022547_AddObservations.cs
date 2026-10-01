using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TransportTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "observations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    trip_id = table.Column<string>(type: "text", nullable: false),
                    route_id = table.Column<string>(type: "text", nullable: false),
                    line = table.Column<string>(type: "text", nullable: true),
                    direction_id = table.Column<short>(type: "smallint", nullable: true),
                    headsign = table.Column<string>(type: "text", nullable: true),
                    stop_id = table.Column<string>(type: "text", nullable: false),
                    station_id = table.Column<string>(type: "text", nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delay_seconds = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_observations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_observations_service_date_trip_id_station_id_scheduled_at",
                table: "observations",
                columns: new[] { "service_date", "trip_id", "station_id", "scheduled_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_observations_station_id_scheduled_at",
                table: "observations",
                columns: new[] { "station_id", "scheduled_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "observations");
        }
    }
}
