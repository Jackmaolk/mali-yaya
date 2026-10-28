using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Xunit;

namespace CodexUsageWidget.Tests;

[Collection("WpfPresentation")]
public sealed class FlipNumberTests
{
    [Fact]
    public void InitialAndExplicitlyNonAnimatedValuesHaveStaticSnapshots()
    {
        RunInSta(() =>
        {
            var number = new FlipNumber();
            AssertSnapshot(number, "--");
            number.SetNumber("09");
            AssertSnapshot(number, "09");
            number.SetNumber("10", animate: false);
            AssertSnapshot(number, "10");
            number.SetNumber("10");
            AssertSnapshot(number, "10");
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("123")]
    [InlineData(" 12")]
    [InlineData("12 ")]
    [InlineData("-1")]
    [InlineData("1a")]
    [InlineData("\uFF11\uFF12")]
    [InlineData("\u0661\u0662")]
    public void InvalidValuesBecomePlaceholder(string? value)
    {
        RunInSta(() =>
        {
            var number = new FlipNumber();
            number.SetNumber("42", animate: false);
            number.SetNumber(value!, animate: false);
            AssertSnapshot(number, "--");
        });
    }

    [Fact]
    public void UnloadedUpdatesAndRepeatedStopsKeepTheLatestValue()
    {
        RunInSta(() =>
        {
            var number = new FlipNumber();
            Assert.False(number.IsLoaded);
            number.SetNumber("01");
            number.SetNumber("02");
            number.StopAnimation();
            number.StopAnimation();
            AssertSnapshot(number, "02");
            number.SetNumber("--", animate: false);
            number.StopAnimation();
            AssertSnapshot(number, "--");
        });
    }

    [Theory]
    [InlineData("00")]
    [InlineData("11")]
    [InlineData("59")]
    [InlineData("88")]
    [InlineData("99")]
    [InlineData("--")]
    public void StaticDigitPairsFitAlignedClippedHalves(string value)
    {
        RunInSta(() =>
        {
            var number = new FlipNumber();
            number.SetNumber(value, animate: false);
            number.Measure(new Size(31, 24));
            number.Arrange(new Rect(0, 0, 31, 24));
            number.UpdateLayout();

            var dpi = VisualTreeHelper.GetDpi(number);
            Assert.InRange(Math.Abs(number.ActualWidth - 31), 0, 1 / dpi.DpiScaleX);
            Assert.InRange(Math.Abs(number.ActualHeight - 24), 0, 1 / dpi.DpiScaleY);
            Assert.Equal("Consolas", number.FontFamily.Source);
            Assert.Equal(19d, number.FontSize);
            Assert.Equal(FontWeights.Bold, number.FontWeight);
            var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(number.FontFamily, number.FontStyle, number.FontWeight, number.FontStretch),
                number.FontSize, number.Foreground, VisualTreeHelper.GetDpi(number).PixelsPerDip);
            Assert.InRange(text.WidthIncludingTrailingWhitespace, 1d, 29d);
            Assert.InRange(text.Height, 1d, 24d);

            foreach (var name in new[] { "StaticTopHalf", "StaticBottomHalf" })
            {
                var half = Find<Border>(number, name);
                Assert.Equal(12d, half.ActualHeight);
                Assert.True(half.ClipToBounds);
                Assert.True(Assert.IsType<Canvas>(half.Child).ClipToBounds);
            }
            var top = Find<TextBlock>(number, "StaticTopText");
            var bottom = Find<TextBlock>(number, "StaticBottomText");
            Assert.Equal(top.TranslatePoint(new Point(), number), bottom.TranslatePoint(new Point(), number));
            Assert.Equal(TextTrimming.None, top.TextTrimming);
            Assert.Equal(TextTrimming.None, bottom.TextTrimming);
            AssertSnapshot(number, value);
        });
    }

    [Fact]
    public void UpperAndLowerHalfFoldInSequenceAndCanBeStopped()
    {
        RunInSta(() =>
        {
            var number = new FlipNumber();
            number.SetNumber("04", animate: false);
            // Seek the controllable animation clock to check both phases without a visible window.
            typeof(FlipNumber).GetMethod("StartFlip", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(number, ["05"]);
            var clock = (ClockGroup)typeof(FlipNumber).GetField("_flipClock",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(number)!;
            clock.Controller!.Pause();
            clock.Controller.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(70), TimeSeekOrigin.BeginTime);
            Assert.InRange(Find<ScaleTransform>(number, "TopFoldScale").ScaleY, 0.01, 0.99);
            Assert.Equal(0, Find<ScaleTransform>(number, "BottomFoldScale").ScaleY);
            clock.Controller.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(210), TimeSeekOrigin.BeginTime);
            Assert.Equal(0, Find<ScaleTransform>(number, "TopFoldScale").ScaleY);
            Assert.InRange(Find<ScaleTransform>(number, "BottomFoldScale").ScaleY, 0.01, 0.99);
            number.StopAnimation();
            AssertSnapshot(number, "04");
        });
    }

    private static void AssertSnapshot(FlipNumber number, string expected)
    {
        Assert.Equal(expected, number.Value);
        foreach (var name in new[] { "StaticTopText", "StaticBottomText", "OldTopText", "NewBottomText" })
            Assert.Equal(expected, Find<TextBlock>(number, name).Text);
        Assert.Equal(Visibility.Collapsed, Find<Border>(number, "OldTopHalf").Visibility);
        Assert.Equal(Visibility.Collapsed, Find<Border>(number, "NewBottomHalf").Visibility);
        var top = Find<ScaleTransform>(number, "TopFoldScale");
        var bottom = Find<ScaleTransform>(number, "BottomFoldScale");
        Assert.False(top.HasAnimatedProperties);
        Assert.False(bottom.HasAnimatedProperties);
        Assert.Equal(1d, top.ScaleY);
        Assert.Equal(0d, bottom.ScaleY);
    }

    private static T Find<T>(FlipNumber number, string name) => Assert.IsType<T>(number.FindName(name));

    private static void RunInSta(Action check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { check(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Offscreen WPF check timed out.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
