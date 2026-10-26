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
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Bitmap = SkiaSharp.SKBitmap;

namespace ShareX.ScreenCaptureLib
{
    internal class ScrollingCaptureManager : IDisposable
    {
        public ScrollingCaptureOptions Options { get; private set; }
        public Bitmap Result { get; private set; }
        public bool IsCapturing { get; private set; }

        private Bitmap lastScreenshot;
        private Bitmap previousScreenshot;
        private bool stopRequested;
        private ScrollingCaptureStatus status;
        private int bestMatchCount, bestMatchIndex, bestIgnoreBottomOffset;
        // Rows the last stitch added, i.e. how far the last scroll actually moved the content.
        private int lastAddedHeight;
        private WindowInfo selectedWindow;
        private Rectangle selectedRectangle;

        public ScrollingCaptureManager(ScrollingCaptureOptions options)
        {
            Options = options;
        }

        public void Dispose()
        {
            Reset();
        }

        private void Reset(bool keepResult = false)
        {
            if (lastScreenshot != null)
            {
                lastScreenshot.Dispose();
                lastScreenshot = null;
            }

            if (previousScreenshot != null)
            {
                previousScreenshot.Dispose();
                previousScreenshot = null;
            }

            if (!keepResult && Result != null)
            {
                Result.Dispose();
                Result = null;
            }
        }

        public async Task<ScrollingCaptureStatus> StartCapture()
        {
            if (!IsCapturing && selectedWindow != null && !selectedRectangle.IsEmpty)
            {
                IsCapturing = true;
                stopRequested = false;
                status = ScrollingCaptureStatus.Failed;
                bestMatchCount = 0;
                bestMatchIndex = 0;
                bestIgnoreBottomOffset = 0;
                lastAddedHeight = 0;
                Reset();

                ScrollingCaptureRegionWindow regionWindow = null;
                Point? restoreCursor = null;

                if (Options.ShowRegion)
                {
                    regionWindow = new ScrollingCaptureRegionWindow(selectedRectangle);
                    regionWindow.Show();
                }

                try
                {
                    selectedWindow.Activate();

                    if (Options.ScrollMethod == ScrollMethod.MouseWheel)
                    {
                        // The wheel scrolls whatever is under the cursor. A drag selection leaves the cursor on the
                        // region's corner, often over a part that does not scroll (a chat page's input box), so
                        // nothing would scroll. Point at the middle of the region, and put the cursor back afterwards.
                        restoreCursor = CaptureHelpers.GetCursorPosition();
                        CaptureHelpers.SetCursorPosition(selectedRectangle.X + selectedRectangle.Width / 2, selectedRectangle.Y + selectedRectangle.Height / 2);
                    }

                    await Task.Delay(Options.StartDelay);

                    if (Options.AutoScrollTop)
                    {
                        InputHelpers.SendKeyPress(VirtualKeyCode.HOME);
                        NativeMethods.SendMessage(selectedWindow.Handle, (int)WindowsMessages.VSCROLL, (int)ScrollBarCommands.SB_TOP, 0);

                        await Task.Delay(Options.ScrollDelay);
                    }

                    Screenshot screenshot = new Screenshot()
                    {
                        CaptureCursor = false
                    };

                    // Wheel steps adapt to the content: the first is one notch, to learn how far a notch moves it.
                    // The next scroll is sent before the current capture is stitched, so the capture being
                    // stitched was moved by the previous step (notchesBehindCapture).
                    int wheelNotches = 1, notchesBehindCapture = 0;
                    double pixelsPerNotch = 0;

                    while (!stopRequested)
                    {
                        lastScreenshot = await CaptureSettledAsync(screenshot);

                        if (CompareLastTwoImages())
                        {
                            break;
                        }

                        int notchesSent = 0;

                        switch (Options.ScrollMethod)
                        {
                            case ScrollMethod.MouseWheel:
                                InputHelpers.SendMouseWheel(-120 * wheelNotches);
                                notchesSent = wheelNotches;
                                break;
                            case ScrollMethod.DownArrow:
                                for (int i = 0; i < Options.ScrollAmount; i++)
                                {
                                    InputHelpers.SendKeyPress(VirtualKeyCode.DOWN);
                                }
                                break;
                            case ScrollMethod.PageDown:
                                InputHelpers.SendKeyPress(VirtualKeyCode.NEXT);
                                break;
                            case ScrollMethod.ScrollMessage:
                                for (int i = 0; i < Options.ScrollAmount; i++)
                                {
                                    NativeMethods.SendMessage(selectedWindow.Handle, (int)WindowsMessages.VSCROLL, (int)ScrollBarCommands.SB_LINEDOWN, 0);
                                }
                                break;
                        }

                        Stopwatch timer = Stopwatch.StartNew();

                        if (lastScreenshot != null)
                        {
                            int expectedHeight = (int)(pixelsPerNotch * notchesBehindCapture);
                            Bitmap newResult = await CombineImagesAsync(Result, lastScreenshot, previousScreenshot, expectedHeight);

                            if (newResult != null)
                            {
                                Result?.Dispose();
                                Result = newResult;

                                // Learn from the first step, then only from steps that moved about as expected:
                                // the last step before the end moves less, and must not shrink the following steps.
                                if (notchesBehindCapture > 0 && lastAddedHeight > 0 &&
                                    (pixelsPerNotch == 0 || Math.Abs(lastAddedHeight - expectedHeight) <= expectedHeight / 3 + 20))
                                {
                                    // Keep each step at about half the region, so consecutive captures always
                                    // overlap enough to stitch exactly. The configured amount is the upper limit.
                                    pixelsPerNotch = (double)lastAddedHeight / notchesBehindCapture;
                                    wheelNotches = Math.Clamp((int)(selectedRectangle.Height * 0.5 / pixelsPerNotch), 1, Math.Max(1, Options.ScrollAmount));
                                }
                            }
                            else
                            {
                                break;
                            }
                        }

                        notchesBehindCapture = notchesSent;

                        if (stopRequested)
                        {
                            break;
                        }

                        if (lastScreenshot != null)
                        {
                            if (previousScreenshot != null)
                            {
                                previousScreenshot.Dispose();
                            }

                            previousScreenshot = lastScreenshot;
                            lastScreenshot = null;
                        }

                        int delay = Options.ScrollDelay - (int)timer.ElapsedMilliseconds;

                        if (delay > 0)
                        {
                            await Task.Delay(delay);
                        }
                    }
                }
                finally
                {
                    regionWindow?.Close();

                    if (restoreCursor is Point cursor)
                    {
                        CaptureHelpers.SetCursorPosition(cursor);
                    }

                    Reset(true);
                    IsCapturing = false;
                }
            }

            return status;
        }

        /// <summary>
        /// Captures once the content has stopped moving: smooth scrolling (browsers) is still animating after the
        /// scroll delay, and a frame from the middle of the animation does not line up with the next one.
        /// Content that never settles (an animation inside the region) is taken after about a second.
        /// </summary>
        private async Task<Bitmap> CaptureSettledAsync(Screenshot screenshot)
        {
            Bitmap current = screenshot.CaptureRectangle(selectedRectangle);

            for (int i = 0; i < 12 && !stopRequested; i++)
            {
                await Task.Delay(80);
                Bitmap next = screenshot.CaptureRectangle(selectedRectangle);
                bool settled = SkiaImageHelpers.CompareImages(current, next);
                current.Dispose();
                current = next;

                if (settled)
                {
                    break;
                }
            }

            return current;
        }

        public void StopCapture()
        {
            if (IsCapturing)
            {
                stopRequested = true;
            }
        }

        public async Task<bool> SelectWindowAsync()
        {
            var selection = await RegionCaptureTasks.GetRectangleRegionAsync(new RegionCaptureOptions());
            if (selection == null)
            {
                return false;
            }

            selectedRectangle = selection.Value.Rectangle;
            selectedWindow = selection.Value.WindowInfo;
            return selectedWindow != null;
        }

        private bool IsScrollReachedBottom(IntPtr handle)
        {
            SCROLLINFO scrollInfo = new SCROLLINFO();
            scrollInfo.cbSize = (uint)Marshal.SizeOf(scrollInfo);
            scrollInfo.fMask = (uint)(ScrollInfoMask.SIF_RANGE | ScrollInfoMask.SIF_PAGE | ScrollInfoMask.SIF_TRACKPOS);

            if (NativeMethods.GetScrollInfo(handle, (int)SBOrientation.SB_VERT, ref scrollInfo))
            {
                return scrollInfo.nMax == scrollInfo.nTrackPos + scrollInfo.nPage - 1;
            }

            return CompareLastTwoImages();
        }

        private bool CompareLastTwoImages()
        {
            if (lastScreenshot != null && previousScreenshot != null)
            {
                return AreNearlyIdentical(lastScreenshot, previousScreenshot);
            }

            return false;
        }

        /// <summary>
        /// The end of the content: two captures are the same apart from a sliver of pixels. Exact equality is never
        /// reached under a live widget (a network-speed ball, a clock) that changes between captures.
        /// </summary>
        private static unsafe bool AreNearlyIdentical(Bitmap first, Bitmap second)
        {
            if (first.Width != second.Width || first.Height != second.Height)
            {
                return false;
            }

            using SkiaPixelBuffer a = new(first, true, PixelAccess.ReadOnly);
            using SkiaPixelBuffer b = new(second, true, PixelAccess.ReadOnly);
            uint* pa = (uint*)a.Pointer;
            uint* pb = (uint*)b.Pointer;
            long total = (long)first.Width * first.Height;
            long allowed = Math.Max(16, total / 1000);
            long different = 0;
            for (long i = 0; i < total; i++)
            {
                if (pa[i] != pb[i] && ++different > allowed)
                {
                    return false;
                }
            }

            return true;
        }

        private async Task<Bitmap> CombineImagesAsync(Bitmap result, Bitmap currentImage, Bitmap previousImage, int expectedHeight)
        {
            return await Task.Run(() => CombineImages(result, currentImage, previousImage, expectedHeight) ??
                (expectedHeight > 0 ? CombineImages(result, currentImage, previousImage, 0) : null));
        }

        /// <summary>
        /// Compares a stitched row with a row of the new capture, skipping pixels that did not move between the two
        /// captures (floating widgets, desktop overlays, sticky headers, plain background): they are not part of the
        /// scrolled content and would break the true alignment. A few differing pixels are tolerated for widgets
        /// whose text changes between captures.
        /// </summary>
        /// <returns>1 when the rows match, 0 when too little moved to tell, -1 when they differ.</returns>
        private static unsafe int CompareRowsIgnoringStatic(uint* resultRow, uint* currentRow, bool* staticAtCurrentRow, bool* staticAtResultRow, int width)
        {
            int compared = 0;
            int different = 0;
            for (int x = 0; x < width; x++)
            {
                if (staticAtCurrentRow[x] || (staticAtResultRow != null && staticAtResultRow[x]))
                {
                    continue;
                }

                compared++;
                if (resultRow[x] != currentRow[x])
                {
                    different++;
                }
            }

            if (compared < 8)
            {
                return 0;
            }

            return different <= Math.Max(2, compared / 50) ? 1 : -1;
        }

        private static unsafe int CompareRows(IntPtr first, IntPtr second, int count)
            => new ReadOnlySpan<byte>(first.ToPointer(), count).SequenceEqual(new ReadOnlySpan<byte>(second.ToPointer(), count)) ? 0 : 1;

        /// <param name="expectedHeight">
        /// How many new rows the scroll should have revealed, or 0 when unknown. Repetitive content (identical text
        /// lines, blank space, table rows) matches at several offsets; only offsets near the expected one count.
        /// </param>
        private unsafe Bitmap CombineImages(Bitmap result, Bitmap currentImage, Bitmap previousImage, int expectedHeight)
        {
            if (result == null)
            {
                status = ScrollingCaptureStatus.Successful;

                return currentImage.Copy();
            }

            int matchCount = 0;
            int matchIndex = 0;

            int ignoreSideOffset = Math.Max(50, currentImage.Width / 20);
            ignoreSideOffset = Math.Min(ignoreSideOffset, currentImage.Width / 3);

            if (previousImage == null || previousImage.Width != currentImage.Width || previousImage.Height != currentImage.Height)
            {
                status = ScrollingCaptureStatus.Failed;
                return null;
            }

            // Align the new capture with the previous capture, not with the stitched result: the result's older rows
            // were stitched from earlier captures, where a floating widget sat over different content. The bottom of
            // the result is the previous capture, so its row R is result row R + (result height - capture height).
            int width = currentImage.Width;
            int height = currentImage.Height;
            int maskWidth = width - ignoreSideOffset * 2;
            using SkiaPixelBuffer bdPrevious = new(previousImage, true, PixelAccess.ReadOnly);
            using SkiaPixelBuffer bdCurrentImage = new(currentImage, true, PixelAccess.ReadOnly);
            uint* previous = (uint*)bdPrevious.Pointer + ignoreSideOffset;
            uint* current = (uint*)bdCurrentImage.Pointer + ignoreSideOffset;

            // Pixels identical at the same screen position in both captures did not scroll (widgets, overlays,
            // sticky headers, plain background).
            bool[] staticMask = new bool[height * maskWidth];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < maskWidth; x++)
                {
                    staticMask[y * maskWidth + x] = previous[y * width + x] == current[y * width + x];
                }
            }

            int ignoreBottomOffsetMax = height / 3;
            int ignoreBottomOffset = Math.Max(50, height / 10);

            if (Options.AutoIgnoreBottomEdge)
            {
                for (int i = 0; i <= ignoreBottomOffsetMax; i++)
                {
                    int row = height - 1 - i;
                    if (CompareRows((IntPtr)(previous + row * width), (IntPtr)(current + row * width), maskWidth * 4) != 0)
                    {
                        ignoreBottomOffset += i;
                        break;
                    }
                }

                ignoreBottomOffset = Math.Max(ignoreBottomOffset, bestIgnoreBottomOffset);
            }

            ignoreBottomOffset = Math.Min(ignoreBottomOffset, ignoreBottomOffsetMax);

            // The previous capture's row that the new capture's row currentImageY must continue.
            int previousBottom = height - ignoreBottomOffset - 1;

            int tolerance = expectedHeight / 3 + 20;

            // Every candidate alignment must agree over its whole overlap. A run of matching rows is not enough:
            // repetitive content (lists, blank space, repeated headings) matches at wrong offsets for a while, and
            // the last scroll before the end moves less than expected, so the nearest plausible offset can be wrong.
            int bestScore = int.MinValue;
            int bestDistance = int.MaxValue;
            fixed (bool* mask = staticMask)
            {
                for (int currentImageY = height - 1; currentImageY >= 0; currentImageY--)
                {
                    int added = height - currentImageY - 1 - ignoreBottomOffset;
                    if (expectedHeight > 0 && Math.Abs(added - expectedHeight) > tolerance)
                    {
                        continue;
                    }

                    int matched = 0;
                    int mismatched = 0;
                    for (int y = 0; currentImageY - y >= 0; y++)
                    {
                        int previousRow = previousBottom - y;
                        if (previousRow < 0)
                        {
                            break;
                        }

                        int currentRow = currentImageY - y;
                        int compare = CompareRowsIgnoringStatic(previous + previousRow * width, current + currentRow * width,
                            mask + currentRow * maskWidth, mask + previousRow * maskWidth, maskWidth);
                        if (compare > 0)
                        {
                            matched++;
                        }
                        else if (compare < 0 && ++mismatched > 3 + matched / 50)
                        {
                            break;
                        }
                    }

                    if (matched < 12 || mismatched > 3 + matched / 50)
                    {
                        continue;
                    }

                    int score = matched - 20 * mismatched;
                    int distance = expectedHeight > 0 ? Math.Abs(added - expectedHeight) : 0;
                    if (score > bestScore || (score == bestScore && distance < bestDistance))
                    {
                        bestScore = score;
                        bestDistance = distance;
                        matchCount = matched;
                        matchIndex = currentImageY;
                    }
                }
            }

            bool bestGuess = false;

            if (matchCount == 0 && expectedHeight > 0)
            {
                // Nothing near the expected offset; the caller retries with a full search.
                return null;
            }

            if (matchCount == 0 && bestMatchCount > 0)
            {
                matchCount = bestMatchCount;
                matchIndex = bestMatchIndex;
                ignoreBottomOffset = bestIgnoreBottomOffset;
                bestGuess = true;
            }

            if (matchCount > 0)
            {
                int matchHeight = currentImage.Height - matchIndex - 1;

                if (matchHeight > 0)
                {
                    lastAddedHeight = matchHeight - ignoreBottomOffset;

                    if (matchCount > bestMatchCount)
                    {
                        bestMatchCount = matchCount;
                        bestMatchIndex = matchIndex;
                        bestIgnoreBottomOffset = ignoreBottomOffset;
                    }

                    Bitmap newResult = SkiaImageHelpers.CreateBitmap(result.Width, result.Height - ignoreBottomOffset + matchHeight);

                    using (SKCanvas g = new(newResult))
                    using (SKPaint paint = new() { BlendMode = SKBlendMode.Src })
                    {
                        g.DrawBitmap(result, SKRect.Create(0, 0, result.Width, result.Height - ignoreBottomOffset),
                            SKRect.Create(0, 0, result.Width, result.Height - ignoreBottomOffset), paint);
                        g.DrawBitmap(currentImage, SKRect.Create(0, matchIndex + 1, currentImage.Width, matchHeight),
                            SKRect.Create(0, result.Height - ignoreBottomOffset, currentImage.Width, matchHeight), paint);
                    }

                    if (bestGuess)
                    {
                        status = ScrollingCaptureStatus.PartiallySuccessful;
                    }
                    else if (status != ScrollingCaptureStatus.PartiallySuccessful)
                    {
                        status = ScrollingCaptureStatus.Successful;
                    }

                    return newResult;
                }
            }

            status = ScrollingCaptureStatus.Failed;

            return null;
        }
    }
}
