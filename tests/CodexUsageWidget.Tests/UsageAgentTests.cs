using CodexUsageWidget;
using System.Text.Json;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class UsageAgentTests
{
    [Fact]
    public void LayoutPolicyUsesCompactCommercialDimensions()
    {
        Assert.Equal(294, WidgetLayoutPolicy.ExpandedWidth);
        Assert.Equal(142, WidgetLayoutPolicy.CollapsedWidth);
        Assert.Equal(40, WidgetLayoutPolicy.CollapsedHeight);
        Assert.True(WidgetLayoutPolicy.ExpandedWidth <= 300);
        Assert.True(WidgetLayoutPolicy.CollapsedWidth < WidgetLayoutPolicy.ExpandedWidth);
    }

    [Fact]
    public void FindWeekly_UsesPrimaryWhenPrimaryIsSevenDays()
    {
        var weekly = Window(1, 10080);
        var snapshot = new RateLimitSnapshot { Primary = weekly, Secondary = null };

        Assert.Same(weekly, UsageWindowSelector.FindWeekly(snapshot));
    }

    [Fact]
    public void FindWeekly_UsesSecondaryWhenPrimaryIsFiveHours()
    {
        var shortWindow = Window(20, 300);
        var weekly = Window(30, 10080);
        var snapshot = new RateLimitSnapshot { Primary = shortWindow, Secondary = weekly };

        Assert.Same(weekly, UsageWindowSelector.FindWeekly(snapshot));
    }

    [Fact]
    public void FindWeekly_DoesNotMislabelFiveHourWindow()
    {
        var snapshot = new RateLimitSnapshot { Primary = Window(20, 300) };

        Assert.Null(UsageWindowSelector.FindWeekly(snapshot));
    }

    [Fact]
    public void FindFiveHour_DetectsWindowInEitherSlot()
    {
        var fiveHour = Window(20, 300);
        var snapshot = new RateLimitSnapshot { Primary = Window(30, 10080), Secondary = fiveHour };

        Assert.Same(fiveHour, UsageWindowSelector.FindFiveHour(snapshot));
    }

    [Fact]
    public void RateLimitResponse_ParsesOfficialResetCredits()
    {
        const string json = """
            {
              "rateLimits": { "primary": { "usedPercent": 4, "windowDurationMins": 10080 } },
              "rateLimitResetCredits": {
                "availableCount": 1,
                "credits": [
                  { "status": "available", "expiresAt": 1786558085, "title": "Full reset" }
                ]
              }
            }
            """;

        var response = JsonSerializer.Deserialize<RateLimitResponse>(json);

        Assert.NotNull(response?.RateLimits);
        Assert.Equal(1, response!.RateLimitResetCredits?.AvailableCount);
        Assert.Equal(1786558085, response.RateLimitResetCredits?.FindNextExpiry());
    }

    [Fact]
    public void Evaluate_ReportsHealthyPaceForFreshWeeklyWindow()
    {
        var now = new DateTimeOffset(2026, 7, 13, 20, 0, 0, TimeSpan.FromHours(8));
        var weekly = Window(1, 10080, now.AddDays(7));

        var advice = UsageAgent.Evaluate(weekly, null, now);

        Assert.Equal(UsageAgentLevel.Healthy, advice.Level);
        Assert.Equal(99, advice.RemainingPercent);
        Assert.False(advice.IsPaceFast);
    }

    [Fact]
    public void Evaluate_DetectsFastConsumptionAgainstElapsedTime()
    {
        var now = new DateTimeOffset(2026, 7, 13, 20, 0, 0, TimeSpan.FromHours(8));
        var weekly = Window(40, 10080, now.AddDays(6));

        var advice = UsageAgent.Evaluate(weekly, null, now);

        Assert.Equal(UsageAgentLevel.Caution, advice.Level);
        Assert.True(advice.IsPaceFast);
    }

    [Fact]
    public void Evaluate_ResetOpportunityOverridesNormalAdvice()
    {
        var weekly = Window(90, 10080);

        var advice = UsageAgent.Evaluate(weekly, 2);

        Assert.Equal(UsageAgentLevel.Celebration, advice.Level);
        Assert.Contains("2 次", advice.Message);
    }

    private static RateLimitWindow Window(int used, long minutes, DateTimeOffset? reset = null) => new()
    {
        UsedPercent = used,
        WindowDurationMins = minutes,
        ResetsAt = (reset ?? DateTimeOffset.Now.AddDays(7)).ToUnixTimeSeconds()
    };
}
