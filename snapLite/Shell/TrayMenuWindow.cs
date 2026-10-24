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
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ShareX.AvaloniaUI.Theming;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ShareX;

/// <summary>
/// The menu shown when the tray icon is clicked: each action with its hotkey, a hand cursor and a darker row on hover.
/// It closes when it loses focus, like a native menu.
/// </summary>
internal sealed class TrayMenuWindow : Window
{
    /// <summary>One row; a null entry in the list is a separator.</summary>
    public sealed record Entry(string Text, string? Hotkey, Action Action);

    private static TrayMenuWindow? current;
    private static long closedAt;
    private readonly IBrush hoverBrush;
    private bool closing;
    private long openedAt = Environment.TickCount64;

    private TrayMenuWindow(IReadOnlyList<Entry?> entries)
    {
        RequestedThemeVariant = ThemeManager.GetCurrentTheme();
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = AppInfo.Name;
        // A dark menu needs a stronger shade for the same visible step.
        hoverBrush = new SolidColorBrush(ThemeManager.IsDarkTheme ? Color.FromArgb(0x55, 0, 0, 0) : Color.FromArgb(0x1A, 0, 0, 0));

        StackPanel rows = new() { MinWidth = 260 };
        foreach (Entry? entry in entries)
        {
            if (entry != null)
            {
                rows.Children.Add(CreateRow(entry));
                continue;
            }
            Border separator = new() { Height = 1, Margin = new Thickness(8, 4) };
            BindBrush(separator, Border.BackgroundProperty, "ShareX.Brush.Separator");
            rows.Children.Add(separator);
        }
        Border frame = new()
        {
            Padding = new Thickness(4),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = rows
        };
        BindBrush(frame, Border.BorderBrushProperty, "ShareX.Brush.Border");
        BindBrush(frame, Border.BackgroundProperty, "ShareX.Brush.Background.Popup");
        BindBrush(frame, TextElement.ForegroundProperty, "ShareX.Brush.Text");
        Content = frame;

        Deactivated += (_, _) =>
        {
            // Opening from the hidden-icons flyout: Windows closes that flyout right after the click and briefly
            // takes focus. Losing focus that early is not the user leaving the menu, so take it back.
            if (Environment.TickCount64 - openedAt < 500) Dispatcher.UIThread.Post(Activate, DispatcherPriority.Background);
            else CloseMenu();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseMenu(); };
        Opened += (_, _) => Dispatcher.UIThread.Post(PlaceAtCursor, DispatcherPriority.Loaded);
        Closed += (_, _) => { if (ReferenceEquals(current, this)) current = null; };
    }

    private Control CreateRow(Entry entry)
    {
        Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock { Text = entry.Text, VerticalAlignment = VerticalAlignment.Center });
        if (!string.IsNullOrEmpty(entry.Hotkey))
        {
            TextBlock hotkey = new()
            {
                Text = entry.Hotkey,
                Margin = new Thickness(32, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            BindBrush(hotkey, TextBlock.ForegroundProperty, "ShareX.Brush.Text.Secondary");
            Grid.SetColumn(hotkey, 1);
            grid.Children.Add(hotkey);
        }
        Border row = new()
        {
            Padding = new Thickness(12, 7),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = grid
        };
        AutomationProperties.SetName(row, entry.Text);
        row.PointerEntered += (_, _) => row.Background = hoverBrush;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            CloseMenu();
            // Let the menu disappear first, so a screenshot never captures it.
            DispatcherTimer.RunOnce(entry.Action, TimeSpan.FromMilliseconds(150));
        };
        return row;
    }

    // Theme brushes resolve only once the window has its theme variant, so bind instead of reading them now.
    private void BindBrush(AvaloniaObject target, AvaloniaProperty property, string key) =>
        target.Bind(property, this.GetResourceObservable(key));

    private void CloseMenu()
    {
        if (closing) return;
        closing = true;
        closedAt = Environment.TickCount64;
        Close();
    }

    // Like a tray menu: the bottom-right corner at the cursor, kept inside the screen's work area.
    private void PlaceAtCursor()
    {
        GetCursorPos(out NativePoint cursor);
        PixelPoint point = new(cursor.X, cursor.Y);
        Screen? screen = Screens.ScreenFromPoint(point) ?? Screens.Primary;
        if (screen == null) return;
        PixelSize size = PixelSize.FromSize(Bounds.Size, screen.Scaling);
        PixelRect area = screen.WorkingArea;
        int x = Math.Clamp(point.X - size.Width, area.X, Math.Max(area.X, area.Right - size.Width));
        int y = Math.Clamp(point.Y - size.Height, area.Y, Math.Max(area.Y, area.Bottom - size.Height));
        Position = new PixelPoint(x, y);
        Activate();
    }

    /// <summary>Opens the menu at the cursor, or closes it when it is already open.</summary>
    public static void Toggle(IReadOnlyList<Entry?> entries)
    {
        if (current != null)
        {
            current.CloseMenu();
            return;
        }
        // Clicking the icon of an open menu first deactivates (closes) it; that click must not reopen it.
        if (Environment.TickCount64 - closedAt < 300) return;
        current = new TrayMenuWindow(entries);
        current.Show();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
}
