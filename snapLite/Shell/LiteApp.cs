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

using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using ShareX.AvaloniaUI.Integration;
using ShareX.HelpersLib;
using ShareX.ImageEditor.Integration;
using ShareX.Screenshots;
using ShareX.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HelpersStrings = ShareX.HelpersLib.Localization.Strings;

namespace ShareX;

/// <summary>The tray icon, its menu and the global hotkeys; everything else is started from here.</summary>
internal static class LiteApp
{
    private static readonly Dictionary<ushort, LiteAction> hotkeyActions = [];
    private static readonly Dictionary<LiteAction, HotkeyInfo> hotkeys = [];
    private static WindowsHotkeyHost? hotkeyHost;
    private static WindowsTrayIcon? trayIcon;
    private static SettingsWindow? settingsWindow;

    public static Task StartAsync()
    {
        ImageEditorIntegration.Initialize();

        hotkeyHost = new WindowsHotkeyHost();
        hotkeyHost.Initialize();
        hotkeyHost.HotkeyPress += (id, _, _) =>
        {
            if (hotkeyActions.TryGetValue(id, out LiteAction action)) Dispatcher.UIThread.Post(() => Run(action));
        };
        RegisterHotkeys();
        CreateTrayIcon();

        ScreenshotController.ScreenshotTaken += (image, path) =>
        {
            if (!SettingsStore.Settings.ShowScreenshotNotification) return;
            NotificationWindow.Show(path != null ? Localization.Strings.Lite_ScreenshotSaved : Localization.Strings.Lite_ScreenshotCopied, path, image);
        };
        RecordingController.RecordingSaved += path =>
        {
            if (SettingsStore.Settings.ShowScreenshotNotification) NotificationWindow.Show(Localization.Strings.Lite_RecordingSaved, path);
        };

        NotificationWindow.Show(Localization.Strings.Lite_RunningInTray, null);
        DebugHelper.WriteLine(AppInfo.Id + " started.");
        return Task.CompletedTask;
    }

    public static string GetActionName(LiteAction action) => action switch
    {
        LiteAction.CaptureRegion => HelpersStrings.HotkeyType_RectangleRegion,
        LiteAction.CaptureFullscreen => HelpersStrings.HotkeyType_PrintScreen,
        LiteAction.ScrollingCapture => HelpersStrings.HotkeyType_ScrollingCapture,
        _ => HelpersStrings.HotkeyType_ScreenRecorder
    };

    public static void Run(LiteAction action)
    {
        switch (action)
        {
            case LiteAction.CaptureRegion: _ = ScreenshotController.CaptureRegionAsync(); break;
            case LiteAction.CaptureFullscreen: _ = ScreenshotController.CaptureFullscreenAsync(); break;
            case LiteAction.ScrollingCapture: _ = ScreenshotController.StartStopScrollingCaptureAsync(); break;
            case LiteAction.RecordScreen: RecordingController.StartStop(); break;
        }
    }

    /// <summary>(Re)registers every hotkey from the settings. Failed ones report <see cref="HotkeyStatus.Failed"/>.</summary>
    public static void RegisterHotkeys()
    {
        if (hotkeyHost == null) return;
        foreach (HotkeyInfo info in hotkeys.Values) hotkeyHost.UnregisterHotkey(info);
        hotkeys.Clear();
        hotkeyActions.Clear();
        foreach (LiteAction action in Enum.GetValues<LiteAction>())
        {
            HotkeyInfo info = new(SettingsStore.Settings[action]);
            hotkeyHost.RegisterHotkey(info);
            hotkeys[action] = info;
            if (info.Status == HotkeyStatus.Registered) hotkeyActions[info.ID] = action;
            else if (info.Status == HotkeyStatus.Failed) DebugHelper.WriteLine($"Hotkey in use by another application: {info} ({action})");
        }
    }

    public static HotkeyStatus GetHotkeyStatus(LiteAction action) =>
        hotkeys.TryGetValue(action, out HotkeyInfo? info) ? info.Status : HotkeyStatus.NotConfigured;

    public static void ShowSettings()
    {
        if (settingsWindow != null)
        {
            settingsWindow.Activate();
            return;
        }
        settingsWindow = new SettingsWindow();
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }

    /// <summary>The tray menu's rows, with the current hotkeys; null is a separator.</summary>
    private static List<TrayMenuWindow.Entry?> GetMenuEntries()
    {
        List<TrayMenuWindow.Entry?> entries = [];
        foreach (LiteAction action in Enum.GetValues<LiteAction>())
        {
            if (action == LiteAction.RecordScreen) entries.Add(null);
            LiteAction captured = action;
            InputKey hotkey = SettingsStore.Settings[action];
            entries.Add(new(GetActionName(action), hotkey == InputKey.None ? null : new HotkeyInfo(hotkey).ToString(), () => Run(captured)));
        }
        entries.Add(null);
        entries.Add(new(HelpersStrings.HotkeyType_OpenScreenshotsFolder, null, OpenScreenshotsFolder));
        entries.Add(new(Localization.Strings.ApplicationSettingsWindow_Settings, null, ShowSettings));
        entries.Add(null);
        entries.Add(new(Localization.Strings.MainMenuBuilder_Exit, null, Exit));
        return entries;
    }

    // A native notification-area icon: Avalonia's tray icon reports only left clicks and always shows its own
    // menu on right click, but both buttons should open the same menu.
    private static void CreateTrayIcon()
    {
        trayIcon = new WindowsTrayIcon(hotkeyHost!);
        using (Stream stream = AssetLoader.Open(new Uri("avares://snapLite/snapLite.ico")))
        using (MemoryStream bytes = new())
        {
            stream.CopyTo(bytes);
            trayIcon.SetIcon(bytes.ToArray());
        }
        trayIcon.ToolTipText = AppInfo.Name;
        trayIcon.MouseUp += button =>
        {
            if (button is InputMouseButton.Left or InputMouseButton.Right)
                Dispatcher.UIThread.Post(() => TrayMenuWindow.Toggle(GetMenuEntries()));
        };
        trayIcon.Visible = true;
    }

    public static void OpenScreenshotsFolder()
    {
        string folder = SettingsStore.Settings.ScreenshotsFolder;
        Directory.CreateDirectory(folder);
        FileHelpers.OpenFolder(folder);
    }

    public static void Exit()
    {
        RecordingController.Stop();
        // The tray icon lives on the hotkey host's window, so remove it first.
        trayIcon?.Dispose();
        trayIcon = null;
        if (hotkeyHost != null)
        {
            foreach (HotkeyInfo info in hotkeys.Values) hotkeyHost.UnregisterHotkey(info);
            hotkeyHost.Dispose();
            hotkeyHost = null;
        }
        MouseHighlighterManager.Shutdown();
        SettingsStore.Save();
        AvaloniaBootstrapper.Shutdown();
    }
}
