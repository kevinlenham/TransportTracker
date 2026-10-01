using TransportTracker.Api.Timetable;

namespace TransportTracker.Tests.Timetable;

public class GtfsTimeTests
{
    [Theory]
    [InlineData("04:49:00", 4 * 3600 + 49 * 60)]
    [InlineData("4:49:30", 4 * 3600 + 49 * 60 + 30)]
    [InlineData("25:10:00", 25 * 3600 + 10 * 60)] // Trip running past midnight
    [InlineData("00:00:00", 0)]
    public void ParsesTimes(string value, int expected) => Assert.Equal(expected, GtfsTime.ParseSeconds(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyMeansNoTime(string? value) => Assert.Null(GtfsTime.ParseSeconds(value));

    [Theory]
    [InlineData("12:60:00")]
    [InlineData("12:00")]
    [InlineData("ab:cd:ef")]
    public void RejectsInvalidTimes(string value) => Assert.Throws<FormatException>(() => GtfsTime.ParseSeconds(value));

    [Fact]
    public void ServiceDayStartsAtMidnightOnAnOrdinaryDay() =>
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(10)), GtfsTime.ServiceDayStart(new DateOnly(2026, 10, 1)));

    [Fact]
    public void ServiceDayStartsAtNoonMinus12HoursWhenDaylightSavingStarts() =>
        // Noon on 4 Oct 2026 is AEDT (UTC+11), so the day counts from 23:00 AEST on 3 Oct.
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 23, 0, 0, TimeSpan.FromHours(10)), GtfsTime.ServiceDayStart(new DateOnly(2026, 10, 4)));
}
