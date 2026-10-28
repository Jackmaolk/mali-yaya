using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace CodexUsageWidget.Tests;

[Collection("WpfPresentation")]
public sealed class WidgetPresentationTests
{
    [Fact]
    public void CompactLayoutKeepsCountdownAndChineseInsidePanel()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
                T Find<T>(string name) => (T)window.FindName(name);
                Find<Border>("FiveHourCard").Visibility = Visibility.Visible;
                Find<Border>("WeeklyCard").Visibility = Visibility.Visible;
                Find<Border>("NoLimitsCard").Visibility = Visibility.Collapsed;
                Find<Border>("PetCountdownPanel").Visibility = Visibility.Visible;
                Find<TextBlock>("PlanText").Text = "PLUS";
                Find<TextBlock>("FiveHourRemainingText").Text = "100%";
                Find<TextBlock>("WeeklyRemainingText").Text = "92%";
                var countdown = Find<FlipNumber>("HoursFlip");
                countdown.SetNumber("04", animate: false);
                Find<FlipNumber>("MinutesFlip").SetNumber("59", animate: false);
                Find<FlipNumber>("SecondsFlip").SetNumber("32", animate: false);
                Find<TextBlock>("TiboHeadlineText").Text = "Tibo 最新已核实动态 · 10月27日 03:24";
                Find<TextBlock>("TiboDetailText").Visibility = Visibility.Collapsed;
                Find<TextBlock>("TiboOpenText").Visibility = Visibility.Visible;
                Find<TextBlock>("TiboOriginalText").Visibility = Visibility.Visible;
                Find<TextBlock>("TiboOriginalText").Text = "原文：Day 22/ Terra is back! Our first model that is significantly better at writing.";
                var chinese = Find<TextBlock>("TiboChineseText");
                chinese.Visibility = Visibility.Visible;
                chinese.Text = "中文：Terra 回归，在写作、个性和理解请求方面有所改进。完整机译可悬停查看。";

                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(294, double.PositiveInfinity));
                root.Arrange(new Rect(0, 0, 294, root.DesiredSize.Height));
                root.UpdateLayout();
                Assert.InRange(root.ActualHeight, 100, 430);
                var textWidth = new FormattedText(countdown.Value, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface(countdown.FontFamily, countdown.FontStyle,
                        countdown.FontWeight, countdown.FontStretch), countdown.FontSize, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
                Assert.True(textWidth <= countdown.ActualWidth, "Countdown text must fit without clipping.");
                Assert.True(countdown.FontSize >= 18, "Flip digits should be large enough to read.");
                Assert.True(Find<FlipNumber>("HoursFlip").TranslatePoint(new Point(), root).Y <
                    Find<FlipNumber>("MinutesFlip").TranslatePoint(new Point(), root).Y);
                Assert.True(Find<FlipNumber>("MinutesFlip").TranslatePoint(new Point(), root).Y <
                    Find<FlipNumber>("SecondsFlip").TranslatePoint(new Point(), root).Y);
                Assert.True(chinese.ActualHeight > 14 && chinese.ActualHeight <= 42);
                var panel = Find<Border>("TiboPanel");
                var bottom = chinese.TranslatePoint(new Point(0, chinese.ActualHeight), panel).Y;
                Assert.True(bottom <= panel.ActualHeight, "Chinese text must remain inside its card.");
                var resetRow = Find<Grid>("ResetInfoRow");
                var gap = panel.TranslatePoint(new Point(), root).Y -
                    resetRow.TranslatePoint(new Point(0, resetRow.ActualHeight), root).Y;
                Assert.InRange(gap, 0, 6);

                foreach (var scale in new[] { 1.0, 1.5, 2.0 })
                {
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * scale),
                        (int)Math.Ceiling(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"widget-preview-{scale * 100:0}.png"));
                    encoder.Save(output);
                }

                var expandedHeight = root.ActualHeight;
                Find<Border>("FiveHourCard").Visibility = Visibility.Collapsed;
                Find<Border>("PetCountdownPanel").Visibility = Visibility.Collapsed;
                root.Measure(new Size(294, double.PositiveInfinity));
                root.Arrange(new Rect(0, 0, 294, root.DesiredSize.Height));
                root.UpdateLayout();
                Assert.True(root.ActualHeight < expandedHeight, "Weekly-only layout should shrink with its content.");
                gap = panel.TranslatePoint(new Point(), root).Y -
                    resetRow.TranslatePoint(new Point(0, resetRow.ActualHeight), root).Y;
                Assert.InRange(gap, 0, 6);
            }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF layout check timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
