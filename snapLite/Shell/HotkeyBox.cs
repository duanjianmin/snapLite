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
using Avalonia.Input;
using Avalonia.Interactivity;
using ShareX.HelpersLib;
using System;

namespace ShareX;

/// <summary>
/// A button that records the next key combination. Escape cancels; Backspace or Delete clears the hotkey.
/// </summary>
internal sealed class HotkeyBox : Button
{
    private InputKey hotkey;
    private bool listening;

    public event Action<InputKey>? HotkeyChanged;

    protected override Type StyleKeyOverride => typeof(Button);

    public HotkeyBox(InputKey hotkey)
    {
        this.hotkey = hotkey;
        MinWidth = 220;
        UpdateText();
        Click += (_, _) =>
        {
            listening = true;
            Content = Localization.Strings.Lite_HotkeyPrompt;
            Focus();
        };
        LostFocus += (_, _) => StopListening();
        // Windows sends no key-down for Print Screen, so the release completes the combination too.
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => { if (listening && e.Key == Key.PrintScreen) OnKey(this, e); }, RoutingStrategies.Tunnel);
    }

    public InputKey Hotkey
    {
        get => hotkey;
        set { hotkey = value; UpdateText(); }
    }

    /// <summary>Shown after the hotkey text, for example when Windows refused to register it.</summary>
    public string? Status
    {
        get => status;
        set { status = value; UpdateText(); }
    }
    private string? status;

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (!listening) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            StopListening();
            return;
        }
        if (e.Key is Key.Back or Key.Delete)
        {
            Commit(InputKey.None);
            return;
        }
        InputKey key = Convert(e.Key);
        // Wait for a non-modifier key; modifiers alone are not a hotkey.
        if (key is InputKey.None or InputKey.ControlKey or InputKey.ShiftKey or InputKey.Menu || e.Key is Key.LWin or Key.RWin) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) key |= InputKey.Control;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) key |= InputKey.Shift;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) key |= InputKey.Alt;
        Commit(key);
    }

    private void Commit(InputKey key)
    {
        listening = false;
        hotkey = key;
        status = null;
        UpdateText();
        HotkeyChanged?.Invoke(key);
    }

    private void StopListening()
    {
        if (!listening) return;
        listening = false;
        UpdateText();
    }

    private void UpdateText()
    {
        string text = new HotkeyInfo(hotkey).ToString();
        Content = string.IsNullOrEmpty(status) ? text : $"{text}  ({status})";
    }

    // Same mapping as ShareX's hotkey settings.
    private static InputKey Convert(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => InputKey.ControlKey,
        Key.LeftShift or Key.RightShift => InputKey.ShiftKey,
        Key.LeftAlt or Key.RightAlt => InputKey.Menu,
        Key.Enter => InputKey.Enter,
        Key.CapsLock => InputKey.CapsLock,
        Key.PageUp => InputKey.PageUp,
        Key.PageDown => InputKey.PageDown,
        Key.PrintScreen => InputKey.PrintScreen,
        _ when Enum.TryParse(key.ToString(), true, out InputKey parsed) => parsed,
        _ => InputKey.None
    };
}
