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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using SkiaSharp;
using System;
using System.IO;

namespace ShareX;

/// <summary>A small toast in the corner of the screen; clicking it opens the file.</summary>
internal sealed class NotificationWindow : Window
{
    private static NotificationWindow? current;
    private readonly DispatcherTimer closeTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private NotificationWindow(string title, string? path, SKBitmap? image)
    {
        RequestedThemeVariant = ThemeManager.GetCurrentTheme();
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = AppInfo.Name;

        StackPanel text = new() { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 260 };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14 });
        if (path != null)
        {
            text.Children.Add(new TextBlock { Text = Path.GetFileName(path), TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.8 });
            text.Children.Add(new TextBlock { Text = Localization.Strings.Lite_ClickToOpen, FontSize = 11, Opacity = 0.6 });
        }
        StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
        if (image != null) content.Children.Add(new Image { Source = CreateThumbnail(image), MaxWidth = 120, MaxHeight = 80, Stretch = Stretch.Uniform });
        content.Children.Add(text);
        Content = new Border
        {
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            BorderBrush = this.FindResource("ShareX.Brush.Accent") as IBrush ?? Brushes.DodgerBlue,
            Background = this.FindResource("ShareX.Brush.Background.Popup") as IBrush ?? Brushes.White,
            Child = content
        };
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerPressed += (_, _) =>
        {
            if (path != null && File.Exists(path)) FileHelpers.OpenFile(path);
            Close();
        };
        PointerEntered += (_, _) => closeTimer.Stop();
        PointerExited += (_, _) => closeTimer.Start();
        closeTimer.Tick += (_, _) => Close();
        Opened += (_, _) =>
        {
            // The size is known only after the first layout pass.
            Dispatcher.UIThread.Post(PlaceInCorner, DispatcherPriority.Loaded);
            closeTimer.Start();
        };
        Closed += (_, _) =>
        {
            closeTimer.Stop();
            if (ReferenceEquals(current, this)) current = null;
        };
    }

    // Bottom-right of the primary work area, in physical pixels.
    private void PlaceInCorner()
    {
        Screen? screen = Screens.Primary;
        if (screen == null) return;
        PixelSize size = PixelSize.FromSize(Bounds.Size, screen.Scaling);
        PixelRect area = screen.WorkingArea;
        int margin = (int)(16 * screen.Scaling);
        Position = new PixelPoint(area.Right - size.Width - margin, area.Bottom - size.Height - margin);
    }

    /// <summary>Shows a toast, replacing the previous one. The image is copied; the caller keeps ownership.</summary>
    public static void Show(string title, string? path, SKBitmap? image = null)
    {
        current?.Close();
        current = new NotificationWindow(title, path, image);
        current.Show();
    }

    private static Bitmap CreateThumbnail(SKBitmap image)
    {
        float scale = Math.Min(1f, Math.Min(240f / image.Width, 160f / image.Height));
        SKImageInfo info = new(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
        using SKBitmap small = image.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) ?? image.Copy();
        using SKData png = small.Encode(SKEncodedImageFormat.Png, 100);
        using MemoryStream stream = new(png.ToArray());
        return new Bitmap(stream);
    }
}
