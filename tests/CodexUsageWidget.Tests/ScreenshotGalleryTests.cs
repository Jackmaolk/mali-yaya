using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace CodexUsageWidget.Tests;

[Collection("WpfPresentation")]
public sealed class ScreenshotGalleryTests
{
    [Theory]
    [InlineData("zh-CN", "normal")]
    [InlineData("en-US", "normal")]
    [InlineData("zh-CN", "planned")]
    [InlineData("en-US", "planned")]
    [InlineData("zh-CN", "confirmed")]
    [InlineData("en-US", "confirmed")]
    [InlineData("zh-CN", "low")]
    [InlineData("en-US", "low")]
    [InlineData("zh-CN", "minimal")]
    [InlineData("en-US", "minimal")]
    [InlineData("zh-CN", "offline")]
    [InlineData("en-US", "offline")]
    public void RenderOfflineSyntheticGallery(string culture, string state)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Render(culture, state); }
            catch (Exception error) { failure = error; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Render(string culture, string state)
    {
        var zh = culture.StartsWith("zh", StringComparison.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var window = new MainWindow(CultureInfo.GetCultureInfo(culture), new(false, state == "minimal"), null);
        var snapshot = new RateLimitSnapshot
        {
            PlanType = "plus", ResetOpportunityReported = true, ResetOpportunityCount = 2,
            Primary = new() { UsedPercent = state == "low" ? 92 : 26, WindowDurationMins = 300, ResetsAt = now.AddHours(4).ToUnixTimeSeconds() },
            Secondary = new() { UsedPercent = state == "low" ? 87 : 24, WindowDurationMins = 10080, ResetsAt = now.AddDays(6).ToUnixTimeSeconds() }
        };
        Invoke(window, "ApplySnapshot", snapshot);
        var url = "https://x.com/thsottiaux/status/2113492187770175603";
        var post = new TiboPost("Sample post: improvements to Codex are on the way.", url, now.AddHours(-1));
        TiboSignal? signal = state switch
        {
            "planned" => new(TiboSignalKind.Planned, "Tibo 预告重置", "社区预计最晚北京时间 10月29日 02:00 前 · 正在加密监测", url, now.AddHours(-1), now)
                { ExpectedAt = now.AddHours(2), ExpectedEndAt = now.AddHours(3) },
            "normal" or "low" => new(TiboSignalKind.RecentPost, "Tibo 最新已核实动态 · 10月28日 09:30", "原帖：" + post.Text, url, post.PostTime, now) { LatestPost = post },
            _ => null
        };
        if (signal is not null)
        {
            var coordinator = Field<TiboStatusCoordinator>(window, "_tiboStatus");
            coordinator.OnSignal(signal, now);
            Set(window, "_briefKey", TiboChineseBriefClient.CacheKey(post));
            Set(window, "_briefRetryAt", DateTimeOffset.MaxValue);
            Set(window, "_chineseBrief", new TiboChineseBrief("示例概要：Codex 即将迎来改进。本条仅用于界面展示，不代表真实预告。", "fixture"));
            Invoke(window, "RenderTiboStatus");
            Invoke(window, "UpdatePetMood", snapshot);
            if (zh && signal.LatestPost is not null)
                ((TextBlock)window.FindName("TiboChineseText")).Text = "中文概要：Codex 即将迎来改进。本条仅为模拟展示。";
        }
        if (state == "confirmed")
        {
            snapshot.Secondary = new() { UsedPercent = 0, WindowDurationMins = 10080, ResetsAt = now.AddDays(7).ToUnixTimeSeconds() };
            Invoke(window, "ApplySnapshot", snapshot);
            Assert.Equal(TiboSignalKind.Confirmed, Field<TiboStatusCoordinator>(window, "_tiboStatus").Display?.Kind);
        }
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(294, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, 294, root.DesiredSize.Height));
        root.UpdateLayout();
        Assert.InRange(root.ActualHeight, 1, 430);
        Assert.Null(Field<object?>(window, "_chineseBriefClient"));
        var visual = new DrawingVisual();
        var rendered = new RenderTargetBitmap(441, (int)Math.Ceiling(root.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        rendered.Render(root);
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(rendered, new Rect(0, 0, 294, root.ActualHeight));
            drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, root.ActualHeight, 294, 22));
            drawing.DrawText(new FormattedText(zh ? "模拟数据 · 离线界面预览" : "Synthetic data · Offline UI preview",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.DarkSlateGray, 1.5),
                new Point(8, root.ActualHeight + 4));
        }
        var bitmap = new RenderTargetBitmap(441, (int)Math.Ceiling((root.ActualHeight + 22) * 1.5), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "gallery");
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, $"widget-{state}-{(zh ? "zh" : "en")}.png"));
        encoder.Save(output);
    }

    private static T Field<T>(MainWindow window, string field) => (T)typeof(MainWindow).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
    private static void Set(MainWindow window, string field, object value) => typeof(MainWindow).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, value);
    private static void Invoke(MainWindow window, string method, params object[] args) => typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
}
