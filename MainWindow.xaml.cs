using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CodexUsageWidget;

public partial class MainWindow : Window
{
    private const double ExpandedWidth = WidgetLayoutPolicy.ExpandedWidth;
    private const double CollapsedWidth = WidgetLayoutPolicy.CollapsedWidth;
    private const double CollapsedHeight = WidgetLayoutPolicy.CollapsedHeight;
    private readonly UiText _text;
    private readonly CodexRateLimitClient _client = new(CodexConnectionSettings.LoadPath());
    private readonly TiboResetMonitor _tiboMonitor;
    private TiboChineseBriefClient? _chineseBriefClient;
    private readonly TiboNetworkGate _tiboNetwork = new();
    private WidgetPreferences _preferences;
    private readonly WidgetUpdateService _updates = new();
    private WidgetUpdateOptions _updateOptions = WidgetUpdateSettings.Load();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    private CancellationTokenSource? _updateCancellation;
    private bool _updateChecking;
    private int _updateGeneration;
    private int _activeUpdateGeneration;
    private TiboStatusCoordinator _tiboStatus = new();
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _tiboTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _petTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _tiboChecking;
    private bool _quotaRefreshing;
    private bool _changingConnection;
    private long? _expiredFiveHourRefresh;
    private string? _briefKey;
    private TiboChineseBrief? _chineseBrief;
    private bool _briefPending;
    private DateTimeOffset _briefRetryAt;
    private CancellationTokenSource? _briefCancellation;
    private readonly CancellationTokenSource _lifetime = new();
    private TiboPollingPlan? _pollingPlan;
    private DateTimeOffset _nextTiboCheckAt = DateTimeOffset.MaxValue;
    private int _tiboFailures;
    private bool _stopping;
    private TiboSignal? _tiboSignal;
    private RateLimitSnapshot? _snapshot;
    private RateLimitWindow? _countdownWindow;
    private DateTimeOffset? _countdownConfirmedAt;
    private string _countdownText = "等待同步";
    private bool _collapsed;
    private bool _settingStartup;
    private bool _shutdownComplete;

    public MainWindow() : this(null) { }

    public MainWindow(CultureInfo? culture) : this(culture, WidgetPreferences.Load(), null) { }

    internal MainWindow(CultureInfo? culture, WidgetPreferences preferences, TiboResetMonitor? monitor)
    {
        _preferences = preferences;
        _tiboMonitor = monitor ?? new TiboResetMonitor();
        _tiboNetwork.SetEnabled(preferences.TiboEnabled);
        _text = new UiText(culture);
        InitializeComponent();
        foreach (var (key, source) in UiText.XamlSources)
        {
            Resources[key] = _text.Get(source);
            ContextMenu.Resources[key] = _text.Get(source);
        }
        AutoUpdateMenu.IsChecked = _updateOptions.Enabled;
        TiboNetworkMenu.IsChecked = _preferences.TiboEnabled;
        CompactModeMenu.IsChecked = _preferences.CompactMode;
        ApplyCompactMode();
        RenderTiboStatus();
        UpdateStatusMenu.Header = _updateOptions.Enabled ? _text.Get("等待更新检查") : _text.Get("自动更新已关闭");
        _updates.StatusChanged += status =>
        {
            var generation = _activeUpdateGeneration;
            Dispatcher.BeginInvoke(() =>
            {
                if (!_stopping && _updateOptions.Enabled && generation == _updateGeneration)
                    UpdateStatusMenu.Header = _text.Translate(status);
            });
        };
        _updateTimer.Tick += async (_, _) => await CheckWidgetUpdateAsync();
        _client.SnapshotUpdated += snapshot => Dispatcher.Invoke(() => ApplySnapshot(snapshot));
        _client.ConnectionStatusChanged += status => Dispatcher.Invoke(() =>
        {
            if (!_stopping)
            {
                StatusText.Text = _text.Translate(status);
                StatusText.ToolTip = _text.Translate(status) + "\n" + ConnectionDescription();
            }
        });
        _countdownTimer.Tick += (_, _) => UpdateResetLabels();
        _refreshTimer.Tick += async (_, _) => await ConnectAndRefreshAsync();
        _tiboTimer.Tick += async (_, _) =>
        {
            if (_stopping || _tiboChecking || !_tiboNetwork.Enabled) return;
            UpdateTiboSchedule();
            if (DateTimeOffset.UtcNow >= _nextTiboCheckAt) await CheckTiboAsync();
        };
        _petTimer.Tick += (_, _) => PlayIdleAnimation();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_stopping) return;
        RestoreCountdownCheckpoint(WidgetRuntimeStatus.ReadCountdownCheckpoint());
        WriteRuntimeStatus();
        PositionTopRight();
        _settingStartup = true;
        StartupCheckBox.IsChecked = StartupManager.IsEnabled();
        _settingStartup = false;
        _countdownTimer.Start();
        _refreshTimer.Start();
        if (_tiboNetwork.Enabled) _tiboTimer.Start();
        _petTimer.Start();
        _updateTimer.Start();
        StartGlassSheen();
        await Task.WhenAll(ConnectAndRefreshAsync(), CheckTiboAsync(), CheckWidgetUpdateAsync());
    }

    private async Task CheckWidgetUpdateAsync()
    {
        if (_stopping || _updateChecking || !_updateOptions.Enabled) return;
        _updateChecking = true;
        _activeUpdateGeneration = _updateGeneration;
        var generation = _updateGeneration;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _updateCancellation = cancellation;
        var options = _updateOptions;
        try
        {
            var prepared = await _updates.CheckAsync(options, cancellation.Token);
            if (prepared is null || _stopping || cancellation.IsCancellationRequested ||
                generation != _updateGeneration || options != _updateOptions) return;
            UpdateStatusMenu.Header = _text.Get("更新已校验，准备切换组件…");
            using var handoff = await WidgetUpdateInstaller.LaunchAsync(prepared, cancellation.Token);
            if (handoff is not null && !_stopping && !cancellation.IsCancellationRequested &&
                generation == _updateGeneration && options == _updateOptions && _updateOptions.Enabled && handoff.TryCommit()) Close();
            else if (!_stopping && _updateOptions.Enabled && generation == _updateGeneration)
                UpdateStatusMenu.Header = _text.Get("更新未能切换，继续使用当前版本");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            if (!_stopping && _updateOptions.Enabled && generation == _updateGeneration)
                UpdateStatusMenu.Header = _text.Get("更新未能完成，继续使用当前版本");
        }
        finally { _updateCancellation = null; _updateChecking = false; }
    }

    private async void AutoUpdate_Click(object sender, RoutedEventArgs e)
    {
        var enabled = AutoUpdateMenu.IsChecked;
        if (enabled && !WidgetUpdateSettings.IsOwnedRepository(_updateOptions.Repository))
        {
            AutoUpdateMenu.IsChecked = false;
            MessageBox.Show(this, _text.Get("请先配置本软件的具体更新仓库。当前没有默认更新源，不会下载参考项目的程序。"), _text.Get("自动更新"));
            return;
        }
        try
        {
            var next = _updateOptions with { Enabled = enabled };
            WidgetUpdateSettings.Save(next);
            _updateGeneration++;
            _updateCancellation?.Cancel();
            _updateOptions = next;
            UpdateStatusMenu.Header = enabled ? _text.Get("等待更新检查") : _text.Get("自动更新已关闭");
            if (enabled) await CheckWidgetUpdateAsync();
        }
        catch (Exception ex) when (WidgetDataPaths.IsStorageError(ex))
        {
            AutoUpdateMenu.IsChecked = _updateOptions.Enabled;
            MessageBox.Show(this, _text.Get("无法保存更新设置，未改变原设置。"), _text.Get("自动更新"));
        }
    }

    private void ConfigureUpdate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window { Title = _text.Get("配置码力芽芽更新仓库"), Width = 390, MinHeight = 185, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = _text.Get("填写具体仓库（owner/repo），不要填写 Token。"), TextWrapping = TextWrapping.Wrap });
        var input = new TextBox { Text = _updateOptions.Repository ?? "", Margin = new Thickness(0, 10, 0, 8) };
        input.ToolTip = _text.Get("例如 Jackmaolk/mali-yaya；建议名不代表仓库已创建。需要公开 Release，无凭据访问。");
        panel.Children.Add(input);
        var save = new Button { Content = _text.Get("保存并保持自动更新关闭"), Padding = new Thickness(8, 4, 8, 4) };
        save.Click += (_, _) =>
        {
            var repository = input.Text.Trim();
            if (!WidgetUpdateSettings.IsOwnedRepository(repository))
            {
                MessageBox.Show(dialog, _text.Get("请填写 Jackmaolk/具体仓库名，不能是个人主页或其他软件仓库。"), _text.Get("仓库格式不正确"));
                return;
            }
            try
            {
                var next = new WidgetUpdateOptions(false, repository);
                WidgetUpdateSettings.Save(next);
                _updateGeneration++;
                _updateCancellation?.Cancel();
                _updateOptions = next;
                AutoUpdateMenu.IsChecked = false;
                UpdateStatusMenu.Header = _text.Get("更新仓库已保存，自动更新已关闭");
                dialog.DialogResult = true;
            }
            catch (Exception ex) when (WidgetDataPaths.IsStorageError(ex))
            { MessageBox.Show(dialog, _text.Get("无法保存更新设置。"), _text.Get("自动更新")); }
        };
        panel.Children.Add(save);
        dialog.Content = panel;
        dialog.ShowDialog();
    }

    private void UpdateInfo_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        _text.Format("当前版本：{0}\n更新仓库：{1}\n", WidgetRuntimeStatus.BuildVersion, _updateOptions.Repository ?? _text.Get("未配置")) +
        _text.Format("自动更新：{0}\n{1}\n", _text.UpdateState(_updateOptions.Enabled), _text.Translate(_updates.State.Status)) +
        _text.Get("开启后每 24 小时最多尝试一次；失败不绕过间隔。私有仓库不支持无凭据自动更新。\n") +
        _text.Get("只切换本组件，不重启 Codex。"), _text.Get("码力芽芽更新状态"));

    private async Task CheckTiboAsync()
    {
        if (_tiboChecking || _stopping || !_tiboNetwork.TryCapture(out var token, out var generation)) return;
        _tiboChecking = true;
        try
        {
            var signal = await _tiboMonitor.CheckAsync(token);
            if (!_tiboNetwork.IsCurrent(generation) || _stopping) return;
            if (signal.Kind == TiboSignalKind.Unavailable || signal.VerificationDegraded)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                signal = await _tiboMonitor.CheckAsync(token);
            }
            if (_stopping || !_tiboNetwork.IsCurrent(generation)) return;
            _tiboFailures = signal.Kind == TiboSignalKind.Unavailable || signal.VerificationDegraded
                ? Math.Min(16, _tiboFailures + 1) : 0;
            _tiboStatus.OnSignal(signal, DateTimeOffset.Now);
            RenderTiboStatus();
            if (_snapshot is not null)
            {
                ApplyAgentAdvice(CurrentAgentAdvice(_snapshot));
                UpdatePetMood(_snapshot);
            }
            UpdateTiboSchedule(completed: true);
            WriteRuntimeStatus();
        }
        catch (OperationCanceledException) when (_stopping || token.IsCancellationRequested) { }
        finally { _tiboChecking = false; }
    }

    private void UpdateTiboSchedule(bool completed = false)
    {
        if (!_tiboNetwork.Enabled)
        {
            _nextTiboCheckAt = DateTimeOffset.MaxValue;
            _refreshTimer.Interval = TimeSpan.FromSeconds(15);
            return;
        }
        var now = DateTimeOffset.UtcNow;
        var plan = TiboPollingPolicy.For(_tiboStatus.Display, now, _tiboFailures);
        if (completed || plan != _pollingPlan)
        {
            var due = now + TiboPollingPolicy.WithJitter(plan.DynamicInterval, Random.Shared.NextDouble());
            // Snapshot updates can accelerate a scheduled check, but cannot postpone it repeatedly.
            if (completed || due < _nextTiboCheckAt) _nextTiboCheckAt = due;
            _pollingPlan = plan;
        }
        if (_refreshTimer.Interval != plan.QuotaInterval) _refreshTimer.Interval = plan.QuotaInterval;
        UpdateTiboToolTip();
    }

    private void UpdateTiboToolTip()
    {
        var signal = _tiboStatus.Display;
        if (signal is null) return;
        var next = _nextTiboCheckAt == DateTimeOffset.MaxValue ? _text.Get("正在安排") :
            _nextTiboCheckAt.ToOffset(TimeSpan.FromHours(8)).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var health = _tiboFailures > 0 ? _text.Get("动态查询波动，自动退避重试") : _text.Get("自动错峰监测");
        TiboPanel.ToolTip = _text.Format("{0}\n原文：{1}\n", TiboDisplayDetail(signal), signal.LatestPost?.Text) +
            (!_text.IsChinese || _chineseBrief is null ? "" : _text.Format("中文机译：{0}\n", _chineseBrief.Text)) +
            _text.Format("源更新（北京时间）：{0:M月d日 HH:mm:ss}\n", signal.CheckedAt.ToOffset(TimeSpan.FromHours(8))) +
            _text.Format("{0} · 下次检查：{1}\n", health, next) +
            _text.Format("账户刷新：{0:0} 秒", _refreshTimer.Interval.TotalSeconds) +
            (signal.SourceUrl is null ? "" : _text.Get("\n点击打开 Tibo 原帖"));
    }

    private string TiboDisplayDetail(TiboSignal signal) =>
        signal.Kind == TiboSignalKind.RecentPost && signal.LatestPost is not null
            ? _text.Get("原文：") + signal.LatestPost.Text
            : _text.Translate(signal.Detail);

    private void RenderTiboStatus()
    {
            var signal = _tiboStatus.Display;
            if (signal is null)
            {
                if (!_tiboNetwork.Enabled)
                {
                    TiboHeadlineText.Text = _text.Get("Tibo 联网已关闭");
                    TiboDetailText.Text = _text.Get("右键开启动态监测；额度和倒计时照常刷新。");
                    TiboDetailText.Visibility = Visibility.Visible;
                    TiboOriginalText.Visibility = TiboChineseText.Visibility = TiboOpenText.Visibility = Visibility.Collapsed;
                    TiboPanel.ToolTip = TiboDetailText.Text;
                }
                return;
            }
            var newAnnouncement = signal.Kind is TiboSignalKind.Planned or TiboSignalKind.Bonus or TiboSignalKind.Confirmed &&
                                  (_tiboSignal?.Kind != signal.Kind || _tiboSignal.SourceUrl != signal.SourceUrl);
            _tiboSignal = signal;
            TiboHeadlineText.Text = _text.Translate(signal.Headline);
            TiboDetailText.Text = TiboDisplayDetail(signal);
            TiboDetailText.Visibility = signal.Kind == TiboSignalKind.RecentPost && signal.LatestPost is not null
                ? Visibility.Collapsed : Visibility.Visible;
            UpdateChineseBrief(signal.LatestPost);
            TiboOpenText.Visibility = signal.SourceUrl is null ? Visibility.Collapsed : Visibility.Visible;
            UpdateTiboToolTip();
            TiboPanel.BorderBrush = new SolidColorBrush(signal.Kind switch
            {
                TiboSignalKind.Planned => Color.FromRgb(226, 150, 34),
                TiboSignalKind.ObservedReset => Color.FromRgb(54, 132, 184),
                TiboSignalKind.Confirmed => Color.FromRgb(30, 158, 103),
                TiboSignalKind.Bonus => Color.FromRgb(32, 155, 107),
                TiboSignalKind.Unavailable => Color.FromRgb(145, 166, 175),
                _ => Color.FromRgb(90, 159, 197)
            });
            TiboSignalShell.Background = new SolidColorBrush(signal.Kind switch
            {
                TiboSignalKind.Bonus or TiboSignalKind.Confirmed => Color.FromRgb(24, 149, 106),
                TiboSignalKind.Unavailable => Color.FromRgb(116, 143, 155),
                _ => Color.FromRgb(22, 141, 196)
            });
            TiboHeadlineText.Foreground = new SolidColorBrush(signal.Kind switch
            {
                TiboSignalKind.Planned => Color.FromRgb(118, 70, 14),
                TiboSignalKind.Bonus or TiboSignalKind.Confirmed => Color.FromRgb(20, 94, 66),
                _ => Color.FromRgb(23, 56, 76)
            });
            if (newAnnouncement && !_collapsed && !_stopping && SystemParameters.ClientAreaAnimation)
            {
                TiboSignalRotate.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(-13, 13, TimeSpan.FromMilliseconds(210))
                    {
                        AutoReverse = true,
                        RepeatBehavior = new RepeatBehavior(3)
                    });
            }
    }

    private void TiboPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_tiboNetwork.Enabled || _tiboSignal?.SourceUrl is not { } url) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void LatestTiboPost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_tiboNetwork.Enabled && _tiboStatus.Display?.LatestPost is { } post)
            Process.Start(new ProcessStartInfo(post.SourceUrl) { UseShellExecute = true });
    }

    private void UpdateChineseBrief(TiboPost? post)
    {
        if (_stopping) return;
        if (post is null || string.IsNullOrWhiteSpace(post.Text))
        {
            _briefCancellation?.Cancel();
            _briefKey = null;
            _chineseBrief = null;
            TiboOriginalText.Visibility = Visibility.Collapsed;
            TiboChineseText.Visibility = Visibility.Collapsed;
            return;
        }

        TiboOriginalText.Text = _text.Get("原文：") + post.Text;
        TiboOriginalText.ToolTip = _text.Format("{0}\n点击查看本条原帖", post.Text);
        TiboOriginalText.Visibility = Visibility.Visible;
        if (!_text.IsChinese)
        {
            TiboChineseText.Visibility = Visibility.Collapsed;
            return;
        }
        TiboChineseText.Visibility = Visibility.Visible;
        var key = TiboChineseBriefClient.CacheKey(post);
        if (_briefKey != key)
        {
            _briefCancellation?.Cancel();
            _briefKey = key;
            _chineseBrief = null;
            _briefPending = false;
            _briefRetryAt = DateTimeOffset.MinValue;
            TiboChineseText.Text = _text.Get("中文：正在获取译文…");
            TiboChineseText.ToolTip = _text.Get("中文机译节选，完整译文获取后可悬停查看");
        }
        if (_briefPending || DateTimeOffset.UtcNow < _briefRetryAt || !_tiboNetwork.TryCapture(out var sessionToken, out _)) return;
        _briefCancellation?.Dispose();
        _briefCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, sessionToken);
        _briefPending = true;
        _briefRetryAt = DateTimeOffset.UtcNow.AddMinutes(2);
        _ = FetchChineseBriefAsync(post, key, _briefCancellation.Token);
    }

    private async Task FetchChineseBriefAsync(TiboPost post, string key, CancellationToken cancellationToken)
    {
        try
        {
            if (!_tiboNetwork.Enabled || cancellationToken.IsCancellationRequested) return;
            _chineseBriefClient ??= new TiboChineseBriefClient();
            var brief = await _chineseBriefClient.GetAsync(post, cancellationToken);
            // A slow reply must never attach the previous post's translation to a new post.
            if (_stopping || !_tiboNetwork.Enabled || cancellationToken.IsCancellationRequested || _briefKey != key) return;
            _chineseBrief = brief;
            TiboChineseText.Text = brief is null ? _text.Get("中文：暂时无法获取，稍后自动重试") :
                _text.Get("中文：") + string.Join(" ", brief.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            TiboChineseText.ToolTip = brief is null ? _text.Get("原文仍可查看；翻译失败不影响额度和重置监测") :
                _text.Format("中文机译全文（{0}）：\n{1}\n仅供阅读，重置判断以原文与账户数据为准", brief.Provider, brief.Text);
            _briefRetryAt = brief is not null ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddMinutes(2);
            UpdateTiboToolTip();
            WriteRuntimeStatus();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            if (!_stopping && !cancellationToken.IsCancellationRequested && _briefKey == key)
            {
                TiboChineseText.Text = _text.Get("中文：暂时无法获取，稍后自动重试");
                _briefRetryAt = DateTimeOffset.UtcNow.AddMinutes(2);
            }
        }
        finally
        {
            if (_briefKey == key && !cancellationToken.IsCancellationRequested) _briefPending = false;
        }
    }

    private void PositionTopRight()
    {
        var area = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        Left = area.Right - width - 16;
        Top = area.Top + 18;
    }

    private void StartGlassSheen()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        GlassSheenTransform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(-100, ExpandedWidth + 70, TimeSpan.FromSeconds(9))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = false
            });
    }

    private async Task ConnectAndRefreshAsync()
    {
        if (_stopping || _quotaRefreshing || _changingConnection) return;
        _quotaRefreshing = true;
        try
        {
            RefreshButton.IsEnabled = false;
            await _client.StartAsync();
            await _client.RefreshAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            if (!_stopping)
            {
                StatusText.Text = _text.Translate(CodexRateLimitClient.DescribeConnectionError(ex));
                StatusText.ToolTip = StatusText.Text + "\n" + ConnectionDescription();
            }
        }
        finally
        {
            _quotaRefreshing = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void ApplySnapshot(RateLimitSnapshot snapshot)
    {
        if (_stopping) return;
        if (_snapshot?.ConnectionSessionId is { } previous && snapshot.ConnectionSessionId != previous)
        {
            _countdownWindow = null;
            _countdownConfirmedAt = null;
            _expiredFiveHourRefresh = null;
        }
        _tiboStatus.OnSnapshot(snapshot, DateTimeOffset.Now);
        _snapshot = snapshot;
        PlanText.Text = snapshot.PlanType?.ToUpperInvariant() ?? "--";
        var fiveHour = UsageWindowSelector.FindFiveHour(snapshot);
        RememberCountdownWindow(fiveHour, DateTimeOffset.UtcNow);
        var weekly = UsageWindowSelector.FindWeekly(snapshot);
        ApplyUsageWindow(
            fiveHour, FiveHourProgress, FiveHourRemainingText, FiveHourUsedText, FiveHourHintText,
            UsageHintPolicy.ForFiveHour, Color.FromRgb(21, 150, 106));
        ApplyUsageWindow(
            weekly, WeeklyProgress, WeeklyRemainingText, WeeklyUsedText, WeeklyHintText,
            UsageHintPolicy.ForWeekly, Color.FromRgb(23, 111, 179));
        UpdateCapabilityLayout(fiveHour is not null, weekly is not null);
        CreditsText.Text = snapshot.Credits switch
        {
            { Unlimited: true } => _text.Get("无限"),
            { HasCredits: true } credits => credits.Balance ?? _text.Get("可用"),
            { Balance: not null } credits => credits.Balance,
            _ => _text.Get("无")
        };

        var resetHint = UsageHintPolicy.ForResetOpportunity(snapshot.ResetOpportunityCount);
        ResetChanceText.Text = FormatResetOpportunity(snapshot);
        ResetChanceText.Foreground = new SolidColorBrush(resetHint.Color);
        RenderTiboStatus();
        ApplyAgentAdvice(CurrentAgentAdvice(snapshot));
        UpdatePetMood(snapshot);
        UpdateTiboSchedule();
        UpdateResetLabels();
        SetDetectionStatus();
    }

    private void ApplyUsageWindow(
        RateLimitWindow? window,
        ProgressBar progress,
        TextBlock remainingText,
        TextBlock usedText,
        TextBlock hintText,
        Func<int, UsageHint> hintFactory,
        Color normalColor)
    {
        if (window is null)
        {
            progress.Value = 0;
            remainingText.Text = "--%";
            usedText.Text = _text.Get("已用 --%");
            hintText.Text = _text.Get("等待数据");
            return;
        }

        var remaining = Math.Clamp(100 - window.UsedPercent, 0, 100);
        progress.Value = remaining;
        remainingText.Text = $"{remaining}%";
        usedText.Text = _text.Format("已用 {0}%", window.UsedPercent);
        var color = remaining <= 15
            ? Color.FromRgb(192, 58, 69)
            : remaining <= 30 ? Color.FromRgb(224, 149, 32) : normalColor;
        progress.Foreground = new SolidColorBrush(color);
        remainingText.Foreground = new SolidColorBrush(color);
        var hint = hintFactory(remaining);
        hintText.Text = _text.Translate(hint.Text);
        hintText.Foreground = new SolidColorBrush(hint.Color);
    }

    private void UpdateCapabilityLayout(bool hasFiveHour, bool hasWeekly)
    {
        PetCountdownPanel.Visibility = hasFiveHour ? Visibility.Visible : Visibility.Collapsed;
        FiveHourCard.Visibility = hasFiveHour ? Visibility.Visible : Visibility.Collapsed;
        WeeklyCard.Visibility = hasWeekly ? Visibility.Visible : Visibility.Collapsed;
        NoLimitsCard.Visibility = hasFiveHour || hasWeekly ? Visibility.Collapsed : Visibility.Visible;
        ApplyCompactMode();
        if (!_collapsed)
        {
            Width = ExpandedWidth;
            SizeToContent = SizeToContent.Height;
            Height = double.NaN;
            Dispatcher.BeginInvoke(PositionTopRight, DispatcherPriority.Loaded);
        }
    }

    private void ApplyAgentAdvice(UsageAgentAdvice advice)
    {
        AgentTitleText.Text = _text.Format("芽芽智能体 · {0}", _text.Translate(advice.Title));
        AgentAdviceText.Text = _text.Translate(advice.Message);
        AgentPanel.ToolTip = _text.Translate(advice.Message);
        var color = advice.Level switch
        {
            UsageAgentLevel.Healthy => Color.FromRgb(26, 156, 107),
            UsageAgentLevel.Balanced => Color.FromRgb(54, 132, 184),
            UsageAgentLevel.Caution => Color.FromRgb(224, 149, 32),
            UsageAgentLevel.Critical => Color.FromRgb(192, 58, 69),
            UsageAgentLevel.Celebration => Color.FromRgb(202, 146, 28),
            _ => Color.FromRgb(102, 116, 109)
        };
        AgentStatusDot.Fill = new SolidColorBrush(color);
    }

    private UsageAgentAdvice CurrentAgentAdvice(RateLimitSnapshot snapshot)
    {
        var weekly = UsageWindowSelector.FindWeekly(snapshot);
        var remaining = weekly is null ? (int?)null : Math.Clamp(100 - weekly.UsedPercent, 0, 100);
        return _tiboStatus.Display?.Kind switch
        {
            TiboSignalKind.Confirmed => new(UsageAgentLevel.Celebration, "本账户重置已完成",
                "周额度已回满！芽芽继续关注 Tibo 新动态。", remaining, false),
            TiboSignalKind.Planned => new(UsageAgentLevel.Balanced, "等待预告重置",
                "Tibo 已预告，但额度未确认回满；芽芽持续核对。", remaining, false),
            TiboSignalKind.ObservedReset => new(UsageAgentLevel.Balanced, "核对额度中",
                "公开动态报告重置，正在等待本账户额度回满证据。", remaining, false),
            _ => UsageAgent.Evaluate(weekly, snapshot.ResetOpportunityCount)
        };
    }

    private void UpdatePetMood(RateLimitSnapshot snapshot)
    {
        var activeWindow = UsageWindowSelector.FindWeekly(snapshot) ?? UsageWindowSelector.FindFiveHour(snapshot);
        var weekly = activeWindow is null ? 100 : Math.Clamp(100 - activeWindow.UsedPercent, 0, 100);
        var hasResetOpportunity = snapshot.ResetOpportunityCount is > 0;

        if (_tiboStatus.Display?.Kind == TiboSignalKind.Confirmed)
        {
            ApplyCharacterPose("重置完成！", Color.FromRgb(22, 158, 106), "M 24,43 Q 36,55 49,43", 135, -135, false);
            CharacterAura.Opacity = 0.8;
            CharacterStatusGlow.Opacity = 1;
            if (_tiboStatus.TakeCelebration()) PlayCelebrationAnimation();
        }
        else if (_tiboStatus.Display?.Kind is TiboSignalKind.Planned or TiboSignalKind.ObservedReset)
        {
            ApplyCharacterPose("期待重置", Color.FromRgb(226, 150, 34), "M 25,44 Q 36,51 48,44", 65, -65, false);
            CharacterAura.Opacity = 0.26;
            CharacterStatusGlow.Opacity = 0.3;
        }
        else if (hasResetOpportunity)
        {
            ApplyCharacterPose("机会在手", Color.FromRgb(245, 185, 66), "M 24,43 Q 36,55 49,43", 75, -75, false);
            CharacterAura.Opacity = 0.35;
            CharacterStatusGlow.Opacity = 0.6;
        }
        else
        {
            CharacterAura.Opacity = weekly < 20 ? 0.3 : 0;
            CharacterStatusGlow.Opacity = 0;
            switch (weekly)
            {
                case >= 80:
                    ApplyCharacterPose("精神满满", Color.FromRgb(15, 167, 106), "M 24,43 Q 36,54 49,43", 42, -42, false);
                    break;
                case >= 60:
                    ApplyCharacterPose("轻松工作", Color.FromRgb(55, 154, 112), "M 25,44 Q 36,51 48,44", 18, -18, false);
                    break;
                case >= 40:
                    ApplyCharacterPose("认真专注", Color.FromRgb(54, 132, 184), "M 27,47 L 46,47", 7, -7, true);
                    break;
                case >= 20:
                    ApplyCharacterPose("有点担心", Color.FromRgb(224, 149, 32), "M 25,50 Q 36,42 48,50", 30, -30, true);
                    break;
                default:
                    ApplyCharacterPose("额度告急", Color.FromRgb(192, 58, 69), "M 24,51 Q 36,40 49,51", 38, -38, true);
                    PlayWorryAnimation();
                    break;
            }
        }

    }

    private void ApplyCharacterPose(string mood, Color color, string mouth, double leftArm, double rightArm, bool focused)
    {
        PetMoodText.Text = _text.Translate(mood);
        PetStatusDot.Fill = new SolidColorBrush(color);
        CharacterStatusLight.Fill = new SolidColorBrush(color);
        CharacterMouth.Data = Geometry.Parse(mouth);
        LeftBrow.Visibility = focused ? Visibility.Visible : Visibility.Collapsed;
        RightBrow.Visibility = focused ? Visibility.Visible : Visibility.Collapsed;
        LeftPupil.Height = focused ? 5 : 7;
        RightPupil.Height = focused ? 5 : 7;
        AnimateTo(LeftArmRotate, RotateTransform.AngleProperty, leftArm, 260);
        AnimateTo(RightArmRotate, RotateTransform.AngleProperty, rightArm, 260);
    }

    private static void AnimateTo(Animatable target, DependencyProperty property, double value, int milliseconds)
    {
        var current = (double)target.GetValue(property);
        target.BeginAnimation(property, null);
        target.SetValue(property, value);
        if (!SystemParameters.ClientAreaAnimation) return;
        target.BeginAnimation(property, new DoubleAnimation(current, value, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void PlayIdleAnimation()
    {
        if (_collapsed || _stopping || !SystemParameters.ClientAreaAnimation || AvatarContainer.IsMouseOver) return;
        CharacterScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.97, TimeSpan.FromMilliseconds(260))
        {
            AutoReverse = true
        });
        LeftPupil.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(LeftPupil.Height, 1, TimeSpan.FromMilliseconds(90))
        {
            AutoReverse = true
        });
        RightPupil.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(RightPupil.Height, 1, TimeSpan.FromMilliseconds(90))
        {
            AutoReverse = true
        });
    }

    private void PlayCelebrationAnimation()
    {
        if (_collapsed || _stopping || !SystemParameters.ClientAreaAnimation) return;
        CharacterTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -9, TimeSpan.FromMilliseconds(190))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3)
        });
        CharacterRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-4, 4, TimeSpan.FromMilliseconds(140))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(4)
        });
        CharacterStatusGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(240))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(4)
        });
    }

    private void PlayWorryAnimation()
    {
        if (_collapsed || _stopping || !SystemParameters.ClientAreaAnimation) return;
        CharacterRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-2, 2, TimeSpan.FromMilliseconds(120))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3)
        });
    }

    private void Avatar_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_collapsed || _stopping) return;
        AnimateTo(CharacterScale, ScaleTransform.ScaleXProperty, 1.06, 150);
        AnimateTo(CharacterScale, ScaleTransform.ScaleYProperty, 1.06, 150);
        AnimateTo(CharacterRotate, RotateTransform.AngleProperty, 2, 150);
        AnimateTo(RightArmRotate, RotateTransform.AngleProperty, -75, 180);
    }

    private void Avatar_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_collapsed || _stopping) { StopMascotAnimations(); return; }
        AnimateTo(CharacterScale, ScaleTransform.ScaleXProperty, 1, 150);
        AnimateTo(CharacterScale, ScaleTransform.ScaleYProperty, 1, 150);
        AnimateTo(CharacterRotate, RotateTransform.AngleProperty, 0, 150);
        if (_snapshot is not null) UpdatePetMood(_snapshot);
    }

    private async void Avatar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        PlayCelebrationAnimation();
        AnimateTo(RightArmRotate, RotateTransform.AngleProperty, -125, 140);
        if (_snapshot is null)
        {
            StatusText.Text = _text.Get("码力芽芽还在读取用量");
            return;
        }

        var weeklyWindow = UsageWindowSelector.FindWeekly(_snapshot);
        var weekly = weeklyWindow is null ? _text.Get("未知") : $"{Math.Clamp(100 - weeklyWindow.UsedPercent, 0, 100)}%";
        StatusText.Text = _snapshot.ResetOpportunityCount is > 0
            ? _text.Format("太好了，有 {0} 次重置机会！", _snapshot.ResetOpportunityCount)
            : _text.Format("每周剩余 {0} · {1}", weekly, PetMoodText.Text);
        await Task.Delay(2200);
        if (!_shutdownComplete && _snapshot is { } currentSnapshot)
        {
            UpdatePetMood(currentSnapshot);
            SetDetectionStatus();
        }
    }

    private void UpdateResetLabels()
    {
        var now = DateTimeOffset.UtcNow;
        if (_tiboStatus.Tick(now))
        {
            RenderTiboStatus();
            UpdateTiboSchedule();
            if (_snapshot is not null)
            {
                ApplyAgentAdvice(CurrentAgentAdvice(_snapshot));
                UpdatePetMood(_snapshot);
            }
            WriteRuntimeStatus();
        }
        var fiveHour = _snapshot is null ? null : UsageWindowSelector.FindFiveHour(_snapshot);
        var weekly = _snapshot is null ? null : UsageWindowSelector.FindWeekly(_snapshot);
        var clock = _countdownWindow ?? fiveHour;
        UpdateFlipCountdown(FiveHourCountdownPolicy.Format(clock, now));
        var isCached = _snapshot is null || fiveHour?.ResetsAt != _countdownWindow?.ResetsAt ||
            _countdownConfirmedAt is not { } confirmedAt || now - confirmedAt > TimeSpan.FromMinutes(2);
        PetCountdownTitle.Text = clock?.ResetsAt <= now.ToUnixTimeSeconds() ? _text.Get("到时待确认") :
            isCached && _countdownWindow is not null ? _text.Get("继续计时") : _text.Get("5小时重置");
        PetCountdownPanel.ToolTip = FormatWindowReset(clock, compact: false) + _text.Get("（北京时间）") +
            (isCached && _countdownWindow is not null ? _text.Get("\n依据上次确认的时间继续计时，正在重试同步账户") : "") +
            (_tiboStatus.Display?.Kind == TiboSignalKind.Planned ? _text.Get("\nTibo 已预告重置；账户实际上报新时间后才切换周期") : "") +
            (_countdownText == "00:00:00" ? _text.Get("\n已到原定时间，保持零，等待账户确认下一轮时间") : "");
        if (_snapshot is not null && clock?.ResetsAt is { } reset && reset <= now.ToUnixTimeSeconds() &&
            _expiredFiveHourRefresh != reset && !_quotaRefreshing && !_stopping)
        {
            _expiredFiveHourRefresh = reset;
            _ = ConnectAndRefreshAsync();
        }
        FiveHourResetText.Text = FormatWindowReset(clock, compact: true);
        FiveHourResetText.ToolTip = PetCountdownPanel.ToolTip;
        WeeklyResetText.Text = FormatWindowReset(weekly, compact: true);
        WeeklyResetText.ToolTip = FormatWindowReset(weekly, compact: false);
    }

    private void RestoreCountdownCheckpoint(CountdownCheckpoint? checkpoint)
    {
        if (checkpoint is null || !FiveHourCountdownPolicy.IsValidReset(checkpoint.ResetsAt, DateTimeOffset.UtcNow)) return;
        _countdownWindow = new RateLimitWindow { ResetsAt = checkpoint.ResetsAt, WindowDurationMins = 300 };
        _countdownConfirmedAt = checkpoint.ConfirmedAt;
        PetCountdownPanel.Visibility = Visibility.Visible;
        UpdateResetLabels();
    }

    private void RememberCountdownWindow(RateLimitWindow? window, DateTimeOffset now)
    {
        if (window is null)
        {
            _countdownWindow = null;
            _countdownConfirmedAt = null;
        }
        else if (FiveHourCountdownPolicy.IsValidReset(window.ResetsAt, now))
        {
            _countdownWindow = new RateLimitWindow { ResetsAt = window.ResetsAt, WindowDurationMins = 300 };
            _countdownConfirmedAt = now;
        }
    }

    private void UpdateFlipCountdown(string text)
    {
        _countdownText = text;
        var parts = text.Split(':');
        var hasTime = parts.Length == 3 && parts.All(part => part.Length == 2 &&
            part.All(character => character is >= '0' and <= '9'));
        CountdownNumbers.Visibility = hasTime ? Visibility.Visible : Visibility.Collapsed;
        PetCountdownStatus.Visibility = hasTime ? Visibility.Collapsed : Visibility.Visible;
        PetCountdownStatus.Text = hasTime ? "" : _text.Translate(text);
        var animate = !_collapsed && IsLoaded;
        HoursFlip.SetNumber(hasTime ? parts[0] : "--", animate);
        MinutesFlip.SetNumber(hasTime ? parts[1] : "--", animate);
        SecondsFlip.SetNumber(hasTime ? parts[2] : "--", animate);
    }

    private void StopCountdownAnimation()
    {
        HoursFlip.StopAnimation();
        MinutesFlip.StopAnimation();
        SecondsFlip.StopAnimation();
    }

    private string FormatWindowReset(RateLimitWindow? window, bool compact)
    {
        if (window?.ResetsAt is not long unixSeconds) return _text.Get("重置时间等待上报");

        DateTimeOffset end;
        try { end = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToOffset(TimeSpan.FromHours(8)); }
        catch (ArgumentOutOfRangeException) { return _text.Get("重置时间等待上报"); }
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        var remaining = end - now;
        var countdown = remaining <= TimeSpan.Zero
            ? _text.Get("即将刷新")
            : remaining.TotalDays >= 1
                ? _text.Format("{0}天{1}时", Math.Floor(remaining.TotalDays), remaining.Hours)
                : _text.Format("{0}时{1}分", remaining.Hours, remaining.Minutes);
        if (!compact) return _text.Format("{0:M月d日 HH:mm}重置 · {1}{2}", end, countdown, (remaining > TimeSpan.Zero ? _text.Get("后") : ""));
        var date = window.WindowDurationMins <= 300 && end.Date == now.Date
            ? end.ToString("HH:mm", CultureInfo.InvariantCulture)
            : end.ToString("M/d HH:mm", CultureInfo.InvariantCulture);
        return $"{date} · {countdown}";
    }

    private string FormatResetOpportunity(RateLimitSnapshot snapshot)
    {
        if (!snapshot.ResetOpportunityReported) return _text.Get("协议未上报");
        if (snapshot.ResetOpportunityCount is not > 0) return _text.Get("暂无可用重置");
        if (snapshot.ResetOpportunityExpiresAt is not long expiresAt)
            return _text.Format("可用 {0} 次", snapshot.ResetOpportunityCount);

        DateTimeOffset expiry;
        try { expiry = DateTimeOffset.FromUnixTimeSeconds(expiresAt).ToOffset(TimeSpan.FromHours(8)); }
        catch (ArgumentOutOfRangeException) { return _text.Format("可用 {0} 次", snapshot.ResetOpportunityCount); }
        return _text.Format("可用 {0} 次 · {1:M月d日}前", snapshot.ResetOpportunityCount, expiry);
    }

    private void SetDetectionStatus()
    {
        if (_snapshot is null) return;
        var capabilities = _text.Translate(UsageWindowSelector.DescribeCapabilities(_snapshot));
        StatusText.Text = _text.Format("已检测 {0} · {1:HH:mm:ss}", capabilities, DateTime.Now);
        StatusText.ToolTip = _text.Format("账户每{0:0}秒刷新：{1}\n悬停芽芽快报可查看下次动态检查时间", _refreshTimer.Interval.TotalSeconds, capabilities);
        StatusText.ToolTip += _text.Format("\n运行版本：{0}", WidgetRuntimeStatus.BuildVersion);
        StatusText.ToolTip += "\n" + ConnectionDescription();
        WriteRuntimeStatus();
    }

    private void WriteRuntimeStatus(bool running = true) => WidgetRuntimeStatus.Write(
        _snapshot, _tiboStatus.Display, running, _chineseBrief?.Text, _chineseBrief?.Provider,
        _tiboStatus.Display?.LatestPost?.SourceUrl, _countdownText, _countdownWindow?.ResetsAt, _countdownConfirmedAt);

    private void AgentPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_snapshot is null) return;
        var advice = CurrentAgentAdvice(_snapshot);
        StatusText.Text = _text.Translate(advice.Message);
        if (advice.Level == UsageAgentLevel.Celebration) PlayCelebrationAnimation();
        else PlayIdleAnimation();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await ConnectAndRefreshAsync();
        await CheckTiboAsync();
    }

    private string ConnectionDescription() => _client.Installation is { } installation
        ? _text.Format("CLI 来源：{0}\n版本：{1}\n路径：{2}\n右键可重新识别或选择 CLI；使用本机现有登录，不复制凭据。", _text.Translate(installation.Source), installation.Version, installation.ExecutablePath)
        : _text.Get("尚未识别可用 CLI；右键小组件可重新识别或手动选择 codex.exe。");

    private void ConnectionInfo_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, ConnectionDescription(), _text.Get("Codex 连接信息"), MessageBoxButton.OK, MessageBoxImage.Information);

    private async void DetectCodex_Click(object sender, RoutedEventArgs e) => await ConfigureCodexAsync(false);

    private async void ChooseCodex_Click(object sender, RoutedEventArgs e) => await ConfigureCodexAsync(true);

    private async Task ConfigureCodexAsync(bool chooseFile)
    {
        if (_stopping || _changingConnection) return;
        if (_quotaRefreshing)
        {
            StatusText.Text = _text.Get("正在检测连接，请稍候再选择 CLI。");
            return;
        }
        _changingConnection = true;
        var changed = false;
        try
        {
            string? selected = null;
            if (chooseFile)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = _text.Get("选择可信的 Codex CLI 原生程序（不是桌面快捷方式或脚本）"),
                    Filter = "Codex CLI (*.exe)|*.exe", CheckFileExists = true, Multiselect = false
                };
                if (dialog.ShowDialog(this) != true) return;
                if (_stopping) return;
                selected = dialog.FileName;
                if (!CodexExecutableResolver.IsNativeExecutable(selected,
                        System.Runtime.InteropServices.RuntimeInformation.OSArchitecture))
                    throw new CodexDiscoveryException("请选择与当前 Windows 架构兼容的 Codex CLI 原生 EXE。" );
            }
            CodexConnectionSettings.SavePath(selected);
            await _client.ChangeExecutableAsync(selected);
            if (_stopping) return;
            ClearQuotaForConnectionChange();
            changed = true;
        }
        catch (Exception ex) when (WidgetDataPaths.IsStorageError(ex) || ex is CodexDiscoveryException)
        {
            StatusText.Text = _text.Get("CLI 设置未完成；请查看错误提示。");
            MessageBox.Show(this, _text.ErrorMessage(ex.Message), _text.Get("Codex 连接设置"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _changingConnection = false; }
        if (changed && !_stopping) await ConnectAndRefreshAsync();
    }

    private void ClearQuotaForConnectionChange()
    {
        _snapshot = null;
        _countdownWindow = null;
        _countdownConfirmedAt = null;
        _expiredFiveHourRefresh = null;
        _tiboStatus = new TiboStatusCoordinator();
        if (_tiboSignal is not null) _tiboStatus.OnSignal(_tiboSignal, DateTimeOffset.Now);
        PlanText.Text = "--";
        CreditsText.Text = "--";
        ResetChanceText.Text = _text.Get("等待账户");
        ApplyUsageWindow(null, FiveHourProgress, FiveHourRemainingText, FiveHourUsedText, FiveHourHintText,
            UsageHintPolicy.ForFiveHour, Color.FromRgb(21, 150, 106));
        ApplyUsageWindow(null, WeeklyProgress, WeeklyRemainingText, WeeklyUsedText, WeeklyHintText,
            UsageHintPolicy.ForWeekly, Color.FromRgb(23, 111, 179));
        UpdateCapabilityLayout(false, false);
        UpdateResetLabels();
        RenderTiboStatus();
        AgentTitleText.Text = _text.Get("芽芽智能体 · 重新连接中");
        AgentAdviceText.Text = _text.Get("等待读取当前 CLI 的账户用量，不沿用旧数据。");
        PetMoodText.Text = _text.Get("等待连接");
        WriteRuntimeStatus();
    }

    private void Collapse_Click(object sender, RoutedEventArgs e)
    {
        var right = Left + ActualWidth;
        _collapsed = !_collapsed;
        if (_collapsed)
        {
            StopCountdownAnimation();
            StopMascotAnimations();
        }
        DetailsPanel.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        Width = _collapsed ? CollapsedWidth : ExpandedWidth;
        if (_collapsed)
        {
            SizeToContent = SizeToContent.Manual;
            Height = CollapsedHeight;
        }
        else
        {
            SizeToContent = SizeToContent.Height;
            Height = double.NaN;
        }
        Left = right - Width;
        CollapseButton.Content = _collapsed ? "⌄" : "⌃";
        PlanBadge.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        RefreshButton.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        if (!_collapsed) Dispatcher.BeginInvoke(PositionTopRight, DispatcherPriority.Loaded);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void TiboNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (!SavePreferences(_preferences with { TiboEnabled = TiboNetworkMenu.IsChecked })) return;
        SetTiboNetworking(_preferences.TiboEnabled);
        if (_preferences.TiboEnabled) await CheckTiboAsync();
    }

    private void SetTiboNetworking(bool enabled)
    {
        _tiboNetwork.SetEnabled(enabled);
        _briefCancellation?.Cancel();
        _briefKey = null;
        _briefPending = false;
        _chineseBrief = null;
        _tiboSignal = null;
        _tiboStatus = new TiboStatusCoordinator();
        if (_snapshot is not null) _tiboStatus.OnSnapshot(_snapshot, DateTimeOffset.Now);
        _tiboFailures = 0;
        _pollingPlan = null;
        _nextTiboCheckAt = DateTimeOffset.MinValue;
        if (enabled && IsLoaded) _tiboTimer.Start(); else _tiboTimer.Stop();
        TiboHeadlineText.Text = _text.Get("Tibo 动态 · 正在检查");
        TiboDetailText.Text = _text.Get("正在获取动态与本账户状态");
        TiboOriginalText.Visibility = TiboChineseText.Visibility = TiboOpenText.Visibility = Visibility.Collapsed;
        RenderTiboStatus();
        UpdateTiboSchedule();
        if (_snapshot is not null)
        {
            ApplyAgentAdvice(CurrentAgentAdvice(_snapshot));
            UpdatePetMood(_snapshot);
        }
        WriteRuntimeStatus();
    }

    private void CompactMode_Click(object sender, RoutedEventArgs e)
    {
        if (SavePreferences(_preferences with { CompactMode = CompactModeMenu.IsChecked })) ApplyCompactMode();
    }

    private bool SavePreferences(WidgetPreferences next)
    {
        try { WidgetPreferences.Save(next); _preferences = next; return true; }
        catch (Exception error) when (WidgetDataPaths.IsStorageError(error))
        {
            TiboNetworkMenu.IsChecked = _preferences.TiboEnabled;
            CompactModeMenu.IsChecked = _preferences.CompactMode;
            StatusText.Text = _text.Get("无法保存偏好设置，原设置未改变。");
            return false;
        }
    }

    private void ApplyCompactMode()
    {
        var visibility = _preferences.CompactMode ? Visibility.Collapsed : Visibility.Visible;
        AgentPanel.Visibility = ResetInfoRow.Visibility = TiboPanel.Visibility = visibility;
        PetMoodText.Visibility = visibility;
        MascotViewport.Width = _preferences.CompactMode ? 30 : 43;
        MascotViewport.Height = _preferences.CompactMode ? 44 : 64;
        if (!_collapsed) { SizeToContent = SizeToContent.Height; Height = double.NaN; }
    }

    private void StopMascotAnimations()
    {
        CharacterScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CharacterScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CharacterScale.ScaleX = CharacterScale.ScaleY = 1;
        CharacterRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        CharacterRotate.Angle = 0;
        CharacterTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        CharacterTranslate.Y = 0;
        LeftArmRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        RightArmRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        LeftPupil.BeginAnimation(FrameworkElement.HeightProperty, null);
        RightPupil.BeginAnimation(FrameworkElement.HeightProperty, null);
        CharacterStatusGlow.BeginAnimation(OpacityProperty, null);
        TiboSignalRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        TiboSignalRotate.Angle = 0;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_settingStartup) return;
        try { StartupManager.SetEnabled(StartupCheckBox.IsChecked == true); }
        catch (Exception ex) { StatusText.Text = _text.Format("开机启动设置失败：{0}", _text.ErrorMessage(ex.Message)); }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownComplete) return;
        e.Cancel = true;
        if (_stopping) return;
        _stopping = true;
        WriteRuntimeStatus(running: false);
        _lifetime.Cancel();
        _tiboNetwork.Dispose();
        _updateCancellation?.Cancel();
        _briefCancellation?.Cancel();
        _countdownTimer.Stop();
        _refreshTimer.Stop();
        _tiboTimer.Stop();
        _petTimer.Stop();
        _updateTimer.Stop();
        StopCountdownAnimation();
        StopMascotAnimations();
        GlassSheenTransform.BeginAnimation(TranslateTransform.XProperty, null);
        await _client.DisposeAsync();
        _tiboMonitor.Dispose();
        _chineseBriefClient?.Dispose();
        _updates.Dispose();
        _briefCancellation?.Dispose();
        _shutdownComplete = true;
        // Disposal can finish inline; leave the original Closing event before closing again.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Close));
    }
}





