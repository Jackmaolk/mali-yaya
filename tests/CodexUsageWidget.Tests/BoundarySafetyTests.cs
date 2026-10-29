using System.Text.Json;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class BoundarySafetyTests
{
    [Fact]
    public void MissingUsageMustNotDeserializeAsFullAllowance()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RateLimitWindow>(
            "{\"windowDurationMins\":10080,\"resetsAt\":1794000000}"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(100)]
    public void ExplicitValidUsageIncludingFullAllowanceRemainsSupported(int used)
    {
        var window = JsonSerializer.Deserialize<RateLimitWindow>("{\"usedPercent\":" + used + "}")!;
        Assert.Equal(used, window.UsedPercent);
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MinValue + 300)]
    [InlineData(long.MaxValue)]
    public void ExtremeWindowDurationsDoNotCrashFiveHourSelection(long duration)
    {
        var valid = new RateLimitWindow { UsedPercent = 30, WindowDurationMins = 300 };
        var snapshot = new RateLimitSnapshot
        {
            Primary = new() { UsedPercent = 10, WindowDurationMins = duration }, Secondary = valid
        };
        Assert.Same(valid, UsageWindowSelector.FindFiveHour(snapshot));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"status\":\"available\",\"expiresAt\":9223372036854775807}]")]
    public void OptionalMalformedCreditExpiryNeverCrashesQuotaRead(string credits)
    {
        var value = JsonSerializer.Deserialize<RateLimitResetCreditsSnapshot>(
            "{\"availableCount\":2,\"credits\":" + credits + "}")!;
        Assert.Null(value.FindNextExpiry());
    }

    [Fact]
    public void ValidCreditExpirySurvivesInvalidNeighbors()
    {
        var credits = JsonSerializer.Deserialize<RateLimitResetCreditsSnapshot>(
            "{\"credits\":[null,{\"status\":\"used\",\"expiresAt\":1},{\"status\":\"available\",\"expiresAt\":9223372036854775807},{\"status\":\"available\",\"expiresAt\":1794000000}]}")!;
        Assert.Equal(1794000000, credits.FindNextExpiry());
    }

    [Theory]
    [InlineData(0, 20, 5)]
    [InlineData(7201, 180, 15)]
    public void CachedPlannedWindowExpiresEvenWithoutFreshFeed(int overdueSeconds, int dynamicSeconds, int quotaSeconds)
    {
        var now = DateTimeOffset.Parse("2026-10-29T04:00:00Z");
        var end = now.AddSeconds(-overdueSeconds);
        var notice = new TiboSignal(TiboSignalKind.Planned, "notice", "detail", null, now.AddDays(-1), now)
        {
            ExpectedAt = end.AddHours(-1), ExpectedEndAt = end
        };
        var coordinator = new TiboStatusCoordinator();
        coordinator.OnSignal(notice, end.AddMinutes(-1));
        coordinator.OnSignal(new(TiboSignalKind.Unavailable, "offline", "offline", null, null, now), now);
        Assert.Equal(TiboSignalKind.ExpiredNotice, coordinator.Display?.Kind);
        Assert.Equal(end, coordinator.Display!.ExpectedEndAt);
        Assert.False(coordinator.TakeCelebration());
        var plan = TiboPollingPolicy.For(notice, now);
        Assert.Equal(TimeSpan.FromSeconds(dynamicSeconds), plan.DynamicInterval);
        Assert.Equal(TimeSpan.FromSeconds(quotaSeconds), plan.QuotaInterval);
    }

    [Fact]
    public void ExpiringAnnouncementDoesNotEraseAlreadyConfirmedAccountRecovery()
    {
        var now = DateTimeOffset.Parse("2026-10-29T04:00:00Z");
        var end = now.AddDays(6).ToUnixTimeSeconds();
        var coordinator = new TiboStatusCoordinator();
        coordinator.OnSnapshot(new() { Primary = new() { UsedPercent = 60, WindowDurationMins = 10080, ResetsAt = end } }, now);
        coordinator.OnSignal(new(TiboSignalKind.Planned, "notice", "detail", null, now.AddHours(-1), now)
        {
            ExpectedEndAt = now.AddMinutes(1)
        }, now);
        coordinator.OnSnapshot(new() { Primary = new() { UsedPercent = 0, WindowDurationMins = 10080, ResetsAt = end } }, now);
        Assert.True(coordinator.TakeCelebration());
        coordinator.Tick(now.AddMinutes(2));
        Assert.Equal(TiboSignalKind.Confirmed, coordinator.Display!.Kind);
        Assert.False(coordinator.TakeCelebration());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoveryRequiresSamplesFromTheSameConnection(bool sameConnection)
    {
        var now = DateTimeOffset.Parse("2026-10-29T04:00:00Z");
        var end = now.AddDays(6).ToUnixTimeSeconds();
        var session = Guid.NewGuid();
        var coordinator = new TiboStatusCoordinator();
        coordinator.OnSnapshot(new()
        {
            ConnectionSessionId = session,
            Primary = new() { UsedPercent = 60, WindowDurationMins = 10080, ResetsAt = end }
        }, now);
        coordinator.OnSnapshot(new()
        {
            ConnectionSessionId = sameConnection ? session : Guid.NewGuid(),
            Primary = new() { UsedPercent = 0, WindowDurationMins = 10080, ResetsAt = end }
        }, now.AddSeconds(15));
        Assert.Equal(sameConnection, coordinator.Display?.Kind == TiboSignalKind.Confirmed);
        Assert.Equal(sameConnection, coordinator.TakeCelebration());
    }

    [Fact]
    public void ConnectionMarkerIsNeverSerializedOrRestored()
    {
        var session = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new RateLimitSnapshot { ConnectionSessionId = session });
        Assert.DoesNotContain(session.ToString(), json);
        Assert.DoesNotContain("ConnectionSessionId", json);
        Assert.Null(JsonSerializer.Deserialize<RateLimitSnapshot>(json)!.ConnectionSessionId);
    }

    [Theory]
    [InlineData(long.MinValue, 10080)]
    [InlineData(long.MaxValue, 10080)]
    [InlineData(-62135596800, 10080)]
    [InlineData(253402300799, 10080)]
    [InlineData(1794000000, long.MaxValue)]
    public void AdviceDoesNotCrashOnInvalidClockOrDuration(long reset, long duration)
    {
        var advice = UsageAgent.Evaluate(new() { UsedPercent = 25, ResetsAt = reset, WindowDurationMins = duration }, null);
        Assert.Equal(75, advice.RemainingPercent);
        Assert.False(string.IsNullOrWhiteSpace(advice.Message));
    }
}
