using TransitRealtime;

namespace TransportTracker.Api.Alerts;

public record AlertPeriod(DateTimeOffset? Start, DateTimeOffset? End);

/// <summary>
/// A TfNSW service alert, with what it affects resolved to Lines and Stations. NetworkWide alerts name
/// no particular Line or Station, so they affect every Saved Trip.
/// </summary>
public record ServiceAlert(
    string Id,
    string Header,
    string? Description,
    string? Url,
    string Cause,
    string Effect,
    string Severity,
    IReadOnlyList<AlertPeriod> ActivePeriods,
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> StationIds,
    bool NetworkWide)
{
    /// <summary>Active now, or always if TfNSW gave no periods.</summary>
    public bool IsActive(DateTimeOffset now) =>
        ActivePeriods.Count == 0 || ActivePeriods.Any(p => (p.Start is null || p.Start <= now) && (p.End is null || now < p.End));

    public bool Affects(IReadOnlyCollection<string> stationIds, IReadOnlyCollection<string> lines) =>
        NetworkWide || StationIds.Any(stationIds.Contains) || Lines.Any(lines.Contains);
}

public static class ServiceAlerts
{
    /// <summary>
    /// Turns the feed into ServiceAlerts. Route and stop IDs are resolved with the active Timetable:
    /// routes to their Line, and platforms to their Station.
    /// </summary>
    public static List<ServiceAlert> From(FeedMessage feed, IReadOnlyDictionary<string, string> lineByRoute,
        IReadOnlyDictionary<string, string> stationByStop)
    {
        var alerts = new List<ServiceAlert>();
        foreach (var entity in feed.Entity)
        {
            if (entity.Alert is not { } alert || entity.IsDeleted) continue;
            var header = Text(alert.HeaderText);
            if (header is null) continue;

            var lines = new HashSet<string>();
            var stations = new HashSet<string>();
            var networkWide = false;
            foreach (var e in alert.InformedEntity)
            {
                var route = e.HasRouteId ? e.RouteId : e.Trip?.HasRouteId == true ? e.Trip.RouteId : null;
                if (route is not null && lineByRoute.TryGetValue(route, out var line)) lines.Add(line);
                if (e.HasStopId) stations.Add(stationByStop.GetValueOrDefault(e.StopId, e.StopId));
                // Only an agency or a mode: the whole network.
                if (!e.HasRouteId && !e.HasStopId && e.Trip is null) networkWide = true;
            }

            alerts.Add(new ServiceAlert(
                entity.Id,
                header,
                Text(alert.DescriptionText),
                Text(alert.Url),
                alert.Cause.ToString(),
                alert.Effect.ToString(),
                alert.SeverityLevel.ToString(),
                ShowDuring(alert).Select(p => new AlertPeriod(
                    p.HasStart ? DateTimeOffset.FromUnixTimeSeconds((long)p.Start) : null,
                    p.HasEnd ? DateTimeOffset.FromUnixTimeSeconds((long)p.End) : null)).ToList(),
                lines.Order().ToList(),
                stations.Order().ToList(),
                networkWide && lines.Count == 0 && stations.Count == 0));
        }
        return alerts;
    }

    /// <summary>
    /// When to show the alert. The spec replaced active_period with communication_period, but TfNSW
    /// still sends active_period, so fall back to it.
    /// </summary>
    private static IEnumerable<TimeRange> ShowDuring(Alert alert)
    {
#pragma warning disable CS0612 // active_period is deprecated
        return alert.CommunicationPeriod.Count > 0 ? alert.CommunicationPeriod : alert.ActivePeriod;
#pragma warning restore CS0612
    }

    /// <summary>The plain English translation, or failing that the first one.</summary>
    private static string? Text(TranslatedString? s)
    {
        if (s is null || s.Translation.Count == 0) return null;
        var t = s.Translation.FirstOrDefault(t => t.Language is "en" or "") ?? s.Translation[0];
        return string.IsNullOrWhiteSpace(t.Text) ? null : t.Text.Trim();
    }
}
