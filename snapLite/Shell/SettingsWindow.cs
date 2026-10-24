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
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using HelpersStrings = ShareX.HelpersLib.Localization.Strings;
using Strings = ShareX.Localization.Strings;

namespace ShareX;

/// <summary>Screenshot, recording and hotkey settings. Every change is saved immediately.</summary>
internal sealed class SettingsWindow : Window
{
    private readonly Dictionary<LiteAction, HotkeyBox> hotkeyBoxes = [];

    public SettingsWindow()
    {
        AppSettings settings = SettingsStore.Settings;
        RequestedThemeVariant = ThemeManager.GetCurrentTheme();
        Title = AppInfo.Title(Strings.ApplicationSettingsWindow_Settings);
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://snapLite/snapLite.ico")));
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        StackPanel root = new() { Spacing = 8, Margin = new Thickness(20) };

        root.Children.Add(Header(Strings.MainMenuBuilder_Capture));
        TextBox folder = new() { Text = settings.ScreenshotsFolder, HorizontalAlignment = HorizontalAlignment.Stretch };
        folder.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(folder.Text)) folder.Text = settings.ScreenshotsFolder;
            else Save(() => settings.ScreenshotsFolder = folder.Text.Trim());
        };
        Button browse = new() { Content = Strings.ApplicationSettingsWindow_Browse };
        browse.Click += async (_, _) =>
        {
            IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Strings.ApplicationSettingsWindow_ScreenshotsFolder,
                AllowMultiple = false
            });
            string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
            if (path == null) return;
            folder.Text = path;
            Save(() => settings.ScreenshotsFolder = path);
        };
        Button open = new() { Content = Strings.MainMenuBuilder_Open };
        open.Click += (_, _) => LiteApp.OpenScreenshotsFolder();
        Grid folderRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        folderRow.Children.Add(folder);
        Grid.SetColumn(browse, 1);
        folderRow.Children.Add(browse);
        Grid.SetColumn(open, 2);
        folderRow.Children.Add(open);
        root.Children.Add(new TextBlock { Text = Strings.ApplicationSettingsWindow_ScreenshotsFolder });
        root.Children.Add(folderRow);
        root.Children.Add(Check(HelpersStrings.AfterCaptureTasks_SaveImageToFile, settings.SaveScreenshotToFile, value => settings.SaveScreenshotToFile = value));
        root.Children.Add(Check(HelpersStrings.AfterCaptureTasks_CopyImageToClipboard, settings.CopyScreenshotToClipboard, value => settings.CopyScreenshotToClipboard = value));
        root.Children.Add(Check(Strings.Lite_RegionAnnotations, settings.RegionCaptureAnnotations, value => settings.RegionCaptureAnnotations = value));
        root.Children.Add(Check(Strings.MainMenuBuilder_ShowCursor, settings.ScreenshotShowCursor, value => settings.ScreenshotShowCursor = value));
        root.Children.Add(Check(Strings.Lite_ShowNotification, settings.ShowScreenshotNotification, value => settings.ShowScreenshotNotification = value));

        root.Children.Add(Header(HelpersStrings.HotkeyType_Category_ScreenRecord));
        root.Children.Add(Check(Strings.ScreenRecorderBar_ShowBeforeRecording, settings.Recorder.ScreenRecordShowBar,
            value => settings.Recorder.ScreenRecordShowBar = value));
        root.Children.Add(new TextBlock { Text = Strings.Lite_RecorderOptionsHint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });

        root.Children.Add(Header(Strings.ApplicationSettingsWindow_AdvancedHotkeys));
        Grid hotkeyGrid = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowSpacing = 6 };
        int row = 0;
        foreach (LiteAction action in Enum.GetValues<LiteAction>())
        {
            hotkeyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            TextBlock label = new() { Text = LiteApp.GetActionName(action), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, row);
            hotkeyGrid.Children.Add(label);
            LiteAction captured = action;
            HotkeyBox box = new(settings[action]);
            box.HotkeyChanged += key =>
            {
                Save(() => settings[captured] = key);
                LiteApp.RegisterHotkeys();
                UpdateHotkeyStatus();
            };
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            hotkeyGrid.Children.Add(box);
            hotkeyBoxes[action] = box;
            row++;
        }
        root.Children.Add(hotkeyGrid);
        UpdateHotkeyStatus();

        Content = new ScrollViewer { Content = root };
    }

    private void UpdateHotkeyStatus()
    {
        foreach ((LiteAction action, HotkeyBox box) in hotkeyBoxes)
            box.Status = LiteApp.GetHotkeyStatus(action) == HotkeyStatus.Failed ? Strings.Lite_HotkeyInUse : null;
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 12, 0, 2)
    };

    private static CheckBox Check(string text, bool value, Action<bool> changed)
    {
        CheckBox check = new() { Content = text, IsChecked = value };
        check.IsCheckedChanged += (_, _) => Save(() => changed(check.IsChecked == true));
        return check;
    }

    private static void Save(Action change)
    {
        change();
        SettingsStore.Save();
    }
}
