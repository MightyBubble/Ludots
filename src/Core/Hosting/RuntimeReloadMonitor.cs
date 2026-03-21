using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ludots.Core.Hosting
{
    public sealed class RuntimeReloadMonitor
    {
        private readonly TimeSpan _debounceWindow;
        private FileSignature[] _committed;
        private FileSignature[]? _pending;
        private DateTime _pendingSinceUtc;

        public RuntimeReloadMonitor(IEnumerable<string> paths, TimeSpan? debounceWindow = null)
        {
            _debounceWindow = debounceWindow ?? TimeSpan.FromMilliseconds(600);
            Paths = NormalizePaths(paths);
            _committed = Capture(Paths);
        }

        public IReadOnlyList<string> Paths { get; }

        public bool Poll()
        {
            var current = Capture(Paths);
            if (SignaturesEqual(_committed, current))
            {
                _pending = null;
                return false;
            }

            if (_pending == null || !SignaturesEqual(_pending, current))
            {
                _pending = current;
                _pendingSinceUtc = DateTime.UtcNow;
                return false;
            }

            if (DateTime.UtcNow - _pendingSinceUtc < _debounceWindow)
            {
                return false;
            }

            _committed = current;
            _pending = null;
            return true;
        }

        private static IReadOnlyList<string> NormalizePaths(IEnumerable<string> paths)
        {
            var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                normalized.Add(Path.GetFullPath(path));
            }

            return normalized.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static FileSignature[] Capture(IReadOnlyList<string> paths)
        {
            var signatures = new FileSignature[paths.Count];
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                bool exists = File.Exists(path);
                long length = exists ? new FileInfo(path).Length : -1;
                long lastWriteUtcTicks = exists ? File.GetLastWriteTimeUtc(path).Ticks : 0;
                signatures[i] = new FileSignature(path, exists, length, lastWriteUtcTicks);
            }

            return signatures;
        }

        private static bool SignaturesEqual(IReadOnlyList<FileSignature> left, IReadOnlyList<FileSignature> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private readonly record struct FileSignature(string Path, bool Exists, long Length, long LastWriteUtcTicks);
    }
}
