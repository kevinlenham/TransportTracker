using TransitRealtime;
using TransportTracker.Api.Alerts;

namespace TransportTracker.Tests.Alerts;

public class ServiceAlertsTests
{
    private static readonly Dictionary<string, string> LineByRoute = new() { ["APS_1a"] = "T8", ["IWL_1a"] = "T2" };
    private static readonly Dictionary<string, string> StationByStop = new() { ["2000323"] = "200060" };

    [Fact]
    public void RoutesResolveToLinesAndPlatformsToStations()
    {
        var alert = Single(AlertEntity("A1", "Lifts out of service",
            new EntitySelector { RouteId = "APS_1a" },
            new EntitySelector { StopId = "2000323" },
            new EntitySelector { Trip = new TripDescriptor { RouteId = "IWL_1a" } }));

        Assert.Equal(["T2", "T8"], alert.Lines);
        Assert.Equal(["200060"], alert.StationIds);
        Assert.False(alert.NetworkWide);
    }

    [Fact]
    public void AlertNamingOnlyTheAgencyIsNetworkWide()
    {
        var alert = Single(AlertEntity("A1", "Industrial action", new EntitySelector { AgencyId = "SydneyTrains" }));

        Assert.True(alert.NetworkWide);
        Assert.True(alert.Affects(["999"], []));
    }

    [Fact]
    public void EnglishTextIsPreferredAndEnumsAreNamed()
    {
        var feedAlert = new Alert
        {
            HeaderText = Translated(("fr", "Travaux"), ("en", "Trackwork")),
            DescriptionText = Translated(("en", "  Buses replace trains.  ")),
            Cause = Alert.Types.Cause.Maintenance,
            Effect = Alert.Types.Effect.ModifiedService,
        };
        feedAlert.InformedEntity.Add(new EntitySelector { RouteId = "APS_1a" });

        var alert = Single(new FeedEntity { Id = "A1", Alert = feedAlert });

        Assert.Equal(("Trackwork", "Buses replace trains.", "Maintenance", "ModifiedService"),
            (alert.Header, alert.Description, alert.Cause, alert.Effect));
    }

    [Fact]
    public void AlertIsActiveOnlyWithinItsPeriods()
    {
        var start = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
        var entity = AlertEntity("A1", "Trackwork", new EntitySelector { RouteId = "APS_1a" });
#pragma warning disable CS0612 // TfNSW still sends the deprecated active_period.
        entity.Alert.ActivePeriod.Add(Range(start, start.AddHours(4)));
#pragma warning restore CS0612

        var alert = Single(entity);

        Assert.False(alert.IsActive(start.AddMinutes(-1)));
        Assert.True(alert.IsActive(start));
        Assert.False(alert.IsActive(start.AddHours(4)));
    }

    [Fact]
    public void CommunicationPeriodIsPreferredOverActivePeriod()
    {
        var start = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
        var entity = AlertEntity("A1", "Trackwork", new EntitySelector { RouteId = "APS_1a" });
#pragma warning disable CS0612
        entity.Alert.ActivePeriod.Add(Range(start, start.AddHours(1)));
#pragma warning restore CS0612
        entity.Alert.CommunicationPeriod.Add(Range(start.AddDays(-1), start.AddDays(1)));

        var alert = Single(entity);

        Assert.Equal(start.AddDays(-1), Assert.Single(alert.ActivePeriods).Start);
    }

    private static TimeRange Range(DateTimeOffset start, DateTimeOffset end) =>
        new() { Start = (ulong)start.ToUnixTimeSeconds(), End = (ulong)end.ToUnixTimeSeconds() };

    [Fact]
    public void AlertsWithoutAHeaderOrDeletedAreDropped()
    {
        var feed = new FeedMessage { Header = new FeedHeader { GtfsRealtimeVersion = "2.0" } };
        feed.Entity.Add(new FeedEntity { Id = "NO_HEADER", Alert = new Alert() });
        feed.Entity.Add(AlertEntity("DELETED", "Old", new EntitySelector { RouteId = "APS_1a" }));
        feed.Entity[1].IsDeleted = true;

        Assert.Empty(ServiceAlerts.From(feed, LineByRoute, StationByStop));
    }

    [Fact]
    public void AffectsMatchesAnyRequestedStationOrLine()
    {
        var alert = Single(AlertEntity("A1", "Trackwork", new EntitySelector { RouteId = "APS_1a" }, new EntitySelector { StopId = "2000323" }));

        Assert.True(alert.Affects([], ["T8"]));
        Assert.True(alert.Affects(["200060"], []));
        Assert.False(alert.Affects(["213510"], ["T1"]));
    }

    private static ServiceAlert Single(FeedEntity entity)
    {
        var feed = new FeedMessage { Header = new FeedHeader { GtfsRealtimeVersion = "2.0" } };
        feed.Entity.Add(entity);
        return Assert.Single(ServiceAlerts.From(feed, LineByRoute, StationByStop));
    }

    private static FeedEntity AlertEntity(string id, string header, params EntitySelector[] informed)
    {
        var alert = new Alert { HeaderText = Translated(("en", header)) };
        alert.InformedEntity.AddRange(informed);
        return new FeedEntity { Id = id, Alert = alert };
    }

    private static TranslatedString Translated(params (string Language, string Text)[] translations)
    {
        var s = new TranslatedString();
        foreach (var (language, text) in translations)
            s.Translation.Add(new TranslatedString.Types.Translation { Language = language, Text = text });
        return s;
    }
}
