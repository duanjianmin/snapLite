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
using ShareX.HelpersLib;
using ShareX.ImageEditor.Integration;
using ShareX.ScreenCaptureLib;
using ShareX.ScreenRecordingLib;
using ShareX.Tools;
using System;
using System.Drawing;
using System.IO;

namespace ShareX;

public enum LiteAction
{
    CaptureRegion,
    CaptureFullscreen,
    ScrollingCapture,
    RecordScreen
}

/// <summary>Everything the app remembers. Saved as JSON in %APPDATA%\snapLite.</summary>
public sealed class AppSettings
{
    /// <summary>Raise when the default hotkeys change; older settings files then switch to the new defaults once.</summary>
    private const int CurrentHotkeyDefaults = 2;

    /// <summary>Raise when another default changes; see <see cref="UpgradeDefaults"/>.</summary>
    private const int CurrentSettingsVersion = 1;

    // Screenshots
    public string ScreenshotsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), AppInfo.Id);
    public bool SaveScreenshotToFile = true;
    public bool CopyScreenshotToClipboard = true;
    public bool ShowScreenshotNotification = true;
    public bool ScreenshotShowCursor = false;
    public bool RegionCaptureAnnotations = true;
    // A region waits for Enter, double-click or the confirm button, so it can be adjusted and annotated first.
    public RegionCaptureOptions RegionCaptureOptions = new() { QuickCapture = false };
    public ImageEditorOptions ImageEditorOptions = new();
    public ScrollingCaptureOptions ScrollingCaptureOptions = new();

    // Screen recording
    public RecorderSettings Recorder = new();
    public MouseHighlighterOptions MouseHighlighter = new();
    public Rectangle LastRecordingRegion;

    public int SettingsVersion;

    // Global hotkeys. The Print Screen family follows Windows, Greenshot, Lightshot and ShareX,
    // and stays clear of the chat apps' screenshot keys (WeChat Alt+A, QQ Ctrl+Alt+A, DingTalk/Feishu Ctrl+Shift+A).
    public int HotkeyDefaultsVersion;
    public InputKey HotkeyCaptureRegion = InputKey.PrintScreen;
    public InputKey HotkeyCaptureFullscreen = InputKey.Control | InputKey.PrintScreen;
    public InputKey HotkeyScrollingCapture = InputKey.Control | InputKey.Alt | InputKey.PrintScreen;
    public InputKey HotkeyRecordScreen = InputKey.Shift | InputKey.PrintScreen;

    [JsonIgnore]
    public InputKey this[LiteAction action]
    {
        get => action switch
        {
            LiteAction.CaptureRegion => HotkeyCaptureRegion,
            LiteAction.CaptureFullscreen => HotkeyCaptureFullscreen,
            LiteAction.ScrollingCapture => HotkeyScrollingCapture,
            _ => HotkeyRecordScreen
        };
        set
        {
            switch (action)
            {
                case LiteAction.CaptureRegion: HotkeyCaptureRegion = value; break;
                case LiteAction.CaptureFullscreen: HotkeyCaptureFullscreen = value; break;
                case LiteAction.ScrollingCapture: HotkeyScrollingCapture = value; break;
                default: HotkeyRecordScreen = value; break;
            }
        }
    }

    /// <summary>Moves settings saved by older versions onto changed defaults, once per change.</summary>
    public void UpgradeDefaults()
    {
        UpgradeHotkeyDefaults();
        if (SettingsVersion < 1)
        {
            // 1.0.0 saved region captures as soon as the mouse was released.
            RegionCaptureOptions.QuickCapture = false;
        }
        SettingsVersion = CurrentSettingsVersion;
    }

    /// <summary>Moves settings saved with older default hotkeys onto the current defaults, once.</summary>
    private void UpgradeHotkeyDefaults()
    {
        if (HotkeyDefaultsVersion >= CurrentHotkeyDefaults) return;
        AppSettings defaults = new();
        foreach (LiteAction action in Enum.GetValues<LiteAction>()) this[action] = defaults[action];
        HotkeyDefaultsVersion = CurrentHotkeyDefaults;
    }
}

/// <summary>The native screen recorder's settings, named as in ShareX so the recorder bar ports unchanged.</summary>
public sealed class RecorderSettings
{
    public bool ScreenRecordShowBar = true;
    public bool ScreenRecordSystemAudio = true;
    public string ScreenRecordSystemAudioDeviceId = "";
    public float ScreenRecordSystemAudioGain = 1f;
    public bool ScreenRecordMicrophone = false;
    public string ScreenRecordMicrophoneDeviceId = "";
    public float ScreenRecordMicrophoneGain = 1f;
    public bool ScreenRecordCamera = false;
    public string ScreenRecordCameraDeviceId = "";
    public CameraCaptureResolution ScreenRecordCameraResolution = CameraCaptureResolution.Size1280x720;
    public int ScreenRecordCameraFPS = 30;
    public CameraOverlayPosition ScreenRecordCameraPosition = CameraOverlayPosition.BottomRight;
    public CameraOverlayShape ScreenRecordCameraShape = CameraOverlayShape.Circle;
    public int ScreenRecordCameraWidthPercent = 20;
    public int ScreenRecordCameraMargin = 20;
    public bool ScreenRecordRequireHardwareEncoder = false;
    public int ScreenRecordVideoBitrate = 10000; // kbps
    public int ScreenRecordFPS = 30;
    public bool ScreenRecordShowCursor = true;
    public bool ScreenRecordMouseHighlighter = false;
    public bool ScreenRecordShowTimer = true;
    public bool ScreenRecordShowButtonLabels = false;
    public bool ScreenRecordStartDelayEnabled = false;
    public float ScreenRecordStartDelay = 3f;
    public bool ScreenRecordFixedDuration = false;
    public float ScreenRecordDuration = 3f;
    public bool ScreenRecordAskConfirmationOnAbort = false;

    public RecorderSettings Copy() => (RecorderSettings)MemberwiseClone();
}
