#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ShareX.HelpersLib;
using System;
using System.Threading.Tasks;
using Rectangle = System.Drawing.Rectangle;
using CaptureStrings = ShareX.ScreenCaptureLib.Localization.Strings;

namespace ShareX;

/// <summary>
/// The start-delay countdown in the middle of the recording's screen: 5, 4, 3, 2, 1, then "Start".
/// Click-through and excluded from capture, so it never covers controls or shows up in the video.
/// </summary>
internal sealed class RecordingCountdownWindow : Window
{
    private const double TileSize = 220;
    private readonly TextBlock text;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Rectangle region;
    private int remaining;

    private RecordingCountdownWindow(int seconds, Rectangle region)
    {
        this.region = region;
        remaining = seconds;
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Title = AppInfo.Name;
        IsHitTestVisible = false;
        Opacity = 0;   // until it is centered

        text = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            RenderTransform = new ScaleTransform(1, 1),
            RenderTransformOrigin = RelativePoint.Center
        };
        Content = new Border
        {
            Width = TileSize,
            Height = TileSize,
            CornerRadius = new CornerRadius(36),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x12, 0x12, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            // Long translations of "Start" (e.g. "Rozpocznij") shrink to fit the tile.
            Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(20), Child = text }
        };

        ShowNumber();
        timer.Tick += (_, _) =>
        {
            // Never below 1: "Start" appears when recording actually begins.
            if (remaining > 1)
            {
                remaining--;
                ShowNumber();
            }
        };
        Opened += (_, _) =>
        {
            if (TryGetPlatformHandle()?.Handle is IntPtr handle)
            {
                // Not in the recording, and clicks go to whatever is underneath.
                NativeMethods.SetWindowDisplayAffinity(handle, 0x11); // WDA_EXCLUDEFROMCAPTURE
                long style = NativeMethods.GetWindowLong(handle, -20).ToInt64(); // GWL_EXSTYLE
                NativeMethods.SetWindowLong(handle, -20, (IntPtr)(style | 0x20 | 0x80000)); // WS_EX_TRANSPARENT | WS_EX_LAYERED
            }
            Dispatcher.UIThread.Post(CenterOnRecordingScreen, DispatcherPriority.Loaded);
            timer.Start();
        };
        Closed += (_, _) => timer.Stop();
    }

    private void ShowNumber() => ShowText(remaining.ToString(), 128, Brushes.White);

    private void ShowText(string value, double size, IBrush brush)
    {
        text.Text = value;
        text.FontSize = size;
        text.Foreground = brush;
        Pop();
    }

    // A short "pop" for each new value: from slightly larger and transparent to its resting size.
    private void Pop()
    {
        Animation animation = new()
        {
            Duration = TimeSpan.FromMilliseconds(260),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(ScaleTransform.ScaleXProperty, 1.35), new Setter(ScaleTransform.ScaleYProperty, 1.35), new Setter(OpacityProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(ScaleTransform.ScaleXProperty, 1.0), new Setter(ScaleTransform.ScaleYProperty, 1.0), new Setter(OpacityProperty, 1.0) } }
            }
        };
        _ = animation.RunAsync(text);
    }

    private void CenterOnRecordingScreen()
    {
        PixelPoint center = new(region.X + region.Width / 2, region.Y + region.Height / 2);
        Screen? screen = Screens.ScreenFromPoint(center) ?? Screens.Primary;
        if (screen != null)
        {
            PixelSize size = PixelSize.FromSize(Bounds.Size, screen.Scaling);
            PixelRect bounds = screen.Bounds;
            Position = new PixelPoint(bounds.X + (bounds.Width - size.Width) / 2, bounds.Y + (bounds.Height - size.Height) / 2);
        }
        Opacity = 1;
    }

    private async Task FinishAsync(bool started)
    {
        timer.Stop();
        if (started)
        {
            ShowText(CaptureStrings.ScreenRecordWindow_Start, 64, this.FindResource("ShareX.Brush.Accent.Start") as IBrush ?? Brushes.DeepSkyBlue);
            await Task.Delay(800);
        }
        Close();
    }

    /// <summary>
    /// Shows the countdown for <paramref name="milliseconds"/> (rounded up to whole seconds) on the screen that
    /// holds <paramref name="region"/>. Call from any thread; finish it with the returned handle.
    /// </summary>
    public static Handle Show(int milliseconds, Rectangle region)
    {
        int seconds = Math.Max(1, (int)Math.Ceiling(milliseconds / 1000.0));
        RecordingCountdownWindow? window = null;
        Dispatcher.UIThread.Invoke(() =>
        {
            window = new RecordingCountdownWindow(seconds, region);
            window.Show();
        });
        return new Handle(window!);
    }

    /// <summary>Ends the countdown once: <see cref="Start"/> shows "Start" briefly, <see cref="Cancel"/> closes it.</summary>
    public sealed class Handle(RecordingCountdownWindow window)
    {
        private int finished;

        public void Start() => Finish(true);

        public void Cancel() => Finish(false);

        private void Finish(bool started)
        {
            if (System.Threading.Interlocked.Exchange(ref finished, 1) == 0)
            {
                Dispatcher.UIThread.Post(() => _ = window.FinishAsync(started));
            }
        }
    }
}
