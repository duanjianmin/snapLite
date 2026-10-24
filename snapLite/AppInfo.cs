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

namespace ShareX;

/// <summary>The app's two names.</summary>
internal static class AppInfo
{
    /// <summary>Folders, file names, logs and the single-instance names. Never localized, so data stays in one place.</summary>
    public const string Id = "snapLite";

    /// <summary>What users see: 极简录屏 in Chinese, snapLite in every other language.</summary>
    public static string Name => Localization.Strings.Lite_AppName;

    /// <summary>A window title in the "Name - Part" form.</summary>
    public static string Title(string part) => $"{Name} - {part}";
}
