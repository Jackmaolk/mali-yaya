using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace CodexUsageWidget.Tests;

[Collection("WpfPresentation")]
public sealed class TiboPrivacyUiTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void DisabledMonitoringSendsNoRequestsWhileQuotaAndClockRefresh(string culture) => Sta(() =>
    {
        var handler = new CountingHandler();
        using var http = new HttpClient(handler);
        using var monitor = new TiboResetMonitor(http);
        var window = new MainWindow(CultureInfo.GetCultureInfo(culture), new(false, false), monitor);
        var check = (Task)Invoke(window, "CheckTiboAsync")!;
        Assert.True(check.IsCompletedSuccessfully);
        Assert.Equal(0, handler.Calls);
        Invoke(window, "ApplySnapshot", Snapshot());
        Assert.Equal("74%", Find<TextBlock>(window, "FiveHourRemainingText").Text);
        Assert.Equal(Visibility.Visible, Find<Border>(window, "PetCountdownPanel").Visibility);
        Assert.Null(typeof(MainWindow).GetField("_chineseBriefClient", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window));
        Assert.Equal(culture == "zh-CN" ? "Tibo 联网已关闭" : "Tibo networking off", Find<TextBlock>(window, "TiboHeadlineText").Text);
    });

    [Fact]
    public void OffDuringRetryCancelsAndDoesNotPublishLateUnavailableState() => Sta(() =>
    {
        var handler = new CountingHandler();
        using var http = new HttpClient(handler);
        using var monitor = new TiboResetMonitor(http);
        var window = new MainWindow(CultureInfo.GetCultureInfo("en-US"), new(true), monitor);
        var check = (Task)Invoke(window, "CheckTiboAsync")!;
        Assert.Equal(1, handler.Calls);
        Invoke(window, "SetTiboNetworking", false);
        check.GetAwaiter().GetResult();
        Assert.Equal(1, handler.Calls);
        Assert.Equal("Tibo networking off", Find<TextBlock>(window, "TiboHeadlineText").Text);
        Assert.Null(typeof(MainWindow).GetField("_tiboSignal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window));
    });

    [Fact]
    public void LateTransportCompletionAfterOffOnCannotPublishPreviousSession() => Sta(() =>
    {
        var handler = new LateHandler();
        using var http = new HttpClient(handler);
        using var monitor = new TiboResetMonitor(http);
        var window = new MainWindow(CultureInfo.GetCultureInfo("en-US"), new(true), monitor);
        var check = (Task)Invoke(window, "CheckTiboAsync")!;
        Assert.False(check.IsCompleted);
        Invoke(window, "SetTiboNetworking", false);
        Invoke(window, "SetTiboNetworking", true);
        handler.Complete();
        check.GetAwaiter().GetResult();
        Assert.Null(typeof(MainWindow).GetField("_tiboSignal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window));
        Invoke(window, "SetTiboNetworking", false);
    });

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void MinimalModeKeepsQuotaCountdownAndMascotWithoutBlankNewsSpace(string culture) => Sta(() =>
    {
        var window = new MainWindow(CultureInfo.GetCultureInfo(culture), new(false, true), null);
        Invoke(window, "ApplySnapshot", Snapshot());
        Assert.Equal(Visibility.Collapsed, Find<Border>(window, "TiboPanel").Visibility);
        Assert.Equal(Visibility.Collapsed, Find<Border>(window, "AgentPanel").Visibility);
        Assert.Equal(Visibility.Collapsed, Find<Grid>(window, "ResetInfoRow").Visibility);
        Assert.Equal(Visibility.Visible, Find<Border>(window, "FiveHourCard").Visibility);
        Assert.Equal(Visibility.Visible, Find<Border>(window, "WeeklyCard").Visibility);
        Assert.Equal(Visibility.Visible, Find<Border>(window, "PetCountdownPanel").Visibility);
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(294, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, 294, root.DesiredSize.Height));
        root.UpdateLayout();
        Assert.InRange(root.ActualHeight, 100, 240);
    });

    [Fact]
    public async Task MonitorDoesNotMutateOrDisposeInjectedTransport()
    {
        var handler = new CountingHandler();
        using var http = new HttpClient(handler);
        var monitor = new TiboResetMonitor(http);
        await monitor.CheckAsync();
        Assert.Empty(http.DefaultRequestHeaders);
        monitor.Dispose();
        using var reply = await http.GetAsync("https://example.invalid/");
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public void QueuedUpdateStatusCannotOverwriteDisabledOrNewerSessionStatus() => Sta(() =>
    {
        var window = new MainWindow(CultureInfo.GetCultureInfo("en-US"), new(false, false), null);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(MainWindow).GetField("_activeUpdateGeneration", flags)!.SetValue(window, 1);
        typeof(MainWindow).GetField("_updateGeneration", flags)!.SetValue(window, 1);
        var updates = (WidgetUpdateService)typeof(MainWindow).GetField("_updates", flags)!.GetValue(window)!;
        var observer = (Action<string>)typeof(WidgetUpdateService).GetField("StatusChanged", flags)!.GetValue(updates)!;
        observer("正在检查更新");
        typeof(MainWindow).GetField("_updateGeneration", flags)!.SetValue(window, 2);
        var menu = Find<MenuItem>(window, "UpdateStatusMenu");
        menu.Header = "Automatic updates off";
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        Assert.Equal("Automatic updates off", menu.Header);
    });

    [Theory]
    [InlineData("zh-CN", "预告参考时间已过，待确认")]
    [InlineData("en-US", "Announced time passed; unconfirmed")]
    public void LocalClockExpiresCachedAnnouncementWithoutAnyNetworkRequest(string culture, string heading) => Sta(() =>
    {
        var handler = new CountingHandler();
        using var http = new HttpClient(handler);
        using var monitor = new TiboResetMonitor(http);
        var window = new MainWindow(CultureInfo.GetCultureInfo(culture), new(false, false), monitor);
        Invoke(window, "ApplySnapshot", Snapshot());
        var coordinator = (TiboStatusCoordinator)typeof(MainWindow).GetField("_tiboStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var now = DateTimeOffset.UtcNow;
        coordinator.OnSignal(new(TiboSignalKind.Planned, "Tibo 预告重置", "北京时间待确认", null, now.AddHours(-1), now.AddMinutes(-2))
        {
            ExpectedAt = now.AddMinutes(-5), ExpectedEndAt = now.AddSeconds(-1)
        }, now.AddMinutes(-2));
        Assert.Equal(TiboSignalKind.Planned, coordinator.Display!.Kind);
        Invoke(window, "UpdateResetLabels");
        Assert.Equal(TiboSignalKind.ExpiredNotice, coordinator.Display.Kind);
        Assert.Equal(heading, Find<TextBlock>(window, "TiboHeadlineText").Text);
        Assert.Equal("74%", Find<TextBlock>(window, "FiveHourRemainingText").Text);
        Assert.False(coordinator.TakeCelebration());
        Assert.Equal(0, handler.Calls);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingClockTimestampReusesCacheOnlyWithinSameConnection(bool sameConnection) => Sta(() =>
    {
        var window = new MainWindow(CultureInfo.GetCultureInfo("en-US"), new(false, false), null);
        var first = Snapshot();
        first.ConnectionSessionId = Guid.NewGuid();
        Invoke(window, "ApplySnapshot", first);
        var next = Snapshot();
        next.ConnectionSessionId = sameConnection ? first.ConnectionSessionId : Guid.NewGuid();
        next.Primary!.ResetsAt = null;
        Invoke(window, "ApplySnapshot", next);
        var clock = typeof(MainWindow).GetField("_countdownWindow", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);
        Assert.Equal(sameConnection, clock is not null);
        if (!sameConnection)
            Assert.Equal(Visibility.Collapsed, Find<StackPanel>(window, "CountdownNumbers").Visibility);
    });

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(253402300799)]
    public void InvalidCreditExpiryKeepsCountInsteadOfCrashingChineseTimeFormatting(long expiry) => Sta(() =>
    {
        var window = new MainWindow(CultureInfo.GetCultureInfo("en-US"), new(false, false), null);
        var snapshot = Snapshot();
        snapshot.ResetOpportunityReported = true;
        snapshot.ResetOpportunityCount = 2;
        snapshot.ResetOpportunityExpiresAt = expiry;
        Invoke(window, "ApplySnapshot", snapshot);
        Assert.Equal(new UiText(CultureInfo.GetCultureInfo("en-US")).Format("可用 {0} 次", 2),
            Find<TextBlock>(window, "ResetChanceText").Text);
    });

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
        }
    }

    private sealed class LateHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> _reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete() => _reply.SetResult(new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => _reply.Task;
    }

    private static RateLimitSnapshot Snapshot() => new()
    {
        Primary = new() { UsedPercent = 26, WindowDurationMins = 300, ResetsAt = DateTimeOffset.UtcNow.AddHours(4).ToUnixTimeSeconds() },
        Secondary = new() { UsedPercent = 4, WindowDurationMins = 10080, ResetsAt = DateTimeOffset.UtcNow.AddDays(6).ToUnixTimeSeconds() }
    };
    private static T Find<T>(MainWindow window, string name) => (T)window.FindName(name);
    private static object? Invoke(MainWindow window, string name, params object[] arguments) => typeof(MainWindow)
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, arguments);
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); } catch (Exception error) { failure = error; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
