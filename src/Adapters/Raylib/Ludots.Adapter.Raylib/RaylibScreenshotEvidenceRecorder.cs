using System;
using System.IO;
using SkiaSharp;

namespace Ludots.Adapter.Raylib
{
    /// <summary>
    /// 运行时截图取证器：LUDOTS_TAKE_SCREENSHOT_PATH/FRAMES/FRAME 与 LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT 合同的持有者，
    /// 含序列帧命名、落盘搬运与尺寸/平坦度校验。时序基准（frameIndex、runtime 毫秒）由宿主逐帧显式传入（#1325）。
    /// </summary>
    internal sealed class RaylibScreenshotEvidenceRecorder
    {
        private readonly string _targetPath;
        private readonly int[] _sequenceFrames;
        private readonly int _minRuntimeMs;
        private int _sequenceIndex;
        private int _currentFrame;
        private bool _pending;

        private RaylibScreenshotEvidenceRecorder(string targetPath, int[] sequenceFrames, int currentFrame, int minRuntimeMs)
        {
            _targetPath = targetPath;
            _sequenceFrames = sequenceFrames;
            _currentFrame = currentFrame;
            _minRuntimeMs = minRuntimeMs;
            _pending = true;
        }

        public bool Pending => _pending;

        public static RaylibScreenshotEvidenceRecorder? TryCreateFromEnvironment()
        {
            string? raw = Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_PATH");
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string targetPath = Path.GetFullPath(raw);
            if (string.IsNullOrWhiteSpace(Path.GetFileName(targetPath)))
            {
                throw new InvalidOperationException(
                    $"LUDOTS_TAKE_SCREENSHOT_PATH must point at a file, got directory-style path '{raw}'.");
            }

            int[] sequenceFrames = ReadEnvFrameList("LUDOTS_TAKE_SCREENSHOT_FRAMES");
            bool sequenceEnabled = sequenceFrames.Length > 0;
            int currentFrame = sequenceEnabled
                ? sequenceFrames[0]
                : int.TryParse(Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_FRAME"), out int parsed)
                    ? Math.Max(1, parsed)
                    : 60;
            int minRuntimeMs = RaylibAdapterEnv.ReadEnvIntOrDefault("LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT", 0);
            return new RaylibScreenshotEvidenceRecorder(targetPath, sequenceFrames, currentFrame, minRuntimeMs);
        }

        public bool ShouldCapture(int frameIndex, long runtimeElapsedMs)
        {
            return _pending && frameIndex >= _currentFrame && runtimeElapsedMs >= _minRuntimeMs;
        }

        /// <summary>落盘一张取证截图并推进序列状态；返回 TakeScreenshot..校验 的耗时毫秒数（double，亚毫秒保留）。writeDiagnostics 在截图前回调（宿主追加时序敏感诊断）。</summary>
        public double CaptureFrame(int frameIndex, int expectedWidth, int expectedHeight, Action writeDiagnostics)
        {
            string fullScreenshotPath = _sequenceFrames.Length > 0
                ? BuildSequencedScreenshotPath(_targetPath, _sequenceIndex, _currentFrame)
                : _targetPath;
            string screenshotFile = Path.GetFileName(fullScreenshotPath);
            string screenshotWorkingFilePath = Path.Combine(Environment.CurrentDirectory, screenshotFile);
            string? screenshotDirectory = Path.GetDirectoryName(fullScreenshotPath);
            if (!string.IsNullOrWhiteSpace(screenshotDirectory))
            {
                Directory.CreateDirectory(screenshotDirectory);
            }

            writeDiagnostics();

            long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            Raylib_cs.Raylib.TakeScreenshot(screenshotFile);
            if (!string.Equals(screenshotWorkingFilePath, fullScreenshotPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(screenshotWorkingFilePath))
            {
                File.Copy(screenshotWorkingFilePath, fullScreenshotPath, overwrite: true);
                File.Delete(screenshotWorkingFilePath);
            }

            ValidateRuntimeScreenshotEvidence(fullScreenshotPath, expectedWidth, expectedHeight);
            double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

            if (_sequenceFrames.Length > 0)
            {
                _sequenceIndex++;
                _pending = _sequenceIndex < _sequenceFrames.Length;
                if (_pending)
                {
                    _currentFrame = _sequenceFrames[_sequenceIndex];
                }
            }
            else
            {
                _pending = false;
            }

            Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Engine, $"Captured runtime screenshot: {fullScreenshotPath}");
            return elapsedMs;
        }


        private static int[] ReadEnvFrameList(string key)
        {
            string? raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<int>();
            }

            string[] parts = raw.Split(
                new[] { ',', ';', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var frames = new List<int>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int frame))
                {
                    frames.Add(Math.Max(1, frame));
                }
            }

            return frames.ToArray();
        }

        internal static string BuildSequencedScreenshotPath(string targetPath, int sequenceIndex, int frame)
        {
            string directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
            string extension = Path.GetExtension(targetPath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".png";
            }

            string fileName = Path.GetFileNameWithoutExtension(targetPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "screenshot";
            }

            string sequencedFileName = $"{fileName}_{sequenceIndex + 1:000}_f{frame:0000}{extension}";
            return string.IsNullOrWhiteSpace(directory)
                ? Path.GetFullPath(sequencedFileName)
                : Path.Combine(directory, sequencedFileName);
        }

        internal static void ValidateRuntimeScreenshotEvidence(string screenshotPath, int expectedWidth, int expectedHeight)
        {
            if (string.IsNullOrWhiteSpace(screenshotPath))
            {
                throw new ArgumentException("Raylib screenshot evidence path cannot be null or whitespace.", nameof(screenshotPath));
            }

            if (expectedWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedWidth));
            }

            if (expectedHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedHeight));
            }

            string fullPath = Path.GetFullPath(screenshotPath);
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException($"Raylib screenshot evidence was not written: {fullPath}");
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length < 24)
            {
                throw new InvalidOperationException($"Raylib screenshot evidence is too small to be a valid PNG: {fullPath} length={fileInfo.Length}.");
            }

            if (!string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Raylib screenshot evidence must be a PNG so dimensions can be verified: {fullPath}");
            }

            using var bitmap = SKBitmap.Decode(fullPath);
            if (bitmap == null)
            {
                throw new InvalidOperationException($"Raylib screenshot evidence is not a decodable PNG image: {fullPath}");
            }

            int actualWidth = bitmap.Width;
            int actualHeight = bitmap.Height;
            if (actualWidth != expectedWidth || actualHeight != expectedHeight)
            {
                throw new InvalidOperationException(
                    $"Raylib screenshot evidence dimensions mismatch: {fullPath} actual={actualWidth}x{actualHeight} expected={expectedWidth}x{expectedHeight}.");
            }

            if (IsVisuallyFlat(bitmap))
            {
                throw new InvalidOperationException($"Raylib screenshot evidence is visually flat and cannot prove a rendered scene: {fullPath}");
            }
        }

        private static bool IsVisuallyFlat(SKBitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            if (width <= 0 || height <= 0)
            {
                return true;
            }

            SKColor first = bitmap.GetPixel(0, 0);
            int stepX = Math.Max(1, width / 16);
            int stepY = Math.Max(1, height / 16);
            for (int y = 0; y < height; y += stepY)
            {
                for (int x = 0; x < width; x += stepX)
                {
                    if (ColorDistance(bitmap.GetPixel(x, y), first) > 6)
                    {
                        return false;
                    }
                }
            }

            return ColorDistance(bitmap.GetPixel(width - 1, height - 1), first) <= 6;
        }

        private static int ColorDistance(SKColor a, SKColor b)
        {
            return Math.Abs(a.Red - b.Red) +
                Math.Abs(a.Green - b.Green) +
                Math.Abs(a.Blue - b.Blue) +
                Math.Abs(a.Alpha - b.Alpha);
        }    }
}
