
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

        private static string BuildSequencedScreenshotPath(string targetPath, int sequenceIndex, int frame)
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