using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using TransportTracker.Api.Data;

namespace TransportTracker.Api.Timetable;

public enum ImportOutcome { Imported, Unchanged }

public record ImportResult(ImportOutcome Outcome, int ImportId, long StopTimeCount);

/// <summary>
/// Loads the static GTFS Timetable into Postgres. The new Timetable is copied in alongside the
/// active one and swapped in a single transaction, so readers never see a half-loaded Timetable.
/// If the feed content hasn't changed since the active import, nothing is loaded.
/// </summary>
public class TimetableImporter(AppDbContext db, IGtfsSource source, TimeProvider time, ILogger<TimetableImporter> logger)
{
    // Only the files V1 uses. shapes.txt and occupancies.txt are large and not needed yet.
    private static readonly string[] ImportedFiles = ["stops.txt", "routes.txt", "calendar.txt", "trips.txt", "stop_times.txt"];

    public async Task<ImportResult> ImportAsync(CancellationToken ct)
    {
        await using var download = await source.DownloadAsync(ct);
        using var zip = new ZipArchive(download.Zip, ZipArchiveMode.Read);

        var hash = await HashContentAsync(zip, ct);
        var active = await db.TimetableImports.AsNoTracking().SingleOrDefaultAsync(i => i.IsActive, ct);
        if (active?.ContentHash == hash)
        {
            logger.LogInformation("Timetable unchanged (import {ImportId}), skipping", active.Id);
            return new ImportResult(ImportOutcome.Unchanged, active.Id, active.StopTimeCount);
        }

        var import = new TimetableImport
        {
            ContentHash = hash,
            SourceLastModified = download.LastModified,
            ImportedAt = time.GetUtcNow(),
        };
        db.TimetableImports.Add(import);
        await db.SaveChangesAsync(ct);
        // The row is activated with raw SQL below, so don't let the context keep a stale copy.
        db.Entry(import).State = EntityState.Detached;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            var id = import.Id;

            await CopyAsync(conn, zip, "stops.txt",
                "COPY stops (import_id, stop_id, name, lat, lon, location_type, parent_station) FROM STDIN (FORMAT BINARY)",
                async (csv, w) =>
                {
                    await w.WriteAsync(id, NpgsqlDbType.Integer, ct);
                    await w.WriteAsync(Required(csv, "stop_id"), NpgsqlDbType.Text, ct);
                    await w.WriteAsync(Required(csv, "stop_name"), NpgsqlDbType.Text, ct);
                    await w.WriteAsync(double.Parse(Required(csv, "stop_lat"), CultureInfo.InvariantCulture), NpgsqlDbType.Double, ct);
                    await w.WriteAsync(double.Parse(Required(csv, "stop_lon"), CultureInfo.InvariantCulture), NpgsqlDbType.Double, ct);
                    await w.WriteAsync(ShortOrZero(csv, "location_type"), NpgsqlDbType.Smallint, ct);
                    await WriteNullableAsync(w, Optional(csv, "parent_station"), NpgsqlDbType.Text, ct);
                }, ct);

            await CopyAsync(conn, zip, "routes.txt",
                "COPY routes (import_id, route_id, agency_id, short_name, long_name, description, color, text_color) FROM STDIN (FORMAT BINARY)",
                async (csv, w) =>
                {
                    await w.WriteAsync(id, NpgsqlDbType.Integer, ct);
                    await w.WriteAsync(Required(csv, "route_id"), NpgsqlDbType.Text, ct);
                    await WriteNullableAsync(w, Optional(csv, "agency_id"), NpgsqlDbType.Text, ct);
                    await WriteNullableAsync(w, Optional(csv, "route_short_name"), NpgsqlDbType.Text, ct);
                    await WriteNullableAsync(w, Optional(csv, "route_long_name"), NpgsqlDbType.Text, ct);
                    await WriteNullableAsync(w, Optional(csv, "route_desc"), NpgsqlDbType.Text, ct);
                    await WriteNullableAsync(w, Optional(csv, "route_color"), NpgsqlDbType.Text, ct);
                    await WriteNullableAsync(w, Optional(csv, "route_text_color"), NpgsqlDbType.Text, ct);
                }, ct);

            await CopyAsync(conn, zip, "calendar.txt",
                "COPY service_calendars (import_id, service_id, monday, tuesday, wednesday, thursday, friday, saturday, sunday, start_date, end_date) FROM STDIN (FORMAT BINARY)",
                async (csv, w) =>
                {
                    await w.WriteAsync(id, NpgsqlDbType.Integer, ct);
                    await w.WriteAsync(Required(csv, "service_id"), NpgsqlDbType.Text, ct);
                    foreach (var day in new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" })
                        await w.WriteAsync(Required(csv, day) == "1", NpgsqlDbType.Boolean, ct);
                    await w.WriteAsync(ParseDate(Required(csv, "start_date")), NpgsqlDbType.Date, ct);
                    await w.WriteAsync(ParseDate(Required(csv, "end_date")), NpgsqlDbType.Date, ct);
                }, ct);

            await CopyAsync(conn, zip, "trips.txt",
                "COPY trips (import_id, trip_id, route_id, service_id, headsign, direction_id) FROM STDIN (FORMAT BINARY)",
                async (csv, w) =>
                {
                    await w.WriteAsync(id, NpgsqlDbType.Integer, ct);
                    await w.WriteAsync(Required(csv, "trip_id"), NpgsqlDbType.Text, ct);
                    await w.WriteAsync(Required(csv, "route_id"), NpgsqlDbType.Text, ct);
                    await w.WriteAsync(Required(csv, "service_id"), NpgsqlDbType.Text, ct);
                    await WriteNullableAsync(w, Optional(csv, "trip_headsign"), NpgsqlDbType.Text, ct);
                    var direction = Optional(csv, "direction_id");
                    await WriteNullableAsync(w, direction is null ? null : (short?)short.Parse(direction), NpgsqlDbType.Smallint, ct);
                }, ct);

            var stopTimeCount = await CopyAsync(conn, zip, "stop_times.txt",
                "COPY stop_times (import_id, trip_id, stop_sequence, stop_id, arrival_seconds, departure_seconds, pickup_type, drop_off_type) FROM STDIN (FORMAT BINARY)",
                async (csv, w) =>
                {
                    await w.WriteAsync(id, NpgsqlDbType.Integer, ct);
                    await w.WriteAsync(Required(csv, "trip_id"), NpgsqlDbType.Text, ct);
                    await w.WriteAsync(int.Parse(Required(csv, "stop_sequence")), NpgsqlDbType.Integer, ct);
                    await w.WriteAsync(Required(csv, "stop_id"), NpgsqlDbType.Text, ct);
                    await WriteNullableAsync(w, GtfsTime.ParseSeconds(Optional(csv, "arrival_time")), NpgsqlDbType.Integer, ct);
                    await WriteNullableAsync(w, GtfsTime.ParseSeconds(Optional(csv, "departure_time")), NpgsqlDbType.Integer, ct);
                    await w.WriteAsync(ShortOrZero(csv, "pickup_type"), NpgsqlDbType.Smallint, ct);
                    await w.WriteAsync(ShortOrZero(csv, "drop_off_type"), NpgsqlDbType.Smallint, ct);
                }, ct);

            await SwapAsync(conn, id, stopTimeCount, ct);

            logger.LogInformation("Imported Timetable {ImportId} with {StopTimeCount} stop times", id, stopTimeCount);
            return new ImportResult(ImportOutcome.Imported, id, stopTimeCount);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Activates the new import and removes every other import's rows, including leftovers from
    /// an import that failed partway.
    /// </summary>
    private static async Task SwapAsync(NpgsqlConnection conn, int id, long stopTimeCount, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        string[] statements =
        [
            "UPDATE timetable_imports SET is_active = false WHERE is_active AND id <> @id",
            "UPDATE timetable_imports SET is_active = true, stop_time_count = @count WHERE id = @id",
            "DELETE FROM stop_times WHERE import_id <> @id",
            "DELETE FROM trips WHERE import_id <> @id",
            "DELETE FROM service_calendars WHERE import_id <> @id",
            "DELETE FROM routes WHERE import_id <> @id",
            "DELETE FROM stops WHERE import_id <> @id",
            "DELETE FROM timetable_imports WHERE id <> @id",
        ];
        foreach (var sql in statements)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx) { CommandTimeout = 300 };
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("count", stopTimeCount);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private static async Task<long> CopyAsync(NpgsqlConnection conn, ZipArchive zip, string file, string copySql,
        Func<CsvReader, NpgsqlBinaryImporter, Task> writeRow, CancellationToken ct)
    {
        var entry = zip.GetEntry(file) ?? throw new InvalidDataException($"GTFS feed is missing {file}.");
        using var reader = new StreamReader(entry.Open());
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        await using var writer = await conn.BeginBinaryImportAsync(copySql, ct);

        await csv.ReadAsync();
        csv.ReadHeader();
        long rows = 0;
        while (await csv.ReadAsync())
        {
            await writer.StartRowAsync(ct);
            await writeRow(csv, writer);
            rows++;
        }
        await writer.CompleteAsync(ct);
        return rows;
    }

    /// <summary>Hashes the imported files' content, so a re-zipped but identical feed counts as unchanged.</summary>
    private static async Task<string> HashContentAsync(ZipArchive zip, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        foreach (var file in ImportedFiles)
        {
            var entry = zip.GetEntry(file) ?? throw new InvalidDataException($"GTFS feed is missing {file}.");
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(file));
            await using var stream = entry.Open();
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0) hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string Required(CsvReader csv, string name) =>
        Optional(csv, name) ?? throw new InvalidDataException($"Missing required GTFS field '{name}' on row {csv.Parser.Row}.");

    private static string? Optional(CsvReader csv, string name) =>
        csv.TryGetField<string>(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static short ShortOrZero(CsvReader csv, string name) =>
        Optional(csv, name) is { } v ? short.Parse(v) : (short)0;

    private static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture);

    private static Task WriteNullableAsync<T>(NpgsqlBinaryImporter w, T? value, NpgsqlDbType type, CancellationToken ct) =>
        value is null ? w.WriteNullAsync(ct) : w.WriteAsync(value, type, ct);
}
