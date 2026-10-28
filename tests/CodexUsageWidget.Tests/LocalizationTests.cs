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
public sealed class LocalizationTests
{
    [Theory]
    [InlineData("zh-CN", true)]
    [InlineData("zh-TW", true)]
    [InlineData("zh-HK", true)]
    [InlineData("en-US", false)]
    [InlineData("en-GB", false)]
    [InlineData("fr-FR", false)]
    [InlineData("", false)]
    public void LanguageSelectionIsExplicitAndDoesNotMutateCulture(string name, bool chinese)
    {
        var before = CultureInfo.CurrentUICulture;
        var text = new UiText(CultureInfo.GetCultureInfo(name));
        Assert.Equal(chinese, text.IsChinese);
        Assert.Equal(chinese ? "码力芽芽" : "Mali Yaya", text.Get("码力芽芽"));
        Assert.Same(before, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public void CatalogHasEnglishForEveryOwnedSourceAndPreservesOpaqueData()
    {
        var english = new UiText(CultureInfo.GetCultureInfo("en-US"));
        var chinese = new UiText(CultureInfo.GetCultureInfo("zh-TW"));
        foreach (var pair in UiText.EnglishBySource)
        {
            Assert.Equal(pair.Key, chinese.Get(pair.Key));
            Assert.Equal(pair.Value, english.Get(pair.Key));
            Assert.DoesNotMatch("[\u3400-\u9fff]", pair.Value);
        }
        foreach (var value in new[] { "00:00:00", "04:59:32", "PLUS", "https://x.com/tibo/status/123", @"C:\用户\codex.exe", "Jackmaolk/mali-yaya" })
            Assert.Equal(value, english.Translate(value));
        Assert.Equal("Used 23%", english.Format("已用 {0}%", 23));
        Assert.Equal("Close", english.Get("关闭"));
        Assert.Equal("Off", english.UpdateState(false));
        Assert.Equal("On", english.UpdateState(true));
        Assert.Equal("关闭", chinese.UpdateState(false));
    }

    [Theory]
    [InlineData("正在自动识别 Codex CLI…", "Detecting Codex CLI...")]
    [InlineData("Codex 已连接", "Codex connected")]
    [InlineData("更新检查或校验失败，旧版本未改动", "Update check or verification failed; current version unchanged")]
    [InlineData("暂无可用的新版本", "No newer version available")]
    [InlineData("太好了 · 可重置1次", "Great! 1 reset available")]
    [InlineData("已有 3 次可用机会，需要时可以放心使用。", "3 resets available. Use them when needed.")]
    [InlineData("本轮周周期：10月28日 09:30 开始 · 重置已完成", "Weekly cycle: 10/28 09:30 start · reset complete")]
    public void PolicyAndServiceTextIsTranslatedOnlyAtTheBoundary(string source, string expected)
    {
        var text = new UiText(CultureInfo.GetCultureInfo("en-GB"));
        Assert.Equal(expected, text.Translate(source));
        Assert.Equal(source, new UiText(CultureInfo.GetCultureInfo("zh-CN")).Translate(source));
    }

    [Fact]
    public void DialogHelpersLocalizeChromeWithoutChangingPathsOrRepository()
    {
        var text = new UiText(CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal("Codex connection settings", text.Get("Codex 连接设置"));
        Assert.Equal("Mali Yaya update status", text.Get("码力芽芽更新状态"));
        Assert.Equal("Cannot save update settings.", text.Get("无法保存更新设置。"));
        Assert.Equal("CLI source: Desktop bundled CLI\nVersion: 1.2.3\nPath: C:\\用户\\codex.exe\nRight-click to detect or select CLI. Uses your existing local sign-in; no credentials are copied.",
            text.Format("CLI 来源：{0}\n版本：{1}\n路径：{2}\n右键可重新识别或选择 CLI；使用本机现有登录，不复制凭据。",
                text.Translate("Desktop 自带 CLI"), "1.2.3", @"C:\用户\codex.exe"));
        Assert.DoesNotMatch("[\u3400-\u9fff]", text.Translate(CodexRateLimitClient.DescribeConnectionError(new TimeoutException())));
    }

    [Theory]
    [InlineData("zh-CN", true)]
    [InlineData("zh-TW", true)]
    [InlineData("en-US", false)]
    [InlineData("en-GB", false)]
    [InlineData("fr-FR", false)]
    public void WindowUsesItsOwnLanguageAndTiboOriginalIsVerbatim(string culture, bool chinese) => OnSta(() =>
    {
        var before = CultureInfo.CurrentUICulture;
        var window = new MainWindow(CultureInfo.GetCultureInfo(culture));
        var other = new MainWindow(CultureInfo.GetCultureInfo(chinese ? "en-US" : "zh-CN"));
        Assert.Equal(chinese ? "码力芽芽" : "Mali Yaya", window.Title);
        Assert.NotEqual(window.Title, other.Title);
        Assert.Same(before, CultureInfo.CurrentUICulture);
        Assert.Equal(chinese ? "开机启动" : "Start at sign-in", Find<CheckBox>(window, "StartupCheckBox").Content);
        Assert.Equal(chinese ? "折叠/展开" : "Collapse/expand", Find<Button>(window, "CollapseButton").ToolTip);
        Assert.Equal(chinese ? "重新自动识别 Codex CLI" : "Detect Codex CLI again", ((MenuItem)window.ContextMenu.Items[0]).Header);
        var post = new TiboPost("Original  spaces\nand 中文 remain verbatim.", "https://x.com/tibo/status/123", DateTimeOffset.UtcNow);
        SetField(window, "_briefKey", TiboChineseBriefClient.CacheKey(post));
        SetField(window, "_briefRetryAt", DateTimeOffset.MaxValue);
        Invoke(window, "UpdateChineseBrief", post);
        Assert.Equal((chinese ? "原文：" : "Original: ") + post.Text, Find<TextBlock>(window, "TiboOriginalText").Text);
        if (!chinese)
        {
            Assert.Equal("Original: " + post.Text, Find<TextBlock>(window, "TiboOriginalText").Text);
            Assert.Equal(Visibility.Collapsed, Find<TextBlock>(window, "TiboChineseText").Visibility);
            Assert.Equal(post.Text + "\nOpen this original post", Find<TextBlock>(window, "TiboOriginalText").ToolTip);
        }
        Assert.Same(before, CultureInfo.CurrentUICulture);
    });

    [Theory]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    public void EnglishNormalWeeklyOnlyAndCollapsedLayoutsFit(string culture) => OnSta(() =>
    {
        var window = new MainWindow(CultureInfo.GetCultureInfo(culture));
        var snapshot = new RateLimitSnapshot
        {
            PlanType = "plus",
            Primary = new RateLimitWindow { UsedPercent = 0, WindowDurationMins = 300, ResetsAt = DateTimeOffset.UtcNow.AddHours(4).ToUnixTimeSeconds() },
            Secondary = new RateLimitWindow { UsedPercent = 8, WindowDurationMins = 10080, ResetsAt = DateTimeOffset.UtcNow.AddDays(5).ToUnixTimeSeconds() }
        };
        Invoke(window, "ApplySnapshot", snapshot);
        Layout(window, 294);
        Assert.Equal("PLUS", Find<TextBlock>(window, "PlanText").Text);
        Assert.Equal("Used 0%", Find<TextBlock>(window, "FiveHourUsedText").Text);
        Assert.Equal("100%", Find<TextBlock>(window, "FiveHourRemainingText").Text);
        Assert.Equal(Visibility.Visible, Find<Border>(window, "FiveHourCard").Visibility);
        foreach (var name in new[] { "PetMoodText", "PetCountdownTitle", "AgentTitleText", "FiveHourHintText", "WeeklyHintText", "ResetChanceText", "StatusText" })
            Assert.DoesNotMatch("[\u3400-\u9fff]", Find<TextBlock>(window, name).Text);
        foreach (var name in new[] { "BrandText", "PetMoodText", "PetCountdownTitle", "FiveHourLabel", "WeeklyLabel", "FiveHourRemainingText", "WeeklyRemainingText" })
            AssertTextFits(Find<TextBlock>(window, name));
        if (culture == "en-US")
        {
            var root = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 1.5),
                (int)Math.Ceiling(root.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(AppContext.BaseDirectory, "widget-preview-en.png"));
            encoder.Save(output);
        }
        var height = ((FrameworkElement)window.Content).ActualHeight;
        snapshot.Primary = null;
        Invoke(window, "ApplySnapshot", snapshot);
        Layout(window, 294);
        Assert.Equal(Visibility.Collapsed, Find<Border>(window, "FiveHourCard").Visibility);
        Assert.Equal(Visibility.Collapsed, Find<Border>(window, "PetCountdownPanel").Visibility);
        Assert.True(((FrameworkElement)window.Content).ActualHeight < height);
        Invoke(window, "Collapse_Click", null, new RoutedEventArgs());
        Layout(window, 142);
        Assert.Equal(142, window.Width);
        Assert.Equal(Visibility.Collapsed, Find<Grid>(window, "DetailsPanel").Visibility);
        AssertTextFits(Find<TextBlock>(window, "BrandText"));
        Invoke(window, "Collapse_Click", null, new RoutedEventArgs());
        Assert.Equal(294, window.Width);
        Assert.Equal(Visibility.Visible, Find<Grid>(window, "DetailsPanel").Visibility);
    });

    [Fact]
    public void EveryQuotaAdviceAndMoodHasAnEnglishDisplay() => OnSta(() =>
    {
        var window = new MainWindow(CultureInfo.GetCultureInfo("en-US"));
        var text = new UiText(CultureInfo.GetCultureInfo("en-US"));
        foreach (var remaining in new[] { 0, 1, 10, 20, 30, 40, 60, 80, 99 })
        {
            var weekly = new RateLimitWindow { UsedPercent = 100 - remaining, WindowDurationMins = 10080,
                ResetsAt = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds() };
            var advice = UsageAgent.Evaluate(weekly, null);
            Assert.DoesNotMatch("[\u3400-\u9fff]", text.Translate(advice.Title));
            Assert.DoesNotMatch("[\u3400-\u9fff]", text.Translate(advice.Message));
            Assert.DoesNotMatch("[\u3400-\u9fff]", text.Translate(UsageHintPolicy.ForFiveHour(remaining).Text));
            Assert.DoesNotMatch("[\u3400-\u9fff]", text.Translate(UsageHintPolicy.ForWeekly(remaining).Text));
            Invoke(window, "UpdatePetMood", new RateLimitSnapshot { Secondary = weekly });
            Assert.DoesNotMatch("[\u3400-\u9fff]", Find<TextBlock>(window, "PetMoodText").Text);
        }
        Invoke(window, "UpdateFlipCountdown", "04:59:32");
        Assert.Equal("04", Find<FlipNumber>(window, "HoursFlip").Value);
        Assert.Equal("59", Find<FlipNumber>(window, "MinutesFlip").Value);
        Assert.Equal("32", Find<FlipNumber>(window, "SecondsFlip").Value);
        Invoke(window, "UpdateFlipCountdown", "等待同步");
        Assert.Equal("Syncing", Find<TextBlock>(window, "PetCountdownStatus").Text);
        Invoke(window, "UpdateFlipCountdown", "暂无限额");
        Assert.Equal("No limit", Find<TextBlock>(window, "PetCountdownStatus").Text);
    });

    [Fact]
    public void TiboHeadlinesDetailsAndTooltipsStayEnglishWithOriginalSourceIntact() => OnSta(() =>
    {
        var window = new MainWindow(CultureInfo.GetCultureInfo("en-GB"));
        var now = DateTimeOffset.UtcNow;
        const string url = "https://x.com/thsottiaux/status/123456789012345";
        var post = new TiboPost("Original  text\nuntouched 中文", url, now);
        var states = new[]
        {
            new TiboSignal(TiboSignalKind.Planned, "Tibo 预告重置", "社区预计最晚北京时间 10月28日 12:00 前 · 正在加密监测", url, now, now),
            new TiboSignal(TiboSignalKind.ObservedReset, "社区观察到重置 · 核对账户中", "公开动态已更新，正在核对本账户实际额度", url, now, now),
            new TiboSignal(TiboSignalKind.Bonus, "Tibo 提到赠送重置机会", "是否到账请看下方实际重置次数 · 点此看原帖", url, now, now),
            new TiboSignal(TiboSignalKind.Confirmed, "本账户重置已完成 · 庆祝！", "本轮周周期：10月28日 09:30 开始 · 重置已完成", url, now, now),
            new TiboSignal(TiboSignalKind.ExpiredNotice, "预告参考时间已过，待确认", "请以账户实际额度为准 · 点此看原帖", url, now, now),
            new TiboSignal(TiboSignalKind.Unavailable, "Tibo 动态暂不可确认", "原帖镜像拒绝访问，稍后重试", null, null, now),
            new TiboSignal(TiboSignalKind.None, "暂无可核实的 Tibo 动态", "继续自动检查公开动态", null, null, now)
        };
        foreach (var signal in states)
        {
            var coordinator = new TiboStatusCoordinator();
            coordinator.OnSignal(signal, now);
            SetField(window, "_tiboStatus", coordinator);
            Invoke(window, "RenderTiboStatus");
            Assert.DoesNotMatch("[\u3400-\u9fff]", Find<TextBlock>(window, "TiboHeadlineText").Text);
            Assert.DoesNotMatch("[\u3400-\u9fff]", Find<TextBlock>(window, "TiboDetailText").Text);
            var tip = Assert.IsType<string>(Find<Border>(window, "TiboPanel").ToolTip);
            Assert.DoesNotMatch("[\u3400-\u9fff]", tip);
            Assert.Contains("Beijing UTC+8", tip);
            Assert.Equal(signal.SourceUrl is null ? Visibility.Collapsed : Visibility.Visible,
                Find<TextBlock>(window, "TiboOpenText").Visibility);
        }
        var recent = new TiboSignal(TiboSignalKind.RecentPost, "Tibo 最新已核实动态 · 10月28日 09:30", "原帖：" + post.Text, url, now, now) { LatestPost = post };
        var recentCoordinator = new TiboStatusCoordinator();
        recentCoordinator.OnSignal(recent, now);
        SetField(window, "_tiboStatus", recentCoordinator);
        Invoke(window, "RenderTiboStatus");
        Assert.Equal("Original: " + post.Text, Find<TextBlock>(window, "TiboOriginalText").Text);
        Assert.Equal(Visibility.Collapsed, Find<TextBlock>(window, "TiboChineseText").Visibility);
        Assert.Contains(post.Text, Assert.IsType<string>(Find<Border>(window, "TiboPanel").ToolTip));
        Assert.Same(recent, typeof(MainWindow).GetField("_tiboSignal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window));
    });

    [Fact]
    public void ResetDatesUseBeijingTimeAndEnglishUnits() => OnSta(() =>
    {
        var window = new MainWindow(CultureInfo.GetCultureInfo("fr-FR"));
        var reset = new DateTimeOffset(DateTimeOffset.UtcNow.Year + 1, 1, 3, 1, 2, 0, TimeSpan.Zero);
        var method = typeof(MainWindow).GetMethod("FormatWindowReset", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var value = Assert.IsType<string>(method.Invoke(window, new object[]
        {
            new RateLimitWindow { ResetsAt = reset.ToUnixTimeSeconds(), WindowDurationMins = 10080 }, false
        }));
        Assert.Contains("01/03 09:02", value);
        Assert.Contains("d", value);
        Assert.DoesNotMatch("[\u3400-\u9fff]", value);
        var text = new UiText(CultureInfo.GetCultureInfo("en-US"));
        var source = "请选择本机 Codex 的原生 .exe 文件，不能填写脚本、网络路径或命令参数。";
        Assert.Equal(text.Get(source) + " (Parameter 'executable')", text.ErrorMessage(source + " (Parameter 'executable')"));
    });

    private static void SetField(MainWindow window, string name, object value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    private static T Find<T>(MainWindow window, string name) => (T)window.FindName(name);
    private static void Invoke(MainWindow window, string method, params object?[] args) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);

    private static void Layout(MainWindow window, double width)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, width, root.DesiredSize.Height));
        root.UpdateLayout();
        Assert.InRange(root.ActualHeight, 1, 430);
    }

    private static void AssertTextFits(TextBlock text)
    {
        var width = new FormattedText(text.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
        Assert.True(width <= text.ActualWidth + 1, $"{text.Name}: '{text.Text}' needs {width}, has {text.ActualWidth} DIP.");
    }

    private static void OnSta(Action action)
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Offline WPF localization test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
