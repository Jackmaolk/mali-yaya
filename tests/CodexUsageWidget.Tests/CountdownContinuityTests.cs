using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace CodexUsageWidget.Tests;

[Collection("WpfPresentation")]
public sealed class CountdownContinuityTests
{
    [Theory]
    [InlineData("zh-CN", "继续计时", "5小时重置")]
    [InlineData("en-US", "Counting", "5h reset")]
    public void RestoredClockSurvivesMissingTimestampAndNoticeUntilAccountSuppliesNewTime(
        string culture, string countingTitle, string resetTitle)
    {
        InSta(() =>
        {
            var window = new MainWindow(CultureInfo.GetCultureInfo(culture));
            var now = DateTimeOffset.UtcNow;
            var originalEnd = now.AddHours(3).ToUnixTimeSeconds();
            Invoke(window, "RestoreCountdownCheckpoint", new CountdownCheckpoint(originalEnd, now.AddMinutes(-20)));
            Assert.Equal(countingTitle, Find<TextBlock>(window, "PetCountdownTitle").Text);
            Assert.Equal(Visibility.Visible, Find<StackPanel>(window, "CountdownNumbers").Visibility);

            var coordinator = (TiboStatusCoordinator)Field(window, "_tiboStatus")!;
            coordinator.OnSignal(new TiboSignal(TiboSignalKind.Planned, "Tibo 预告重置", "预计稍后重置",
                null, now, now) { ExpectedEndAt = now.AddHours(1) }, now);
            Invoke(window, "UpdateResetLabels");
            Assert.Equal(originalEnd, ((RateLimitWindow)Field(window, "_countdownWindow")!).ResetsAt);

            Invoke(window, "ApplySnapshot", new RateLimitSnapshot
            {
                Primary = new RateLimitWindow { WindowDurationMins = 300, UsedPercent = 60 }
            });
            Assert.Equal(originalEnd, ((RateLimitWindow)Field(window, "_countdownWindow")!).ResetsAt);
            Assert.Equal(Visibility.Visible, Find<StackPanel>(window, "CountdownNumbers").Visibility);

            var nextEnd = now.AddHours(5).ToUnixTimeSeconds();
            Invoke(window, "ApplySnapshot", new RateLimitSnapshot
            {
                Primary = new RateLimitWindow { WindowDurationMins = 300, ResetsAt = nextEnd, UsedPercent = 0 }
            });
            Assert.Equal(nextEnd, ((RateLimitWindow)Field(window, "_countdownWindow")!).ResetsAt);
            Assert.Equal(resetTitle, Find<TextBlock>(window, "PetCountdownTitle").Text);

            Invoke(window, "ApplySnapshot", new RateLimitSnapshot
            {
                Primary = new RateLimitWindow { WindowDurationMins = 10080, UsedPercent = 20 }
            });
            Assert.Null(Field(window, "_countdownWindow"));
            Assert.Equal(Visibility.Collapsed, Find<Border>(window, "PetCountdownPanel").Visibility);
        });
    }

    [Theory]
    [InlineData("zh-CN", "到时待确认")]
    [InlineData("en-US", "Verifying")]
    public void ExpiredSavedClockKeepsZeroDigitsAndDoesNotInventAnotherFiveHours(string culture, string expiredTitle)
    {
        InSta(() =>
        {
            var window = new MainWindow(CultureInfo.GetCultureInfo(culture));
            var now = DateTimeOffset.UtcNow;
            var end = now.AddMinutes(-10).ToUnixTimeSeconds();
            Invoke(window, "RestoreCountdownCheckpoint", new CountdownCheckpoint(end, now.AddHours(-2)));
            Invoke(window, "UpdateResetLabels");
            Assert.Equal(Visibility.Visible, Find<StackPanel>(window, "CountdownNumbers").Visibility);
            Assert.Equal(expiredTitle, Find<TextBlock>(window, "PetCountdownTitle").Text);
            Assert.Equal("00", Find<FlipNumber>(window, "HoursFlip").Value);
            Assert.Equal("00", Find<FlipNumber>(window, "MinutesFlip").Value);
            Assert.Equal("00", Find<FlipNumber>(window, "SecondsFlip").Value);
            Assert.Equal(end, ((RateLimitWindow)Field(window, "_countdownWindow")!).ResetsAt);
        });
    }

    private static T Find<T>(MainWindow window, string name) => (T)window.FindName(name);
    private static object? Field(MainWindow window, string name) => typeof(MainWindow)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
    private static void Invoke(MainWindow window, string name, params object[] args) => typeof(MainWindow)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    private static void InSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Countdown continuity check timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
