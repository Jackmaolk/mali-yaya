using CodexUsageWidget;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class FiveHourCountdownPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
    private static RateLimitWindow Window(long? reset) => new() { WindowDurationMins = 300, ResetsAt = reset };

    [Fact]
    public void UsesServerResetTimeAndShowsSeconds()
    {
        Assert.Equal("04:59:32", FiveHourCountdownPolicy.Format(Window(Now.AddHours(5).AddSeconds(-28).ToUnixTimeSeconds()), Now));
    }

    [Fact]
    public void ExpiredWindowStaysAtZeroUntilNewServerTimestamp()
    {
        Assert.Equal("00:00:00", FiveHourCountdownPolicy.Format(Window(Now.ToUnixTimeSeconds()), Now));
        Assert.Equal("00:00:00", FiveHourCountdownPolicy.Format(Window(Now.ToUnixTimeSeconds()), Now.AddHours(2)));
        Assert.Equal("05:00:00", FiveHourCountdownPolicy.Format(Window(Now.AddHours(5).ToUnixTimeSeconds()), Now));
    }

    [Fact]
    public void UnavailableAndInvalidValuesWaitButKnownTimeSurvivesRefreshFailure()
    {
        Assert.Equal("暂无限额", FiveHourCountdownPolicy.Format(null, Now));
        Assert.Equal("等待同步", FiveHourCountdownPolicy.Format(Window(null), Now));
        Assert.Equal("等待同步", FiveHourCountdownPolicy.Format(Window(long.MaxValue), Now));
        var knownWindow = Window(Now.AddHours(5).ToUnixTimeSeconds());
        Assert.Equal("04:57:00", FiveHourCountdownPolicy.Format(knownWindow, Now.AddMinutes(3)));
    }

    [Fact]
    public void CountdownIsIndependentOfTimeZoneAndCatchesUpAfterSleep()
    {
        var window = Window(Now.AddHours(5).ToUnixTimeSeconds());
        var wokeAt = Now.AddHours(1);
        Assert.Equal("04:00:00", FiveHourCountdownPolicy.Format(window, wokeAt.ToOffset(TimeSpan.FromHours(8))));
        Assert.Equal("04:00:00", FiveHourCountdownPolicy.Format(window, wokeAt));
    }

    [Fact]
    public void RoundsUpPartialSecondsWithoutShowingZeroBeforeExpiry()
    {
        Assert.Equal("00:00:01", FiveHourCountdownPolicy.Format(Window(Now.AddSeconds(1).ToUnixTimeSeconds()),
            Now.AddMilliseconds(900)));
    }
}
