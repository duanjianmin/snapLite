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

using ShareX.ScreenRecordingLib.Native;
using Vortice.MediaFoundation;

namespace ShareX.ScreenRecordingLib;

/// <summary>A Windows camera. Id is its persistent Media Foundation symbolic link.</summary>
public sealed record CameraCaptureDevice(string Id, string Name);

public static unsafe class CameraCaptureDevices
{
    public static IReadOnlyList<CameraCaptureDevice> GetCameras()
    {
        HRESULT initialized = NativeMethods.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED);
        if (initialized.Value != unchecked((int)0x80010106)) initialized.ThrowOnFailure(); // UI may already be STA.
        bool started = false;
        try
        {
            MediaFactory.MFStartup(true).CheckError();
            started = true;
            using var devices = MediaFactory.MFEnumVideoDeviceSources();
            List<CameraCaptureDevice> result = new();
            foreach (IMFActivate device in devices)
                result.Add(new(device.GetString(CaptureDeviceAttributeKeys.SourceTypeVidcapSymbolicLink),
                    device.GetString(CaptureDeviceAttributeKeys.FriendlyName)));
            return result;
        }
        finally
        {
            if (started) MediaFactory.MFShutdown();
            if (initialized.Succeeded) NativeMethods.CoUninitialize();
        }
    }
}

public enum CameraOverlayPosition { TopLeft, TopRight, BottomLeft, BottomRight }
// Append new shapes so saved settings keep their meaning.
public enum CameraOverlayShape { Rectangle, Circle, RoundedRectangle, Square, Ellipse }

public static class CameraOverlayShapes
{
    /// <summary>Circles and squares use a centered square crop; the other shapes preserve the full camera image.</summary>
    public static bool IsSquareCrop(this CameraOverlayShape shape) => shape is CameraOverlayShape.Circle or CameraOverlayShape.Square;
    /// <summary>Whether the tile needs an antialiased mask instead of a plain rectangle.</summary>
    public static bool IsMasked(this CameraOverlayShape shape) => shape is not (CameraOverlayShape.Rectangle or CameraOverlayShape.Square);
    public static bool IsElliptical(this CameraOverlayShape shape) => shape is CameraOverlayShape.Circle or CameraOverlayShape.Ellipse;
    /// <summary>Rounded rectangle corner radius for a tile of this size, in the tile's units.</summary>
    public static double GetCornerRadius(this CameraOverlayShape shape, double width, double height) =>
        shape == CameraOverlayShape.RoundedRectangle ? Math.Min(width, height) * 0.15 : 0;
}
public enum CameraCaptureResolution { Size640x480, Size1280x720, Size1920x1080 }

/// <summary>The camera's negotiated mode; actual delivery depends on the device.</summary>
public sealed record CameraCaptureInfo(string Name, int Width, int Height, double FramesPerSecond, string PixelFormat);
