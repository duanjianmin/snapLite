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

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ShareX.HelpersLib;
using System;
using System.IO;

namespace ShareX;

/// <summary>Loads and saves <see cref="AppSettings"/> in %APPDATA%\snapLite.</summary>
public static class SettingsStore
{
    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Converters = { new StringEnumConverter() }
    };

    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Id);
    public static string SettingsPath => Path.Combine(Folder, "Settings.json");
    public static string LogPath => Path.Combine(Folder, "Logs", $"{AppInfo.Id}-Log-{DateTime.Now:yyyy-MM}.txt");

    public static AppSettings Settings { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                Settings = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsPath), JsonSettings) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            // A damaged file must not stop the app; keep it for inspection and start with defaults.
            DebugHelper.WriteException(ex, "Settings could not be loaded; defaults are used.");
            try { File.Copy(SettingsPath, SettingsPath + ".bad", true); } catch { }
            Settings = new AppSettings();
        }
        Settings.UpgradeHotkeyDefaults();
    }

    public static void Save()
    {
        lock (SyncRoot)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string temp = SettingsPath + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(Settings, JsonSettings));
                File.Move(temp, SettingsPath, true);
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "Settings could not be saved.");
            }
        }
    }
}
