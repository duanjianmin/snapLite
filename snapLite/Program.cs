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

using Avalonia.Threading;
using ShareX.AvaloniaUI.Integration;
using ShareX.HelpersLib;
using System;

namespace ShareX;

/// <summary>snapLite (极简录屏): screenshots and screen recording (with camera overlay), nothing else.</summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        DebugHelper.Init(SettingsStore.LogPath);
        // A second launch only asks the running instance to show itself.
        using SingleInstanceManager singleInstance = new(AppInfo.Id + ".Mutex", AppInfo.Id + ".Pipe", args);
        if (!singleInstance.IsFirstInstance) return;
        singleInstance.ArgumentsReceived += _ => Dispatcher.UIThread.Post(LiteApp.ShowSettings);

        DebugHelper.WriteLine(AppInfo.Id + " starting. Version: " + typeof(Program).Assembly.GetName().Version);
        DebugHelper.WriteLine("Settings: " + SettingsStore.SettingsPath);
        SettingsStore.Load();
        // Library windows, message boxes and the recording tray text show this name instead of "ShareX".
        AppIdentity.Name = AppInfo.Name;

        AvaloniaBootstrapper.Initialize(args, LiteApp.StartAsync, SettingsStore.Save);
        AvaloniaBootstrapper.Run();

        SettingsStore.Save();
        DebugHelper.WriteLine(AppInfo.Id + " closed.");
        DebugHelper.Flush();
    }
}
