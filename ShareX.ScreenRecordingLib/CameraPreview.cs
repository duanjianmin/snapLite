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
using SharpGen.Runtime;
using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace ShareX.ScreenRecordingLib;

/// <summary>
/// Receives one top-down BGRA frame. Alpha is undefined; treat the image as opaque. The array is reused
/// for later frames, so copy it during the call and never write to it.
/// </summary>
public delegate void CameraPreviewFrameReader(byte[] pixels, int width, int height, int stride);

/// <summary>
/// A small CPU camera feed for setup previews. Recording uses the GPU camera path instead,
/// so a preview must be disposed, and <see cref="Released"/> awaited, before recording opens the device.
/// </summary>
public sealed unsafe class CameraPreview : IDisposable
{
    private const int PreviewWidth = 640;
    private readonly object sync = new();
    private readonly string? deviceId;
    private readonly CameraCaptureResolution resolution;
    private Task? initialization;
    private IMFMediaSource? source;
    private IMFSourceReader? reader;
    private SourceReaderCallback? callback;
    private byte[] front = [], back = [];
    private int width, height, defaultStride;
    private bool hasFrame, stopped, mediaStarted;
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Raised on a Media Foundation thread after a new frame can be read with <see cref="TryReadLatestFrame"/>.</summary>
    public event Action? FrameArrived;
    /// <summary>Raised on a background thread when the camera cannot start or stops delivering frames.</summary>
    public event Action<Exception>? Failed;

    /// <summary>Completes after <see cref="Dispose"/> once the camera device has been released.</summary>
    public Task Released => released.Task;

    /// <param name="deviceId">Camera symbolic link; null or empty selects the first available camera.</param>
    /// <param name="resolution">The recording mode, so the preview shows the same aspect ratio.</param>
    public CameraPreview(string? deviceId, CameraCaptureResolution resolution)
    {
        this.deviceId = deviceId;
        this.resolution = resolution;
    }

    /// <summary>Opens the camera on a pool thread; activation can take a moment and must not block the UI.</summary>
    public void Start()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(stopped, this);
            initialization ??= Task.Run(Initialize);
        }
    }

    public bool TryReadLatestFrame(CameraPreviewFrameReader read)
    {
        lock (sync)
        {
            if (!hasFrame) return false;
            read(front, width, height, width * 4);
            return true;
        }
    }

    private void Initialize()
    {
        try
        {
            MediaFactory.MFStartup(true).CheckError();
            mediaStarted = true;
            using (var devices = MediaFactory.MFEnumVideoDeviceSources())
            {
                IMFActivate selected = devices.FirstOrDefault(device => string.IsNullOrEmpty(deviceId) ||
                    string.Equals(device.GetString(CaptureDeviceAttributeKeys.SourceTypeVidcapSymbolicLink), deviceId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new NotSupportedException("The selected camera is unavailable.");
                if (Volatile.Read(ref stopped)) return;
                source = selected.ActivateObject<IMFMediaSource>();
            }
            callback = new(OnReadSample);
            using (IMFAttributes attributes = MediaFactory.MFCreateAttributes(2))
            {
                attributes.Set(SourceReaderAttributeKeys.AsyncCallback, callback).CheckError();
                // Previews are small, so let Media Foundation decode and convert any camera format to RGB32.
                attributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, 1u).CheckError();
                reader = MediaFactory.MFCreateSourceReaderFromMediaSource(source, attributes);
            }
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
            ConfigureFormat(reader);
            lock (sync)
            {
                if (!stopped) SourceReaderOperations.RequestSample(reader);
            }
        }
        catch (Exception ex)
        {
            if (!Volatile.Read(ref stopped)) Failed?.Invoke(ex);
        }
    }

    private void ConfigureFormat(IMFSourceReader reader)
    {
        (double requestedWidth, double requestedHeight) = resolution == CameraCaptureResolution.Size640x480 ? (4d, 3d) : (16d, 9d);
        double aspect = requestedWidth / requestedHeight;
        double area = PreviewWidth * (PreviewWidth / aspect);
        IMFMediaType? best = null;
        double bestScore = double.MaxValue;
        try
        {
            for (int index = 0; index < 512; index++)
            {
                IMFMediaType type;
                try { type = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, index); }
                catch (SharpGenException ex) when (ex.ResultCode.Code == unchecked((int)0xc00d36b9)) { break; } // MF_E_NO_MORE_TYPES
                ulong size = type.GetUInt64(MediaTypeAttributeKeys.FrameSize);
                int modeWidth = (int)(size >> 32), modeHeight = (int)size;
                ulong rate = type.GetUInt64(MediaTypeAttributeKeys.FrameRate);
                double fps = (double)(rate >> 32) / Math.Max(1u, (uint)rate);
                if (modeWidth < 2 || modeHeight < 2)
                {
                    type.Dispose();
                    continue;
                }
                // Match the recording's aspect ratio first, then a small frame near 30 FPS.
                double score = Math.Abs((double)modeWidth / modeHeight - aspect) * 4
                    + Math.Abs(Math.Log((double)modeWidth * modeHeight / area))
                    + Math.Abs(fps - 30) / 30;
                if (score < bestScore)
                {
                    best?.Dispose();
                    best = type;
                    bestScore = score;
                }
                else type.Dispose();
            }
            if (best != null) reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, best);
            using IMFMediaType output = MediaFactory.MFCreateMediaType();
            output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            output.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32).CheckError();
            // Keep the selected native size; a partial type could switch the camera to its default mode.
            if (best != null) output.Set(MediaTypeAttributeKeys.FrameSize, best.GetUInt64(MediaTypeAttributeKeys.FrameSize)).CheckError();
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, output);
            ReadFrameSize(reader);
        }
        finally { best?.Dispose(); }
    }

    // The reader has one request outstanding at a time, so callbacks never overlap and own the back buffer.
    private void OnReadSample(int status, SourceReaderFlag flags, nint sample)
    {
        try
        {
            if (Volatile.Read(ref stopped)) return;
            new Result(status).CheckError();
            if ((flags & (SourceReaderFlag.Error | SourceReaderFlag.EndOfStream)) != 0)
                throw new IOException("The camera stopped delivering frames.");
            if ((flags & SourceReaderFlag.CurrentMediaTypeChanged) != 0) ReadFrameSize(reader!);
            bool copied = false;
            if (sample != 0)
            {
                // OnReadSample lends us a reference; take our own so disposing the wrapper balances it.
                Marshal.AddRef(sample);
                using IMFSample frame = new(sample);
                CopyFrame(frame);
                copied = true;
            }
            lock (sync)
            {
                if (stopped) return;
                SourceReaderOperations.RequestSample(reader!);
            }
            if (copied) FrameArrived?.Invoke();
        }
        catch (Exception ex)
        {
            if (!Volatile.Read(ref stopped)) Failed?.Invoke(ex);
        }
    }

    private void ReadFrameSize(IMFSourceReader reader)
    {
        using IMFMediaType current = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
        ulong size = current.GetUInt64(MediaTypeAttributeKeys.FrameSize);
        int newWidth = (int)(size >> 32), newHeight = (int)size;
        if (newWidth < 1 || newHeight < 1 || newWidth > 8192 || newHeight > 8192)
            throw new NotSupportedException("The camera reported an invalid preview size.");
        // Media Foundation RGB is top-down unless the type says otherwise.
        defaultStride = current.GetUInt32(MediaTypeAttributeKeys.DefaultStride, out uint stride).Success && stride != 0
            ? unchecked((int)stride) : newWidth * 4;
        back = new byte[newWidth * newHeight * 4];
        lock (sync)
        {
            width = newWidth; height = newHeight;
            front = new byte[back.Length];
            hasFrame = false;
        }
    }

    private void CopyFrame(IMFSample sample)
    {
        int rowBytes = width * 4;
        using IMFMediaBuffer buffer = sample.ConvertToContiguousBuffer();
        using IMF2DBuffer? twoDimensional = buffer.QueryInterfaceOrNull<IMF2DBuffer>();
        if (twoDimensional != null)
        {
            // Lock2D reports a negative pitch for bottom-up RGB32 and points at the top row.
            twoDimensional.Lock2D(out nint scanline, out int pitch);
            try { CopyRows(scanline, pitch, rowBytes); }
            finally { twoDimensional.Unlock2D(); }
        }
        else
        {
            buffer.Lock(out nint data, out _, out int length);
            try
            {
                if ((long)rowBytes * height > length) throw new IOException("The camera delivered a truncated preview frame.");
                int pitch = defaultStride;
                if ((long)Math.Abs(pitch) * height > length) throw new IOException("The camera delivered a truncated preview frame.");
                CopyRows(pitch < 0 ? data + (height - 1) * -pitch : data, pitch, rowBytes);
            }
            finally { buffer.Unlock(); }
        }
        lock (sync)
        {
            (front, back) = (back, front);
            hasFrame = true;
        }
    }

    private void CopyRows(nint top, int pitch, int rowBytes)
    {
        if (Math.Abs((long)pitch) < rowBytes) throw new IOException("The camera delivered an unsupported preview stride.");
        for (int row = 0; row < height; row++)
            new ReadOnlySpan<byte>((void*)(top + (nint)row * pitch), rowBytes).CopyTo(back.AsSpan(row * rowBytes, rowBytes));
    }

    /// <summary>Stops the preview without waiting. <see cref="Released"/> completes once the camera is free.</summary>
    public void Dispose()
    {
        Task? pending;
        lock (sync)
        {
            if (stopped) return;
            stopped = true;
            FrameArrived = null;
            Failed = null;
            callback?.Detach();
            pending = initialization;
        }
        // A camera that is still starting can take seconds to shut down, so never block the caller.
        // Activation cannot be interrupted; release the device once it has finished.
        (pending ?? Task.CompletedTask).ContinueWith(_ => TearDown(), TaskScheduler.Default);
    }

    private void TearDown()
    {
        try
        {
            callback?.Detach();
            // Never hold the callback lock across shutdown: native code may wait for queued callbacks.
            // Shutdown alone cancels the pending read. Flush would wait for the camera's first frame.
            try { source?.Shutdown(); } catch (SharpGenException) { }
            reader?.Dispose(); source?.Dispose(); callback?.Dispose();
            reader = null; source = null; callback = null;
            if (mediaStarted) MediaFactory.MFShutdown();
        }
        finally { released.TrySetResult(); }
    }
}
