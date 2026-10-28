using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class TiboPollingPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 59, 0, TimeSpan.Zero);

    [Fact]
    public void For_UsesNormalCadence_WhenNoSignal()
    {
        AssertPlan(TiboPollingPolicy.For(null, Now), 300, 15);
    }

    [Theory]
    [InlineData(TiboSignalKind.None)]
    [InlineData(TiboSignalKind.RecentPost)]
    [InlineData(TiboSignalKind.Bonus)]
    public void For_UsesNormalCadence_ForNonActionableSignal(TiboSignalKind kind)
    {
        AssertPlan(TiboPollingPolicy.For(Signal(kind), Now), 300, 15);
    }

    [Fact]
    public void For_SlowsDown_WhenConfirmed()
    {
        AssertPlan(TiboPollingPolicy.For(Signal(TiboSignalKind.Confirmed), Now), 180, 15);
    }

    [Theory]
    [InlineData(7201, 180, 15)]
    [InlineData(7200, 60, 5)]
    [InlineData(7199, 60, 5)]
    [InlineData(901, 60, 5)]
    [InlineData(900, 20, 5)]
    [InlineData(899, 20, 5)]
    [InlineData(0, 20, 5)]
    public void For_ChangesStage_AtDeadlineBoundaries(int secondsUntilStart, int dynamicSeconds, int quotaSeconds)
    {
        var signal = Signal(TiboSignalKind.Planned) with { ExpectedAt = Now.AddSeconds(secondsUntilStart) };

        AssertPlan(TiboPollingPolicy.For(signal, Now), dynamicSeconds, quotaSeconds);
    }

    [Theory]
    [InlineData(7201, 180, 15)]
    [InlineData(7200, 60, 5)]
    [InlineData(900, 20, 5)]
    [InlineData(0, 20, 5)]
    [InlineData(-1, 20, 5)]
    public void For_UsesEndDeadline_WhenStartIsMissing(int secondsUntilEnd, int dynamicSeconds, int quotaSeconds)
    {
        var signal = Signal(TiboSignalKind.Planned) with { ExpectedEndAt = Now.AddSeconds(secondsUntilEnd) };

        AssertPlan(TiboPollingPolicy.For(signal, Now), dynamicSeconds, quotaSeconds);
    }

    [Theory]
    [InlineData(600, 14400, 20, 5)]
    [InlineData(14400, 3600, 60, 5)]
    [InlineData(10800, 14400, 180, 15)]
    public void For_UsesEarliestFutureDeadline(int startSeconds, int endSeconds, int dynamicSeconds, int quotaSeconds)
    {
        var signal = Signal(TiboSignalKind.Planned) with
        {
            ExpectedAt = Now.AddSeconds(startSeconds),
            ExpectedEndAt = Now.AddSeconds(endSeconds)
        };

        AssertPlan(TiboPollingPolicy.For(signal, Now), dynamicSeconds, quotaSeconds);
    }

    [Fact]
    public void For_StaysFast_InsideAnnouncedWindowEvenWithDistantEnd()
    {
        var signal = Signal(TiboSignalKind.Planned) with
        {
            ExpectedAt = Now.AddMinutes(-1),
            ExpectedEndAt = Now.AddHours(4)
        };

        AssertPlan(TiboPollingPolicy.For(signal, Now), 20, 5);
    }

    [Fact]
    public void For_StaysFast_WhenTimingIsUnknown()
    {
        AssertPlan(TiboPollingPolicy.For(Signal(TiboSignalKind.Planned), Now), 20, 5);
    }

    [Fact]
    public void For_TransitionsAsTimeAdvances_WithoutMutatingSignal()
    {
        var start = Now.AddHours(3);
        var signal = Signal(TiboSignalKind.Planned) with { ExpectedAt = start, ExpectedEndAt = start.AddHours(4) };

        var far = TiboPollingPolicy.For(signal, Now);
        var soon = TiboPollingPolicy.For(signal, start.AddHours(-2));
        var imminent = TiboPollingPolicy.For(signal, start.AddMinutes(-15));
        var active = TiboPollingPolicy.For(signal, start.AddMinutes(1));

        AssertPlan(far, 180, 15);
        AssertPlan(soon, 60, 5);
        AssertPlan(imminent, 20, 5);
        AssertPlan(active, 20, 5);
        Assert.NotEqual(far.Mode, soon.Mode);
        Assert.NotEqual(soon.Mode, imminent.Mode);
        Assert.Equal(imminent.Mode, active.Mode);
        Assert.Equal(start, signal.ExpectedAt);
    }

    [Fact]
    public void For_IsIndependentOfOffsets_AcrossChinaMidnight()
    {
        var signal = Signal(TiboSignalKind.Planned) with { ExpectedAt = Now.AddMinutes(10) };
        var chinaOffset = TimeSpan.FromHours(8);
        var chinaSignal = signal with { ExpectedAt = Now.AddMinutes(10).ToOffset(chinaOffset) };

        Assert.Equal(TiboPollingPolicy.For(signal, Now),
            TiboPollingPolicy.For(chinaSignal, Now.ToOffset(chinaOffset)));
        AssertPlan(TiboPollingPolicy.For(chinaSignal, Now), 20, 5);
    }

    [Theory]
    [InlineData(TiboSignalKind.ObservedReset)]
    [InlineData(TiboSignalKind.ExpiredNotice)]
    public void For_UsesFastCadence_WhenAwaitingConfirmation(TiboSignalKind kind)
    {
        AssertPlan(TiboPollingPolicy.For(Signal(kind), Now), 20, 5);
    }

    [Fact]
    public void For_DoesNotAgeOutObservedReset()
    {
        var signal = Signal(TiboSignalKind.ObservedReset) with
        {
            ObservedAt = Now.AddHours(-3),
            PostTime = Now.AddDays(-1)
        };

        AssertPlan(TiboPollingPolicy.For(signal, Now), 20, 5);
    }

    [Theory]
    [InlineData(7199, 20, 5)]
    [InlineData(7200, 20, 5)]
    [InlineData(7201, 180, 15)]
    public void For_SlowsOldExpiredNotice_OnlyAfterTwoHours(int ageSeconds, int dynamicSeconds, int quotaSeconds)
    {
        var signal = Signal(TiboSignalKind.ExpiredNotice) with
        {
            ExpectedEndAt = Now.AddSeconds(-ageSeconds),
            PostTime = Now.AddDays(-1)
        };

        AssertPlan(TiboPollingPolicy.For(signal, Now), dynamicSeconds, quotaSeconds);
    }

    [Fact]
    public void For_AgesExpiredNotice_FromEndBeforeStartOrPost()
    {
        var signal = Signal(TiboSignalKind.ExpiredNotice) with
        {
            ExpectedAt = Now.AddHours(-4),
            ExpectedEndAt = Now.AddMinutes(-1),
            PostTime = Now.AddDays(-1)
        };

        AssertPlan(TiboPollingPolicy.For(signal, Now), 20, 5);
    }

    [Fact]
    public void For_FallsBackToExpectedStart_WhenExpiredEndIsUnknown()
    {
        var signal = Signal(TiboSignalKind.ExpiredNotice) with { ExpectedAt = Now.AddHours(-3) };

        AssertPlan(TiboPollingPolicy.For(signal, Now), 180, 15);
    }

    [Fact]
    public void For_FallsBackToPostTime_NotFreshCheckedAt_WhenExpiredTimingIsUnknown()
    {
        var signal = Signal(TiboSignalKind.ExpiredNotice) with { PostTime = Now.AddHours(-3) };

        AssertPlan(TiboPollingPolicy.For(signal, Now), 180, 15);
    }

    [Fact]
    public void For_StaysFast_WhenExpiredAgeIsUnknown()
    {
        var signal = Signal(TiboSignalKind.ExpiredNotice) with { PostTime = null };

        AssertPlan(TiboPollingPolicy.For(signal, Now), 20, 5);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 300)]
    [InlineData(6, 300)]
    [InlineData(int.MaxValue, 300)]
    [InlineData(int.MinValue, 30)]
    public void For_UsesBoundedExponentialRetry_WhenUnavailable(int failures, int dynamicSeconds)
    {
        AssertPlan(TiboPollingPolicy.For(Signal(TiboSignalKind.Unavailable), Now, failures), dynamicSeconds, 15);
    }

    [Theory]
    [InlineData(TiboSignalKind.None)]
    [InlineData(TiboSignalKind.RecentPost)]
    [InlineData(TiboSignalKind.Bonus)]
    [InlineData(TiboSignalKind.Planned)]
    [InlineData(TiboSignalKind.ObservedReset)]
    [InlineData(TiboSignalKind.ExpiredNotice)]
    [InlineData(TiboSignalKind.Confirmed)]
    public void For_FailuresOverrideEveryStatus(TiboSignalKind kind)
    {
        AssertPlan(TiboPollingPolicy.For(Signal(kind), Now, 3), 120, 15);
    }

    [Fact]
    public void For_FailuresOverrideMissingSignal_AndSuccessRestoresStatusPolicy()
    {
        AssertPlan(TiboPollingPolicy.For(null, Now, 2), 60, 15);
        var signal = Signal(TiboSignalKind.Planned);
        AssertPlan(TiboPollingPolicy.For(signal, Now, 4), 240, 15);
        AssertPlan(TiboPollingPolicy.For(signal, Now), 20, 5);
    }

    [Fact]
    public void For_IgnoresNegativeFailureCount_WhenSignalIsAvailable()
    {
        AssertPlan(TiboPollingPolicy.For(null, Now, int.MinValue), 300, 15);
    }

    [Theory]
    [InlineData(-10, 85)]
    [InlineData(0, 85)]
    [InlineData(0.5, 100)]
    [InlineData(1, 115)]
    [InlineData(10, 115)]
    [InlineData(double.NegativeInfinity, 85)]
    [InlineData(double.PositiveInfinity, 115)]
    [InlineData(double.NaN, 100)]
    public void WithJitter_ClampsSample_AndScalesByAtMostFifteenPercent(double sample, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds),
            TiboPollingPolicy.WithJitter(TimeSpan.FromSeconds(100), sample));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(180)]
    [InlineData(240)]
    [InlineData(300)]
    public void WithJitter_RemainsWithinBounds_ForEveryPolicyInterval(int seconds)
    {
        var interval = TimeSpan.FromSeconds(seconds);
        for (var step = 0; step <= 100; step++)
        {
            var result = TiboPollingPolicy.WithJitter(interval, step / 100.0);

            Assert.InRange(result.TotalSeconds, seconds * 0.85, seconds * 1.15 + 0.0000001);
        }
        Assert.Equal(interval, TiboPollingPolicy.WithJitter(interval, 0.5));
    }

    [Fact]
    public void WithJitter_HandlesTimeSpanBounds_WithoutOverflow()
    {
        Assert.Equal(TimeSpan.Zero, TiboPollingPolicy.WithJitter(TimeSpan.Zero, 1));
        Assert.Equal(TimeSpan.FromTicks(1), TiboPollingPolicy.WithJitter(TimeSpan.FromTicks(1), 0));
        Assert.Equal(TimeSpan.MaxValue, TiboPollingPolicy.WithJitter(TimeSpan.MaxValue, 1));
        Assert.Equal(TimeSpan.MaxValue, TiboPollingPolicy.WithJitter(TimeSpan.MaxValue, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TiboPollingPolicy.WithJitter(TimeSpan.FromTicks(-1), 0.5));
    }

    private static TiboSignal Signal(TiboSignalKind kind) =>
        new(kind, "headline", "detail", null, Now, Now);

    private static void AssertPlan(TiboPollingPlan plan, int dynamicSeconds, int quotaSeconds)
    {
        Assert.False(string.IsNullOrWhiteSpace(plan.Mode));
        Assert.Equal(TimeSpan.FromSeconds(dynamicSeconds), plan.DynamicInterval);
        Assert.Equal(TimeSpan.FromSeconds(quotaSeconds), plan.QuotaInterval);
        Assert.InRange(plan.DynamicInterval, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(5));
        Assert.InRange(plan.QuotaInterval, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
    }
}
