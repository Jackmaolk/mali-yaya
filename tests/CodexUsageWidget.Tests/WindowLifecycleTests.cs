using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace CodexUsageWidget.Tests;

[Collection("WpfPresentation")]
public sealed class WindowLifecycleTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void ClosingBeforeConnectionStartsNeverReentersTheClosingEvent(string culture) => Sta(errors =>
    {
        var window = new MainWindow(CultureInfo.GetCultureInfo(culture), new(false, false), null);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        var timer = Field<DispatcherTimer>(window, "_countdownTimer");
        timer.Start();

        window.Close();
        Assert.False(closed);
        PumpUntil(() => closed || errors.Count > 0);

        Assert.Empty(errors);
        Assert.True(closed);
        Assert.False(timer.IsEnabled);
        Assert.True(Field<bool>(window, "_shutdownComplete"));
    });

    [Fact]
    public void RepeatedCloseWhileClientIsBusyFinishesExactlyOnce() => Sta(errors =>
    {
        var window = new MainWindow(CultureInfo.GetCultureInfo("en-US"), new(false, false), null);
        var client = Field<CodexRateLimitClient>(window, "_client");
        var gate = Field<SemaphoreSlim>(client, "_refreshLock");
        var closed = 0;
        window.Closed += (_, _) => closed++;
        gate.Wait();
        try
        {
            window.Close();
            window.Close();
            Assert.True(Field<bool>(window, "_stopping"));
            Assert.False(Field<bool>(window, "_shutdownComplete"));
            Assert.Equal(0, closed);
        }
        finally { gate.Release(); }
        PumpUntil(() => closed > 0 || errors.Count > 0);

        Assert.Empty(errors);
        Assert.Equal(1, closed);
        foreach (var name in new[] { "_countdownTimer", "_refreshTimer", "_tiboTimer", "_petTimer", "_updateTimer" })
            Assert.False(Field<DispatcherTimer>(window, name).IsEnabled);
    });

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void NativeWindowCanCloseImmediatelyAfterLoadingWithoutStartingServices(string culture) => Sta(errors =>
    {
        var window = new MainWindow(CultureInfo.GetCultureInfo(culture), new(false, false), null);
        var loaded = typeof(MainWindow).GetMethod("Window_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!;
        window.Loaded -= loaded.CreateDelegate<RoutedEventHandler>(window);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Loaded += (_, _) => window.Close();
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Left = window.Top = -20000;

        window.Show();
        PumpUntil(() => closed || errors.Count > 0);

        Assert.Empty(errors);
        Assert.True(closed);
        Assert.Null(Field<CodexRateLimitClient>(window, "_client").Installation);
    });

    [Fact]
    public void LoadedCallbackAfterShutdownCannotRestartTimersOrServices() => Sta(errors =>
    {
        var window = new MainWindow(CultureInfo.GetCultureInfo("en-US"), new(false, false), null);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Close();
        typeof(MainWindow).GetMethod("Window_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [window, new RoutedEventArgs()]);
        foreach (var name in new[] { "_countdownTimer", "_refreshTimer", "_tiboTimer", "_petTimer", "_updateTimer" })
            Assert.False(Field<DispatcherTimer>(window, name).IsEnabled);
        Assert.Null(Field<CodexRateLimitClient>(window, "_client").Installation);
        PumpUntil(() => closed || errors.Count > 0);
        Assert.Empty(errors);
        Assert.True(closed);
    });

    private static T Field<T>(object value, string name) => (T)value.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;

    private static void PumpUntil(Func<bool> completed)
    {
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        var probe = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timeout.Tick += (_, _) => frame.Continue = false;
        probe.Tick += (_, _) => { if (completed()) frame.Continue = false; };
        timeout.Start();
        probe.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timeout.Stop(); probe.Stop(); }
        Assert.True(completed(), "Window shutdown did not finish within the guard timeout.");
    }

    private static void Sta(Action<List<Exception>> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var errors = new List<Exception>();
            dispatcher.UnhandledException += (_, e) => { errors.Add(e.Exception); e.Handled = true; };
            try { action(errors); }
            catch (Exception error) { failure = error; }
            finally { dispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
