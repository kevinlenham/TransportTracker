using TransportTracker.Api.Stats;

namespace TransportTracker.Tests.Stats;

public class TimeBandTests
{
    private static readonly TimeSpan Aest = TimeSpan.FromHours(10);

    [Theory]
    [InlineData(6, 29, TimeBand.OffPeak)]
    [InlineData(6, 30, TimeBand.AmPeak)]
    [InlineData(9, 29, TimeBand.AmPeak)]
    [InlineData(9, 30, TimeBand.OffPeak)]
    [InlineData(14, 59, TimeBand.OffPeak)]
    [InlineData(15, 0, TimeBand.PmPeak)]
    [InlineData(18, 59, TimeBand.PmPeak)]
    [InlineData(19, 0, TimeBand.OffPeak)]
    public void WeekdayBandsIncludeTheirStartButNotTheirEnd(int hour, int minute, TimeBand expected) =>
        Assert.Equal(expected, TimeBands.Of(new DateTimeOffset(2026, 10, 1, hour, minute, 0, Aest)));

    [Fact]
    public void WeekendHasNoPeaks() =>
        Assert.Equal(TimeBand.Weekend, TimeBands.Of(new DateTimeOffset(2026, 10, 3, 8, 0, 0, Aest)));

    [Fact]
    public void BandIsWorkedOutInSydneyTime()
    {
        // 22:00 UTC Thursday is 08:00 Friday in Sydney.
        Assert.Equal(TimeBand.AmPeak, TimeBands.Of(new DateTimeOffset(2026, 10, 1, 22, 0, 0, TimeSpan.Zero)));
    }
}
