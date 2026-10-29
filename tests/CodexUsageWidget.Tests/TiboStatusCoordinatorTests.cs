using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class TiboStatusCoordinatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void InitialFreshAlignedReadingIsOnlyABaseline(int used)
    {
        var coordinator = new TiboStatusCoordinator();
        var now = Reset.AddMinutes(3);
        coordinator.OnSignal(Notice(now), now);
        coordinator.OnSnapshot(Snapshot(used, Reset, Reset), now);
        Assert.NotEqual(TiboSignalKind.Confirmed, coordinator.Display?.Kind);
        Assert.False(coordinator.TakeCelebration());
    }

    [Fact]
    public void InitialFullReadingWithoutCycleMetadataDoesNotCelebrate()
    {
        var coordinator = new TiboStatusCoordinator();
        coordinator.OnSnapshot(new RateLimitSnapshot
        {
            Primary = new RateLimitWindow { UsedPercent = 0, WindowDurationMins = 10080 }
        }, Reset);
        Assert.NotEqual(TiboSignalKind.Confirmed, coordinator.Display?.Kind);
        Assert.False(coordinator.TakeCelebration());
    }

    private static readonly DateTimeOffset Posted = DateTimeOffset.Parse("2026-10-25T03:39:31Z");
    private static readonly DateTimeOffset Reset = DateTimeOffset.Parse("2026-10-25T06:17:00Z");
    private const string Source = "https://x.com/thsottiaux/status/2114200152701575656";

    [Fact]
    public void WitnessedFreshAlignedRecoveryConfirmsOnlyOnce()
    {
        var coordinator = Tracking();
        var now = Reset.AddMinutes(3);
        coordinator.OnSnapshot(Snapshot(0, Reset, Reset), now);
        coordinator.OnSignal(Notice(now), now);

        Assert.Equal(TiboSignalKind.Confirmed, coordinator.Display?.Kind);
        Assert.True(coordinator.TakeCelebration());
        Assert.False(coordinator.TakeCelebration());
        coordinator.OnSnapshot(Snapshot(0, Reset, Reset), now.AddSeconds(15));
        Assert.False(coordinator.TakeCelebration());
    }

    [Fact]
    public void WeeklyAtHundredPercentImmediatelyEndsVerificationRegardlessOfNoticeTime()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        coordinator.OnSignal(Notice(now), now);
        coordinator.OnSnapshot(Snapshot(0, Posted.AddHours(-4), Posted.AddHours(-4)), now);

        Assert.Equal(TiboSignalKind.Confirmed, coordinator.Display?.Kind);
        Assert.True(coordinator.TakeCelebration());
    }

    [Fact]
    public void FiveHourRecoveryAloneDoesNotConfirmWeeklyReset()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        coordinator.OnSignal(Notice(now), now);
        coordinator.OnSnapshot(Snapshot(40, Reset.AddDays(-2), Reset), now);

        Assert.Equal(TiboSignalKind.Planned, coordinator.Display?.Kind);
    }

    [Fact]
    public void ChangedWeeklyWindowConfirmsAfterPriorUsage()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        coordinator.OnSignal(Notice(now), now);
        coordinator.OnSnapshot(Snapshot(48, Reset.AddDays(-7), Reset.AddHours(-1)), now.AddMinutes(-1));
        coordinator.OnSnapshot(Snapshot(0, Reset, Reset.AddHours(-1)), now);

        Assert.Equal(TiboSignalKind.Confirmed, coordinator.Display?.Kind);
    }

    [Fact]
    public void LaterVerifiedPostReplacesStaleAnnouncement()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        coordinator.OnSnapshot(Snapshot(0, Reset, Reset), now);
        coordinator.OnSignal(Notice(now), now);
        const string laterUrl = "https://x.com/thsottiaux/status/2114200152701575657";
        coordinator.OnSnapshot(Snapshot(4, Reset, Reset), now.AddMinutes(2));
        coordinator.OnSignal(Notice(now) with
        {
            LatestPost = new TiboPost("New update from Tibo", laterUrl, now.AddMinutes(1))
        }, now.AddMinutes(2));

        Assert.Equal(TiboSignalKind.RecentPost, coordinator.Display?.Kind);
        Assert.Contains("New update", coordinator.Display!.Detail);
    }

    [Fact]
    public void RestartAfterPublicWindowClosedStillConfirmsAccountRecovery()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        coordinator.OnSnapshot(Snapshot(0, Reset, Reset), now);
        coordinator.OnSignal(Notice(now) with
        {
            Kind = TiboSignalKind.ObservedReset,
            ObservedAt = Reset.AddMinutes(-6)
        }, now);

        Assert.Equal(TiboSignalKind.Confirmed, coordinator.Display?.Kind);
    }

    [Fact]
    public void PublicObservationStillConfirmsAfterAccountUsesFreshAllowance()
    {
        var now = Reset.AddMinutes(12);
        var coordinator = Tracking();
        coordinator.OnSnapshot(Snapshot(4, Reset, Reset), now);
        coordinator.OnSignal(Notice(now) with
        {
            Kind = TiboSignalKind.ObservedReset,
            ObservedAt = Reset.AddMinutes(-6)
        }, now);

        Assert.Equal(TiboSignalKind.Confirmed, coordinator.Display?.Kind);
    }

    [Fact]
    public void PublicObservationDoesNotConfirmOldWeeklyWindow()
    {
        var now = Reset.AddMinutes(12);
        var coordinator = Tracking();
        coordinator.OnSnapshot(Snapshot(4, Posted.AddHours(-5), Reset), now);
        coordinator.OnSignal(Notice(now) with
        {
            Kind = TiboSignalKind.ObservedReset,
            ObservedAt = Reset.AddMinutes(-6)
        }, now);

        Assert.Equal(TiboSignalKind.ObservedReset, coordinator.Display?.Kind);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 4)]
    public void LocalAlignedFreshWindowsConfirmDespiteDelayedAccountCycle(int weeklyUsed, int fiveHourUsed)
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        var snapshot = Snapshot(weeklyUsed, Reset, Reset);
        snapshot.Secondary!.UsedPercent = fiveHourUsed;
        coordinator.OnSnapshot(snapshot, now);
        coordinator.OnSignal(Notice(now) with
        {
            PostTime = Reset.AddHours(-4),
            Kind = TiboSignalKind.ObservedReset,
            ObservedAt = Reset.AddHours(-3)
        }, now);

        Assert.Equal(TiboSignalKind.Confirmed, coordinator.Display?.Kind);
        Assert.True(coordinator.TakeCelebration());
    }

    [Fact]
    public void PostBeforeAccountRecoveryDoesNotHideCelebration()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        coordinator.OnSnapshot(Snapshot(0, Reset, Reset), now);
        coordinator.OnSignal(Notice(now) with
        {
            LatestPost = new TiboPost("Other news before reset", Source + "1", Reset.AddMinutes(-30))
        }, now);

        Assert.Equal(TiboSignalKind.Confirmed, coordinator.Display?.Kind);
    }

    [Fact]
    public void SmallWindowExtensionWithUsageDropConfirmsWeeklyOnlyReset()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        coordinator.OnSignal(Notice(now), now);
        coordinator.OnSnapshot(new RateLimitSnapshot { Primary = Snapshot(65, Reset.AddHours(-1), Reset).Primary }, now.AddSeconds(-15));
        coordinator.OnSnapshot(new RateLimitSnapshot { Primary = Snapshot(0, Reset, Reset).Primary }, now);

        Assert.Equal(TiboSignalKind.Confirmed, coordinator.Display?.Kind);
    }

    [Fact]
    public void DynamicsFailureDoesNotEraseConfirmedAccountReset()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        coordinator.OnSnapshot(Snapshot(0, Reset, Reset), now);
        coordinator.OnSignal(Notice(now), now);
        coordinator.OnSignal(new(TiboSignalKind.Unavailable, "Unavailable", "Network failure", null, null, now), now.AddSeconds(15));

        Assert.Equal(TiboSignalKind.Confirmed, coordinator.Display?.Kind);
    }

    [Fact]
    public void MissingCycleMetadataDoesNotConfirmEvenAfterPriorUsage()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        var snapshot = new RateLimitSnapshot
        {
            Primary = new RateLimitWindow { UsedPercent = 0, WindowDurationMins = 10080 }
        };
        coordinator.OnSnapshot(snapshot, now);

        Assert.NotEqual(TiboSignalKind.Confirmed, coordinator.Display?.Kind);
        Assert.False(coordinator.TakeCelebration());
        coordinator.OnSnapshot(snapshot, now.AddSeconds(15));
        Assert.False(coordinator.TakeCelebration());
    }

    [Fact]
    public void NewWeeklyCycleCanCelebrateEvenWhenAnnouncementUrlIsUnchanged()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        coordinator.OnSnapshot(Snapshot(0, Reset, Reset), now);
        coordinator.OnSignal(Notice(now), now);
        Assert.True(coordinator.TakeCelebration());
        coordinator.OnSnapshot(Snapshot(15, Reset, Reset), now.AddHours(1));
        coordinator.OnSnapshot(Snapshot(0, Reset.AddHours(2), Reset.AddHours(2)), now.AddHours(2));

        Assert.True(coordinator.TakeCelebration());
        Assert.Equal(TiboSignalKind.Confirmed, coordinator.Display?.Kind);
    }

    [Fact]
    public void MissingWindowMetadataDoesNotRepeatCelebrationForKnownCycle()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        coordinator.OnSnapshot(Snapshot(0, Reset, Reset), now);
        Assert.True(coordinator.TakeCelebration());
        var missingId = Snapshot(0, Reset, Reset);
        missingId.Primary!.ResetsAt = null;
        coordinator.OnSnapshot(missingId, now.AddSeconds(15));
        Assert.False(coordinator.TakeCelebration());
        coordinator.OnSnapshot(Snapshot(0, Reset, Reset), now.AddSeconds(30));
        Assert.False(coordinator.TakeCelebration());
    }

    [Fact]
    public void UnknownCycleMetadataCannotManufactureRestoration()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        var missingId = Snapshot(0, Reset, Reset);
        missingId.Primary!.ResetsAt = null;
        coordinator.OnSnapshot(missingId, now);
        Assert.False(coordinator.TakeCelebration());
        coordinator.OnSnapshot(Snapshot(0, Reset, Reset), now.AddSeconds(15));
        Assert.False(coordinator.TakeCelebration());
        coordinator.OnSnapshot(Snapshot(0, Reset.AddHours(2), Reset.AddHours(2)), now.AddHours(2));
        Assert.False(coordinator.TakeCelebration());
    }

    [Fact]
    public void UnknownCycleAndNewWindowWithoutUsageDropNeverCelebrate()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        var missingId = Snapshot(0, Reset, Reset);
        missingId.Primary!.ResetsAt = null;
        coordinator.OnSnapshot(missingId, now);
        Assert.False(coordinator.TakeCelebration());
        coordinator.OnSnapshot(Snapshot(0, Reset.AddHours(2), Reset.AddHours(2)), now.AddHours(2));
        Assert.False(coordinator.TakeCelebration());
    }

    [Fact]
    public void MinorWindowAnchorDriftWhileFullDoesNotRepeatCelebration()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        coordinator.OnSnapshot(Snapshot(0, Reset, Reset), now);
        Assert.True(coordinator.TakeCelebration());
        coordinator.OnSnapshot(Snapshot(0, Reset.AddMinutes(2), Reset.AddMinutes(2)), now.AddSeconds(15));
        Assert.False(coordinator.TakeCelebration());
    }

    [Fact]
    public void ScreenshotAtNinetyFivePercentIdentifiesNewCycleWithoutWaitingForNotice()
    {
        var coordinator = Tracking();
        coordinator.OnSnapshot(new RateLimitSnapshot
        {
            Primary = new RateLimitWindow { UsedPercent = 31, WindowDurationMins = 300, ResetsAt = 1793024481 },
            Secondary = new RateLimitWindow { UsedPercent = 5, WindowDurationMins = 10080, ResetsAt = 1793611281 }
        }, DateTimeOffset.Parse("2026-10-26T17:50:41+08:00"));

        Assert.Equal(TiboSignalKind.Confirmed, coordinator.Display?.Kind);
        Assert.Contains("10月26日 17:21", coordinator.Display!.Detail);
        Assert.True(coordinator.TakeCelebration());
    }

    [Fact]
    public void CelebrationEventuallyReturnsToRecentPost()
    {
        var now = Reset.AddMinutes(3);
        var coordinator = Tracking();
        coordinator.OnSnapshot(Snapshot(4, Reset, Reset), now);
        var observed = Notice(now) with
        {
            Kind = TiboSignalKind.ObservedReset,
            ObservedAt = Reset.AddMinutes(-6),
            LatestPost = new TiboPost("Day 20/ Rest and reset", Source, Posted)
        };
        coordinator.OnSignal(observed, now);
        coordinator.OnSignal(observed, now.AddHours(7));

        Assert.Equal(TiboSignalKind.RecentPost, coordinator.Display?.Kind);
        Assert.Contains("Rest and reset", coordinator.Display!.Detail);
    }

    private static TiboStatusCoordinator Tracking()
    {
        var coordinator = new TiboStatusCoordinator();
        coordinator.OnSnapshot(Snapshot(60, Reset.AddDays(-1), Reset.AddHours(-1)), Reset);
        return coordinator;
    }

    private static TiboSignal Notice(DateTimeOffset checkedAt) =>
        new(TiboSignalKind.Planned, "Tibo 已预告重置", "北京时间待确认", Source, Posted, checkedAt);

    private static RateLimitSnapshot Snapshot(int weeklyUsed, DateTimeOffset weeklyStart,
        DateTimeOffset fiveHourStart) => new()
        {
            Primary = new RateLimitWindow
            {
                UsedPercent = weeklyUsed, WindowDurationMins = 10080,
                ResetsAt = weeklyStart.AddDays(7).ToUnixTimeSeconds()
            },
            Secondary = new RateLimitWindow
            {
                UsedPercent = 0, WindowDurationMins = 300,
                ResetsAt = fiveHourStart.AddHours(5).ToUnixTimeSeconds()
            }
        };
}
