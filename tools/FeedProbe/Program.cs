// FeedProbe: polls the TfNSW Sydney Trains GTFS-realtime TripUpdates feed, saves each raw
// response as a fixture, and reports whether passed Stations drop out of the feed (ADR 0002).
//
// Usage: dotnet run --project tools/FeedProbe -- [minutes=60] [intervalSeconds=30]

using System.Net.Http.Headers;
using TransitRealtime;

const string FeedUrl = "https://api.transport.nsw.gov.au/v2/gtfs/realtime/sydneytrains";

var minutes = args.Length > 0 ? int.Parse(args[0]) : 60;
var interval = TimeSpan.FromSeconds(args.Length > 1 ? int.Parse(args[1]) : 30);

var repoRoot = FindRepoRoot();
var apiKey = Environment.GetEnvironmentVariable("NSW_API_KEY") ?? ReadDotEnv(repoRoot, "NSW_API_KEY")
    ?? throw new InvalidOperationException("NSW_API_KEY not set (env var or .env at repo root).");

var outDir = Path.Combine(repoRoot, "data", "feed-samples", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(outDir);

using var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("apikey", apiKey);

// (tripId, stopId) -> what we last saw for it
var previous = new Dictionary<(string Trip, string Stop), StopSnapshot>();
var stats = new ProbeStats();
var deadline = DateTime.UtcNow.AddMinutes(minutes);

Console.WriteLine($"Polling {FeedUrl} every {interval.TotalSeconds}s for {minutes} min. Saving to {outDir}");

while (DateTime.UtcNow < deadline)
{
    var started = DateTime.UtcNow;
    try
    {
        var bytes = await http.GetByteArrayAsync(FeedUrl);
        var feed = FeedMessage.Parser.ParseFrom(bytes);
        var feedTime = (long)feed.Header.Timestamp;
        await File.WriteAllBytesAsync(Path.Combine(outDir, $"{feedTime}.pb"), bytes);

        var current = Snapshot(feed);
        var tripsNow = current.Keys.Select(k => k.Trip).ToHashSet();
        Analyse(previous, current, tripsNow, feedTime, stats);
        previous = current;

        Console.WriteLine(
            $"{DateTimeOffset.FromUnixTimeSeconds(feedTime).ToLocalTime():HH:mm:ss} " +
            $"entities={feed.Entity.Count} trips={tripsNow.Count} stops={current.Count} " +
            $"| dropped: passed={stats.DroppedAfterPassing} early={stats.DroppedBeforeTime} " +
            $"| past-but-present={stats.PastButPresentNow} | trips-gone={stats.TripsDisappeared}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Poll failed: {ex.Message}");
    }

    var wait = interval - (DateTime.UtcNow - started);
    if (wait > TimeSpan.Zero) await Task.Delay(wait);
}

stats.Print();
return;

static Dictionary<(string Trip, string Stop), StopSnapshot> Snapshot(FeedMessage feed)
{
    var result = new Dictionary<(string Trip, string Stop), StopSnapshot>();
    foreach (var entity in feed.Entity)
    {
        if (entity.TripUpdate is not { } tu) continue;
        foreach (var stu in tu.StopTimeUpdate)
        {
            var evt = stu.Arrival ?? stu.Departure;
            result[(tu.Trip.TripId, stu.StopId)] = new StopSnapshot(
                Delay: evt?.HasDelay == true ? evt.Delay : null,
                PredictedTime: evt?.HasTime == true ? evt.Time : null,
                Skipped: stu.ScheduleRelationship == TripUpdate.Types.StopTimeUpdate.Types.ScheduleRelationship.Skipped,
                TripCancelled: tu.Trip.ScheduleRelationship == TripDescriptor.Types.ScheduleRelationship.Canceled);
        }
    }
    return result;
}

static void Analyse(
    Dictionary<(string Trip, string Stop), StopSnapshot> previous,
    Dictionary<(string Trip, string Stop), StopSnapshot> current,
    HashSet<string> tripsNow, long feedTime, ProbeStats stats)
{
    foreach (var (key, snap) in previous)
    {
        if (current.ContainsKey(key)) continue;
        if (!tripsNow.Contains(key.Trip)) { stats.TripStopsLostWithTrip++; continue; }

        // The Station dropped out while the Trip is still in the feed: the case ADR 0002 relies on.
        if (snap.PredictedTime is { } t && t > feedTime + 60)
        {
            stats.DroppedBeforeTime++;
            stats.AddExample(stats.EarlyDropExamples, $"{key.Trip} @ {key.Stop}: predicted {t - feedTime}s in future");
        }
        else
        {
            stats.DroppedAfterPassing++;
            if (snap.Delay is { } d) stats.FinalDelays.Add(d);
        }
    }

    var tripsBefore = previous.Keys.Select(k => k.Trip).ToHashSet();
    stats.TripsDisappeared += tripsBefore.Count(t => !tripsNow.Contains(t));

    // Stations that should have been passed (predicted > 2 min ago) but are still listed.
    stats.PastButPresentNow = 0;
    foreach (var (key, snap) in current)
    {
        if (snap.PredictedTime is { } t && t < feedTime - 120)
        {
            stats.PastButPresentNow++;
            stats.AddExample(stats.PastExamples, $"{key.Trip} @ {key.Stop}: predicted {feedTime - t}s ago");
        }
        if (snap.Skipped) stats.SkippedSeen.Add(key);
        if (snap.TripCancelled) stats.CancelledTrips.Add(key.Trip);
        if (snap.Delay is null) stats.NoDelayField.Add(key);
    }
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

static string? ReadDotEnv(string root, string name)
{
    var path = Path.Combine(root, ".env");
    if (!File.Exists(path)) return null;
    return File.ReadLines(path)
        .Select(l => l.Split('=', 2))
        .Where(p => p.Length == 2 && p[0].Trim() == name)
        .Select(p => p[1].Trim().Trim('"'))
        .FirstOrDefault();
}

record StopSnapshot(int? Delay, long? PredictedTime, bool Skipped, bool TripCancelled);

class ProbeStats
{
    public int DroppedAfterPassing;
    public int DroppedBeforeTime;
    public int TripStopsLostWithTrip;
    public int TripsDisappeared;
    public int PastButPresentNow;
    public List<int> FinalDelays = [];
    public HashSet<(string, string)> SkippedSeen = [];
    public HashSet<string> CancelledTrips = [];
    public HashSet<(string, string)> NoDelayField = [];
    public List<string> EarlyDropExamples = [];
    public List<string> PastExamples = [];

    public void AddExample(List<string> list, string s) { if (list.Count < 5) list.Add(s); }

    public void Print()
    {
        Console.WriteLine();
        Console.WriteLine("=== Summary ===");
        Console.WriteLine($"Stations dropped after passing (ADR 0002 case): {DroppedAfterPassing}");
        Console.WriteLine($"Stations dropped while still in the future:      {DroppedBeforeTime}");
        Console.WriteLine($"Stations lost because the whole Trip vanished:   {TripStopsLostWithTrip} ({TripsDisappeared} trips)");
        Console.WriteLine($"Stations still listed >2 min after predicted time (last poll): {PastButPresentNow}");
        Console.WriteLine($"Distinct skipped trip-stops: {SkippedSeen.Count}, cancelled trips: {CancelledTrips.Count}");
        Console.WriteLine($"Trip-stops with no delay field (last poll): {NoDelayField.Count}");
        if (FinalDelays.Count > 0)
        {
            var late = FinalDelays.Count(d => d > 120);
            Console.WriteLine($"Would-be Observations: {FinalDelays.Count}, Late (>120s): {late} ({100.0 * late / FinalDelays.Count:F1}%)");
        }
        foreach (var e in EarlyDropExamples) Console.WriteLine($"  early drop: {e}");
        foreach (var e in PastExamples) Console.WriteLine($"  past but present: {e}");
    }
}
