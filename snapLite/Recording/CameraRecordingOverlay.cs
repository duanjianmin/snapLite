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

#nullable enable

using Avalonia.Threading;
using ShareX.HelpersLib;
using ShareX.ScreenRecordingLib;
using SkiaSharp;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ShareX;

/// <summary>
/// Shows the recorded camera on screen while recording, at the overlay's position, and lets it be dragged.
/// The window is excluded from capture, so the video shows only the recorder's own overlay, which follows it.
/// </summary>
internal sealed class CameraRecordingOverlay : IDisposable
{
    private const int WM_NCHITTEST = 0x0084, WM_MOUSEACTIVATE = 0x0021, WM_MOVING = 0x0216,
        WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232, HTCAPTION = 2, MA_NOACTIVATE = 3;

    private readonly ScreenRecorder recorder;
    private readonly CameraOverlayShape shape;
    private readonly Func<Rectangle> recordingScreenBounds;
    private readonly WindowsNativeWindow window;
    private readonly object sync = new();
    private byte[] incoming = [];
    private int incomingWidth, incomingHeight;
    private Rectangle incomingBounds;
    private Size incomingFrame;
    private bool framePosted, disposed, visible, dragging;
    private Rectangle screenBounds;
    // Where a drag left the overlay, in recording pixels, until the recorder reports it back.
    private Point? requestedLocation;
    private SKSurface? surface;
    private IntPtr dc, bitmap, previousBitmap;
    private int bufferWidth, bufferHeight;

    private CameraRecordingOverlay(ScreenRecorder recorder, CameraOverlayShape shape, Func<Rectangle> recordingScreenBounds)
    {
        this.recorder = recorder;
        this.shape = shape;
        this.recordingScreenBounds = recordingScreenBounds;
        window = new WindowsNativeWindow(AppInfo.Title("Camera"), new Rectangle(0, 0, 1, 1),
            WindowStyles.WS_EX_LAYERED | WindowStyles.WS_EX_TOOLWINDOW | WindowStyles.WS_EX_NOACTIVATE);
        window.MessageReceived += OnNativeMessage;
        if (!NativeMethods.SetWindowDisplayAffinity(window.Handle, 0x11)) // WDA_EXCLUDEFROMCAPTURE
            DebugHelper.WriteLine("Could not exclude the camera preview from capture. Win32 error: {0}", Marshal.GetLastWin32Error());
        recorder.CameraFrameRecorded += OnFrameRecorded;
    }

    /// <param name="recordingScreenBounds">The recorded area on screen, in physical pixels; read on the UI thread.</param>
    public static Task<IDisposable> ShowAsync(ScreenRecorder recorder, CameraOverlayShape shape, Func<Rectangle> recordingScreenBounds) =>
        Dispatcher.UIThread.InvokeAsync(() => (IDisposable)new CameraRecordingOverlay(recorder, shape, recordingScreenBounds)).GetTask();

    // Recording thread: keep only the newest frame and draw it on the UI thread.
    private void OnFrameRecorded(byte[] pixels, int width, int height, int stride, Rectangle bounds, Size frame)
    {
        lock (sync)
        {
            if (disposed) return;
            int bytes = width * height * 4;
            if (incoming.Length != bytes) incoming = new byte[bytes];
            for (int row = 0; row < height; row++)
                Buffer.BlockCopy(pixels, row * stride, incoming, row * width * 4, width * 4);
            incomingWidth = width; incomingHeight = height;
            incomingBounds = bounds; incomingFrame = frame;
            if (framePosted) return;
            framePosted = true;
        }
        Dispatcher.UIThread.Post(Present);
    }

    private void Present()
    {
        lock (sync)
        {
            framePosted = false;
            if (disposed) return;
            try { Draw(); }
            catch (Exception ex)
            {
                // The preview is a convenience; never let it interrupt the recording.
                DebugHelper.WriteException(ex);
                recorder.CameraFrameRecorded -= OnFrameRecorded;
                NativeMethods.ShowWindow(window.Handle, 0); // SW_HIDE
            }
        }
    }

    private void Draw()
    {
        Rectangle area = recordingScreenBounds();
        if (area.Width <= 0 || area.Height <= 0 || incomingFrame.Width <= 0 || incomingFrame.Height <= 0) return;
        double scaleX = (double)area.Width / incomingFrame.Width, scaleY = (double)area.Height / incomingFrame.Height;
        if (requestedLocation is Point requested && requested == incomingBounds.Location) requestedLocation = null;
        Size size = new(Math.Max(1, (int)Math.Round(incomingBounds.Width * scaleX)), Math.Max(1, (int)Math.Round(incomingBounds.Height * scaleY)));
        // Follow the recorder unless a drag is in progress or its result has not been recorded yet.
        Point location = dragging || requestedLocation != null ? screenBounds.Location
            : new(area.X + (int)Math.Round(incomingBounds.X * scaleX), area.Y + (int)Math.Round(incomingBounds.Y * scaleY));
        screenBounds = new(location, size);

        EnsureBuffer(size.Width, size.Height);
        SKCanvas canvas = surface!.Canvas;
        canvas.Clear(SKColors.Transparent);
        SKRect tile = new(0, 0, size.Width, size.Height);
        using (SKPath clip = new())
        {
            if (shape.IsElliptical()) clip.AddOval(tile);
            else
            {
                float radius = (float)shape.GetCornerRadius(size.Width, size.Height);
                clip.AddRoundRect(tile, radius, radius);
            }
            canvas.Save();
            canvas.ClipPath(clip, SKClipOperation.Intersect, true);
            GCHandle pinned = GCHandle.Alloc(incoming, GCHandleType.Pinned);
            try
            {
                SKImageInfo info = new(incomingWidth, incomingHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
                using SKImage? image = SKImage.FromPixels(info, pinned.AddrOfPinnedObject(), incomingWidth * 4);
                if (image != null) canvas.DrawImage(image, tile, new SKSamplingOptions(SKFilterMode.Linear));
            }
            finally { pinned.Free(); }
            canvas.Restore();
        }
        canvas.Flush();

        POINT destination = new(location.X, location.Y), source = new(0, 0);
        SIZE layeredSize = new(size.Width, size.Height);
        BLENDFUNCTION blend = new() { SourceConstantAlpha = 255, AlphaFormat = NativeConstants.AC_SRC_ALPHA };
        if (!NativeMethods.UpdateLayeredWindow(window.Handle, IntPtr.Zero, ref destination, ref layeredSize, dc, ref source, 0, ref blend, NativeConstants.ULW_ALPHA))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!visible)
        {
            NativeMethods.SetWindowPos(window.Handle, new IntPtr(NativeConstants.HWND_TOPMOST), location.X, location.Y, size.Width, size.Height,
                SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_SHOWWINDOW);
            visible = true;
        }
    }

    private void OnNativeMessage(object? sender, NativeWindowMessageEventArgs message)
    {
        switch (message.Message)
        {
            case WM_NCHITTEST:
                // Drag from anywhere; fully transparent corners already pass clicks through.
                message.Result = new IntPtr(HTCAPTION); message.Handled = true;
                break;
            case WM_MOUSEACTIVATE:
                message.Result = new IntPtr(MA_NOACTIVATE); message.Handled = true;
                break;
            case WM_ENTERSIZEMOVE:
                lock (sync) dragging = true;
                break;
            case WM_EXITSIZEMOVE:
                lock (sync) dragging = false;
                break;
            case WM_MOVING:
                {
                    NativeRect proposed = Marshal.PtrToStructure<NativeRect>(message.LParam);
                    lock (sync)
                    {
                        // Keep the camera inside the recording, as the recorder does.
                        Rectangle area = recordingScreenBounds();
                        int width = proposed.Right - proposed.Left, height = proposed.Bottom - proposed.Top;
                        int x = Math.Clamp(proposed.Left, area.Left, Math.Max(area.Left, area.Right - width));
                        int y = Math.Clamp(proposed.Top, area.Top, Math.Max(area.Top, area.Bottom - height));
                        Marshal.StructureToPtr(new NativeRect { Left = x, Top = y, Right = x + width, Bottom = y + height }, message.LParam, false);
                        screenBounds = new(x, y, width, height);
                        if (incomingFrame.Width > 0 && area.Width > 0 && area.Height > 0)
                        {
                            Point location = new((int)Math.Round((x - area.X) * (double)incomingFrame.Width / area.Width),
                                (int)Math.Round((y - area.Y) * (double)incomingFrame.Height / area.Height));
                            requestedLocation = location;
                            recorder.MoveCamera(location);
                        }
                    }
                    message.Result = new IntPtr(1); message.Handled = true;
                    break;
                }
        }
    }

    private void EnsureBuffer(int width, int height)
    {
        if (surface != null && width == bufferWidth && height == bufferHeight) return;
        ReleaseBuffer();
        bufferWidth = width; bufferHeight = height;
        dc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
        BITMAPINFOHEADER info = new(width, height, 32) { biHeight = -height };
        bitmap = NativeMethods.CreateDIBSection(dc, ref info, 0, out IntPtr pixels, IntPtr.Zero, 0);
        if (dc == IntPtr.Zero || bitmap == IntPtr.Zero || pixels == IntPtr.Zero)
        {
            ReleaseBuffer();
            throw new InvalidOperationException("Could not allocate the camera preview bitmap.");
        }
        previousBitmap = NativeMethods.SelectObject(dc, bitmap);
        surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), pixels, width * 4)
            ?? throw new InvalidOperationException("Could not create the camera preview surface.");
    }

    private void ReleaseBuffer()
    {
        surface?.Dispose();
        surface = null;
        if (previousBitmap != IntPtr.Zero) NativeMethods.SelectObject(dc, previousBitmap);
        if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
        if (dc != IntPtr.Zero) NativeMethods.DeleteDC(dc);
        previousBitmap = bitmap = dc = IntPtr.Zero;
    }

    public void Dispose()
    {
        recorder.CameraFrameRecorded -= OnFrameRecorded;
        Dispatcher.UIThread.Post(() =>
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                ReleaseBuffer();
                window.Dispose();
            }
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }
}
