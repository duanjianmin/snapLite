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

using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using ShareX.ScreenRecordingLib;
using ShareX.Tools;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using MessageBox = ShareX.AvaloniaUI.MessageBox;
using ScreenRecorder = ShareX.ScreenRecordingLib.ScreenRecorder;
using MessageBoxButtons = ShareX.AvaloniaUI.MessageBoxButtons;
using MessageBoxIcon = ShareX.AvaloniaUI.MessageBoxIcon;

namespace ShareX;

/// <summary>
/// Native screen recording: region selection, the setup bar (sources, camera preview), the recording frame,
/// and the draggable camera while recording. Ported from ShareX's ScreenRecordManager without FFmpeg or uploads.
/// </summary>
public static class RecordingController
{
    private static ScreenRecorder? recorder;
    private static ScreenRecordWindow? recordForm;
    private static ScreenRecorderBarWindow? configurationBar;

    public static bool IsRecording { get; private set; }

    /// <summary>Raised on the UI thread with the saved file.</summary>
    public static event Action<string>? RecordingSaved;

    /// <summary>Starts a new recording, or, while one is set up or running, records/stops it (hotkey behavior).</summary>
    public static async void StartStop()
    {
        if (IsRecording)
        {
            if (configurationBar != null) configurationBar.RequestRecord();
            else if (recordForm != null && !recordForm.IsDisposed) recordForm.StartStopRecording();
            return;
        }
        try { await StartAsync(); }
        catch (Exception ex)
        {
            IsRecording = false;
            DebugHelper.WriteException(ex);
            MessageBox.Show(ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static void Stop()
    {
        configurationBar?.Cancel();
        if (recordForm?.Status == ScreenRecordingStatus.Aborted || recordForm?.RestartRequested == true) recorder?.RequestAbort();
        else recorder?.RequestStop();
    }

    private static async Task StartAsync()
    {
        AppSettings settings = SettingsStore.Settings;
        DebugHelper.WriteLine("Starting native screen recording. H.264/AAC MP4");

        var selection = await RegionCaptureTasks.GetRectangleRegionAsync(settings.RegionCaptureOptions);
        if (selection == null) return;
        Rectangle captureRectangle = Rectangle.Intersect(selection.Value.Rectangle, CaptureHelpers.GetScreenBounds());
        captureRectangle = CaptureHelpers.EvenRectangleSize(captureRectangle);
        if (IsRecording || !captureRectangle.IsValid() || recorder != null) return;
        IntPtr captureWindow = IntPtr.Zero;

        IsRecording = true;
        if (settings.Recorder.ScreenRecordShowBar)
        {
            try
            {
                configurationBar = new ScreenRecorderBarWindow(settings, captureRectangle, captureWindow);
                bool record = await configurationBar.ShowSetupAsync();
                configurationBar.Commit(settings);
                SettingsStore.Save();
                if (!record)
                {
                    IsRecording = false;
                    return;
                }
                captureRectangle = configurationBar.RecordingRegion;
                captureWindow = configurationBar.CaptureWindow;
            }
            catch
            {
                IsRecording = false;
                throw;
            }
            finally
            {
                configurationBar?.Cancel();
                configurationBar = null;
            }
        }

        settings.LastRecordingRegion = captureRectangle;
        RecorderSettings options = settings.Recorder;
        recordForm = new ScreenRecordWindow(captureRectangle)
        {
            ActivateWindow = true,
            Duration = options.ScreenRecordFixedDuration ? options.ScreenRecordDuration : 0,
            AskConfirmationOnAbort = options.ScreenRecordAskConfirmationOnAbort,
            ShowRecordingTimer = options.ScreenRecordShowTimer,
            ShowRecordingButtonLabels = options.ScreenRecordShowButtonLabels,
            UseInProcessPause = true,
            ExcludeFromCapture = true
        };
        recordForm.PauseRequested += () => recorder?.Pause();
        recordForm.ResumeRequested += () =>
        {
            if (captureWindow == IntPtr.Zero) recorder?.Resume(recordForm.RecordingRegion);
            else recorder?.Resume();
        };
        recordForm.StopRequested += Stop;
        recordForm.Show();

        string? savedPath = await Task.Run(async () =>
        {
            try
            {
                string path = Screenshots.ScreenshotController.GetOutputPath("mp4");
                return await RecordAsync(path, captureWindow, options) ? null : path;
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex);
                recordForm.InvokeSafe(() => MessageBox.Show(ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error));
                return null;
            }
            finally
            {
                if (recorder != null)
                {
                    await recorder.DisposeAsync();
                    recorder = null;
                }
            }
        });

        ScreenRecordWindow form = recordForm;
        recordForm = null;
        form.InvokeSafe(() =>
        {
            form.Close();
            form.Dispose();
        });
        IsRecording = false;
        if (savedPath != null && File.Exists(savedPath))
        {
            DebugHelper.WriteLine("Recording saved: " + savedPath);
            RecordingSaved?.Invoke(savedPath);
        }
    }

    /// <returns>True when the recording was aborted or did not complete.</returns>
    private static async Task<bool> RecordAsync(string path, IntPtr captureWindow, RecorderSettings settings)
    {
        ScreenRecordWindow form = recordForm!;
        // Record next to the target and move the finished file, so an abort never leaves a partial file.
        string recordingPath = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}-recording-{Guid.NewGuid():N}{Path.GetExtension(path)}");
        while (true)
        {
            bool completed = false;
            RecordingOptions options = new()
            {
                OutputPath = recordingPath,
                Region = form.RecordingRegion,
                WindowHandle = captureWindow,
                FramesPerSecond = settings.ScreenRecordFPS,
                VideoBitrate = settings.ScreenRecordVideoBitrate * 1000,
                CaptureSystemAudio = settings.ScreenRecordSystemAudio,
                SystemAudioDeviceId = settings.ScreenRecordSystemAudioDeviceId,
                SystemAudioGain = settings.ScreenRecordSystemAudioGain,
                CaptureMicrophone = settings.ScreenRecordMicrophone,
                MicrophoneDeviceId = settings.ScreenRecordMicrophoneDeviceId,
                MicrophoneGain = settings.ScreenRecordMicrophoneGain,
                CaptureCamera = settings.ScreenRecordCamera && IsCameraAvailable(settings.ScreenRecordCameraDeviceId),
                CameraDeviceId = settings.ScreenRecordCameraDeviceId,
                CameraResolution = settings.ScreenRecordCameraResolution,
                CameraFramesPerSecond = settings.ScreenRecordCameraFPS,
                CameraPosition = settings.ScreenRecordCameraPosition,
                CameraShape = settings.ScreenRecordCameraShape,
                CameraWidthPercent = settings.ScreenRecordCameraWidthPercent,
                CameraMargin = settings.ScreenRecordCameraMargin,
                RequireHardwareEncoder = settings.ScreenRecordRequireHardwareEncoder,
                IncludeCursor = settings.ScreenRecordShowCursor,
                Duration = settings.ScreenRecordFixedDuration ? TimeSpan.FromSeconds(settings.ScreenRecordDuration) : TimeSpan.Zero
            };
            recorder = CreateRecorder(options);
            try
            {
                form.ChangeState(ScreenRecordState.BeforeStart);
                // Initialize the GPU, codecs, camera and audio endpoints before the countdown.
                await recorder.PrepareAsync();
                int delay = settings.ScreenRecordStartDelayEnabled ? (int)(settings.ScreenRecordStartDelay * 1000) : 0;
                if (delay > 0)
                {
                    form.InvokeSafe(() => form.StartCountdown(delay));
                    form.RecordResetEvent.WaitOne(delay);
                }
                form.ConsumeRestartRequest();
                if (form.Status is ScreenRecordingStatus.Aborted or ScreenRecordingStatus.Stopped) return true;
                // A waiting recording region can be dragged. Rebuild for its final physical coordinates.
                if (captureWindow == IntPtr.Zero && options.Region != form.RecordingRegion)
                {
                    await recorder.DisposeAsync();
                    recorder = CreateRecorder(options with { Region = form.RecordingRegion });
                    await recorder.PrepareAsync();
                }
                DebugHelper.WriteLine("Native screen recording camera: {0}", recorder.Camera != null
                    ? $"{recorder.Camera}, shape: {options.CameraShape}, position: {options.CameraPosition}" : "off");
                form.ChangeState(ScreenRecordState.AfterStart);
                using (IDisposable? highlighter = settings.ScreenRecordMouseHighlighter
                    ? await MouseHighlighterManager.BeginRecordingAsync(SettingsStore.Settings.MouseHighlighter) : null)
                // A draggable live camera at the overlay's position; dragging it moves the recorded camera.
                using (IDisposable? cameraPreview = recorder.Camera != null
                    ? await CameraRecordingOverlay.ShowAsync(recorder, options.CameraShape, captureWindow != IntPtr.Zero
                        ? () => CaptureHelpers.GetWindowRectangle(captureWindow) : () => form.RecordingRegion) : null)
                {
                    recorder.EncodingProgressChanged += progress =>
                    {
                        if (progress == 0)
                        {
                            highlighter?.Dispose();
                            cameraPreview?.Dispose();
                        }
                    };
                    await recorder.StartAsync();
                    if (form.Status == ScreenRecordingStatus.Aborted || form.RestartRequested) recorder.RequestAbort();
                    else if (form.Status == ScreenRecordingStatus.Stopped) recorder.RequestStop();
                    else form.ChangeState(ScreenRecordState.AfterRecordingStart);
                    RecordingResult result = await recorder.Completion;
                    completed = true;
                    DebugHelper.WriteLine("Native screen recording completed. Encoder: {0}, hardware: {1}, frames: {2}, dropped: {3}",
                        result.Encoder.Name, result.Encoder.IsHardwareAccelerated, result.VideoFrames, result.DroppedVideoFrames);
                }
            }
            catch (OperationCanceledException) when (form.Status is ScreenRecordingStatus.Aborted or ScreenRecordingStatus.Stopped || form.RestartRequested)
            {
                // A stop during preparation or before the first frame removes the incomplete file.
            }
            finally
            {
                await recorder.DisposeAsync();
                recorder = null;
                form.ChangeState(ScreenRecordState.RecordingEnd);
            }
            if (form.RestartRequested)
            {
                FileHelpers.DeleteFile(recordingPath);
                continue;
            }
            bool aborted = form.Status == ScreenRecordingStatus.Aborted;
            if (aborted || !completed)
            {
                FileHelpers.DeleteFile(recordingPath);
                return true;
            }
            try { File.Move(recordingPath, path, false); }
            catch (Exception ex)
            {
                throw new IOException($"The recording finished, but it could not be moved to '{path}'. It is available at '{recordingPath}'.", ex);
            }
            return false;
        }
    }

    /// <summary>The camera is optional: without one (or without the selected one) the screen is recorded alone.</summary>
    private static bool IsCameraAvailable(string deviceId)
    {
        try
        {
            IReadOnlyList<CameraCaptureDevice> cameras = CameraCaptureDevices.GetCameras();
            return string.IsNullOrEmpty(deviceId) ? cameras.Count > 0 :cameras.Any(camera => camera.Id == deviceId);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex);
            return false;
        }
    }

    private static ScreenRecorder CreateRecorder(RecordingOptions options)
    {
        ScreenRecorder created = new(options);
        created.Diagnostic += message => DebugHelper.WriteLine("Native recorder: " + message);
        created.EncodingProgressChanged += progress =>
        {
            ScreenRecordWindow? form = recordForm;
            if (form == null) return;
            if (progress == 0)
            {
                form.ChangeState(ScreenRecordState.RecordingEnd);
                form.ChangeState(ScreenRecordState.Encoding);
            }
            form.ChangeStateProgress(progress);
        };
        return created;
    }
}
