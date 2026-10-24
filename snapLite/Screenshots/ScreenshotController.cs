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

using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using ShareX.ScreenCaptureLib.Presentation.RegionCapture;
using SkiaSharp;
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MessageBox = ShareX.AvaloniaUI.MessageBox;
using MessageBoxButtons = ShareX.AvaloniaUI.MessageBoxButtons;
using MessageBoxIcon = ShareX.AvaloniaUI.MessageBoxIcon;

namespace ShareX.Screenshots;

/// <summary>Region, full screen and scrolling screenshots; each is saved and/or copied to the clipboard.</summary>
public static class ScreenshotController
{
    private static int busy;

    /// <summary>
    /// Raised on the UI thread after a screenshot is handled; the path is null when it was not saved.
    /// The image is disposed after the event, so copy anything that must outlive it.
    /// </summary>
    public static event Action<SKBitmap, string?>? ScreenshotTaken;

    public static Task CaptureRegionAsync() => RunAsync(CaptureRegionCoreAsync);

    public static Task CaptureFullscreenAsync() => RunAsync(() => Task.FromResult<SKBitmap?>(CreateScreenshot().CaptureFullscreen()));

    /// <summary>
    /// Opens the scrolling capture window, or starts/stops capturing when it is already open.
    /// Its Save button always writes the stitched image to a file.
    /// </summary>
    public static async Task StartStopScrollingCaptureAsync()
    {
        try
        {
            await ScrollingCaptureWindowIntegration.StartStopAsync(SettingsStore.Settings.ScrollingCaptureOptions, image =>
            {
                Finish(image, alwaysSave: true);
                SettingsStore.Save();
            });
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex);
            MessageBox.Show(ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>A unique file path in the screenshots folder, grouped by month.</summary>
    public static string GetOutputPath(string extension)
    {
        string folder = Path.Combine(SettingsStore.Settings.ScreenshotsFolder, DateTime.Now.ToString("yyyy-MM"));
        Directory.CreateDirectory(folder);
        string name = $"{AppInfo.Id}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        string path = Path.Combine(folder, $"{name}.{extension}");
        for (int index = 2; File.Exists(path); index++) path = Path.Combine(folder, $"{name}_{index}.{extension}");
        return path;
    }

    private static Screenshot CreateScreenshot() => new()
    {
        CaptureCursor = SettingsStore.Settings.ScreenshotShowCursor,
        RemoveOutsideScreenArea = true
    };

    private static async Task<SKBitmap?> CaptureRegionCoreAsync()
    {
        AppSettings settings = SettingsStore.Settings;
        Screenshot screenshot = CreateScreenshot();
        screenshot.CaptureCursor = false;
        bool activeMonitorMode = settings.RegionCaptureOptions.ActiveMonitorMode;
        Rectangle screenBounds = activeMonitorMode ? CaptureHelpers.GetActiveScreenBounds() : CaptureHelpers.GetScreenBounds();
        SKBitmap frozenScreenshot;
        using (SKBitmap canvas = activeMonitorMode ? screenshot.CaptureActiveMonitor() : screenshot.CaptureFullscreen())
        {
            frozenScreenshot = canvas.Copy();
        }

        SKBitmap? cursorBitmap = null;
        Point cursorPosition = Point.Empty;
        if (settings.ScreenshotShowCursor)
        {
            CursorData cursorData = new();
            if (cursorData.IsVisible)
            {
                using SKBitmap cursor = cursorData.ToBitmap(frozenScreenshot, screenBounds.Location);
                cursorBitmap = cursor.Copy();
                cursorPosition = new Point(cursorData.DrawPosition.X - screenBounds.X, cursorData.DrawPosition.Y - screenBounds.Y);
            }
        }

        AvaloniaRegionCaptureResult? result = await RegionCaptureIntegration.CaptureAsync(new AvaloniaRegionCaptureRequest
        {
            Screenshot = frozenScreenshot,
            ScreenBounds = screenBounds,
            RegionCaptureOptions = settings.RegionCaptureOptions,
            ImageEditorOptions = settings.ImageEditorOptions,
            EnableAnnotations = settings.RegionCaptureAnnotations,
            CursorBitmap = cursorBitmap,
            CursorPosition = cursorPosition
        });
        if (result == null) return null;
        using (result.Image) return result.Image.Copy();
    }

    private static async Task RunAsync(Func<Task<SKBitmap?>> capture)
    {
        // Hotkeys can repeat; one capture at a time.
        if (Interlocked.Exchange(ref busy, 1) != 0) return;
        try
        {
            SKBitmap? image = await capture();
            if (image == null) return;
            Finish(image);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex);
            MessageBox.Show(ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Interlocked.Exchange(ref busy, 0);
            SettingsStore.Save();
        }
    }

    private static void Finish(SKBitmap image, bool alwaysSave = false)
    {
        AppSettings settings = SettingsStore.Settings;
        string? path = null;
        if (alwaysSave || settings.SaveScreenshotToFile)
        {
            path = GetOutputPath("png");
            if (!SkiaImageHelpers.SaveImage(image, path)) path = null;
            else DebugHelper.WriteLine("Screenshot saved: " + path);
        }
        using (image)
        {
            if (settings.CopyScreenshotToClipboard) ClipboardHelpers.CopyImage(image, path == null ? null : Path.GetFileName(path));
            ScreenshotTaken?.Invoke(image, path);
        }
    }
}
