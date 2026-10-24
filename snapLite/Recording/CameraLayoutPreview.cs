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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ShareX.Localization;
using ShareX.ScreenRecordingLib;
using System;
using System.Globalization;
using DrawingRectangle = System.Drawing.Rectangle;

namespace ShareX;

/// <summary>Where the camera will appear in the recording. With a live frame, the tile shows the camera image.</summary>
internal sealed class CameraLayoutPreview(RecorderSettings settings, Func<DrawingRectangle> region) : Control
{
    /// <summary>The latest preview frame, owned by the caller. Call InvalidateVisual after it changes.</summary>
    public Bitmap? Frame { get; set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        DrawingRectangle target = region();
        if (target.Width <= 0 || target.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        double scale = Math.Min(Bounds.Width / target.Width, Bounds.Height / target.Height);
        Rect screen = new((Bounds.Width - target.Width * scale) / 2, (Bounds.Height - target.Height * scale) / 2,
            target.Width * scale, target.Height * scale);
        IBrush accent = this.FindResource("ShareX.Brush.Accent") as IBrush ?? Brushes.DodgerBlue;
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)), new Pen(accent, 1), screen);
        double aspect = Frame != null ? (double)Frame.PixelSize.Width / Frame.PixelSize.Height
            : settings.ScreenRecordCameraResolution == CameraCaptureResolution.Size640x480 ? 4d / 3 : 16d / 9;
        int marginPixels = Math.Min(settings.ScreenRecordCameraMargin, Math.Max(0, Math.Min(target.Width, target.Height) / 2 - 1));
        int availableWidth = target.Width - marginPixels * 2, availableHeight = target.Height - marginPixels * 2;
        int widthPixels = Math.Clamp(target.Width * settings.ScreenRecordCameraWidthPercent / 100, 2, availableWidth);
        bool square = settings.ScreenRecordCameraShape.IsSquareCrop();
        int heightPixels = square ? widthPixels : Math.Max(2, (int)(widthPixels / aspect));
        if (heightPixels > availableHeight)
        {
            heightPixels = availableHeight;
            widthPixels = square ? heightPixels : Math.Clamp((int)(heightPixels * aspect), 2, availableWidth);
        }
        double width = widthPixels * scale, height = heightPixels * scale, margin = marginPixels * scale;
        bool left = settings.ScreenRecordCameraPosition is CameraOverlayPosition.TopLeft or CameraOverlayPosition.BottomLeft;
        bool top = settings.ScreenRecordCameraPosition is CameraOverlayPosition.TopLeft or CameraOverlayPosition.TopRight;
        Rect camera = new(left ? screen.Left + margin : screen.Right - margin - width,
            top ? screen.Top + margin : screen.Bottom - margin - height, width, height);
        CameraShapeDrawing.Draw(context, camera, settings.ScreenRecordCameraShape, Frame, accent);
    }
}

/// <summary>A large live camera image in the selected shape, cropped exactly as the recording overlay crops it.</summary>
internal sealed class CameraLivePreview(RecorderSettings settings) : Control
{
    /// <summary>The latest preview frame, owned by the caller. Call InvalidateVisual after it changes.</summary>
    public Bitmap? Frame { get; set; }
    /// <summary>Shown instead of the image while the camera starts or when it is unavailable.</summary>
    public string? Status { get; set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        CameraOverlayShape shape = settings.ScreenRecordCameraShape;
        double aspect = shape.IsSquareCrop() ? 1 : Frame != null ? (double)Frame.PixelSize.Width / Frame.PixelSize.Height
            : settings.ScreenRecordCameraResolution == CameraCaptureResolution.Size640x480 ? 4d / 3 : 16d / 9;
        double width = Math.Min(Bounds.Width, Bounds.Height * aspect), height = width / aspect;
        Rect tile = new((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
        IBrush placeholder = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
        CameraShapeDrawing.Draw(context, tile, shape, Frame, placeholder);
        if (Frame == null && !string.IsNullOrEmpty(Status))
        {
            IBrush foreground = this.FindResource("ShareX.Brush.Text") as IBrush ?? Brushes.Gray;
            FormattedText text = new(Status, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 12, foreground)
            {
                MaxTextWidth = Math.Max(1, tile.Width - 16),
                TextAlignment = TextAlignment.Center
            };
            context.DrawText(text, new Point(tile.X + (tile.Width - text.Width) / 2, tile.Center.Y - text.Height / 2));
        }
    }
}

internal static class CameraShapeDrawing
{
    public static string GetName(CameraOverlayShape shape) => shape switch
    {
        CameraOverlayShape.Circle => Strings.TaskSettingsWindow_NativeRecorderCamera_Circle,
        CameraOverlayShape.RoundedRectangle => Strings.TaskSettingsWindow_NativeRecorderCamera_RoundedRectangle,
        CameraOverlayShape.Square => Strings.TaskSettingsWindow_NativeRecorderCamera_Square,
        CameraOverlayShape.Ellipse => Strings.TaskSettingsWindow_NativeRecorderCamera_Ellipse,
        _ => Strings.TaskSettingsWindow_NativeRecorderCamera_Rectangle
    };

    /// <summary>Fills the shape with the camera frame (center-cropped to the tile) or, without one, with a brush.</summary>
    public static void Draw(DrawingContext context, Rect tile, CameraOverlayShape shape, Bitmap? frame, IBrush fallback)
    {
        double radius = shape.GetCornerRadius(tile.Width, tile.Height);
        Geometry geometry = shape.IsElliptical() ? new EllipseGeometry(tile)
            // Plain rectangles keep the slight rounding of the original layout illustration.
            : new RectangleGeometry(tile, Math.Max(radius, frame == null ? 2 : 0), Math.Max(radius, frame == null ? 2 : 0));
        if (frame == null)
        {
            context.DrawGeometry(fallback, null, geometry);
            return;
        }
        double frameWidth = frame.PixelSize.Width, frameHeight = frame.PixelSize.Height;
        double tileAspect = tile.Width / tile.Height;
        Rect source = frameWidth / frameHeight > tileAspect
            ? new Rect((frameWidth - frameHeight * tileAspect) / 2, 0, frameHeight * tileAspect, frameHeight)
            : new Rect(0, (frameHeight - frameWidth / tileAspect) / 2, frameWidth, frameWidth / tileAspect);
        using (context.PushGeometryClip(geometry)) context.DrawImage(frame, source, tile);
    }
}
