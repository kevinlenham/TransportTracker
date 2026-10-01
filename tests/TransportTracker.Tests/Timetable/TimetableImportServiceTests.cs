using TransportTracker.Api.Timetable;

namespace TransportTracker.Tests.Timetable;

public class TimetableImportServiceTests
{
    [Fact]
    public void RunsAt3amSydneyLaterToday()
    {
        // 01:00 AEST (UTC+10) on 1 Oct 2026 → 03:00 AEST is 2 hours away.
        var now = new DateTimeOffset(2026, 9, 30, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(2), TimetableImportService.DelayUntilNextRun(now));
    }

    [Fact]
    public void RunsAt3amSydneyTomorrowOnceItHasPassed()
    {
        // 10:00 AEST on 1 Oct 2026 → next run is 03:00 AEST on 2 Oct, 17 hours away.
        var now = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(17), TimetableImportService.DelayUntilNextRun(now));
    }

    [Fact]
    public void AccountsForDaylightSavingStarting()
    {
        // Daylight saving starts at 02:00 on 4 Oct 2026, so 03:00 that morning is AEDT (UTC+11).
        // 12:00 AEST on 3 Oct → 03:00 AEDT on 4 Oct is 14 hours of real time away.
        var now = new DateTimeOffset(2026, 10, 3, 2, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(14), TimetableImportService.DelayUntilNextRun(now));
    }
}
