using System;
using System.Collections.Generic;
using System.IO;

namespace Ludots.Core.Modding
{
    public class VirtualFileSystem : IVirtualFileSystem
    {
        private readonly Dictionary<string, string> _mountPoints = new Dictionary<string, string>(StringComparer.Ordinal);

        public void Mount(string modId, string physicalPath)
        {
            if (string.IsNullOrWhiteSpace(modId))
                throw new ArgumentException("Mount mod id is required.", nameof(modId));
            if (string.IsNullOrWhiteSpace(physicalPath))
                throw new ArgumentException("Mount physical path is required.", nameof(physicalPath));

            var fullPath = Path.GetFullPath(physicalPath);
            if (!Directory.Exists(fullPath))
                throw new DirectoryNotFoundException($"Mount physical path was not found: {fullPath}");

            _mountPoints[modId] = fullPath;
        }

        public bool Unmount(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId)) return false;
            return _mountPoints.Remove(modId);
        }

        public Stream GetStream(string uri)
        {
            // Format: ModId:Path/To/File
            var parts = uri.Split(new[] { ':' }, 2);
            if (parts.Length != 2)
            {
                throw new ArgumentException($"Invalid URI format: {uri}. Expected ModId:Path");
            }

            var modId = parts[0];
            var relativePath = parts[1];

            if (!_mountPoints.TryGetValue(modId, out var rootPath))
            {
                throw new FileNotFoundException($"Mod '{modId}' is not mounted.");
            }

            if (!TryResolveUnderRoot(rootPath, relativePath, out var fullPath))
            {
                throw new UnauthorizedAccessException($"Path escapes mount root: {uri}");
            }

            if (!File.Exists(fullPath))
            {
                 // Console.WriteLine($"[VFS] File not found: {fullPath} (URI: {uri})");
                 throw new FileNotFoundException($"File not found: {fullPath}");
            }

            return File.OpenRead(fullPath);
        }

        public bool TryResolveFullPath(string uri, out string fullPath)
        {
            fullPath = string.Empty;
            var parts = uri.Split(new[] { ':' }, 2);
            if (parts.Length != 2) return false;

            var modId = parts[0];
            var relativePath = parts[1];
            if (!_mountPoints.TryGetValue(modId, out var rootPath)) return false;

            return TryResolveUnderRoot(rootPath, relativePath, out fullPath);
        }

        public IReadOnlyList<string> EnumerateFiles(string uri, string searchPattern = "*.json")
        {
            if (string.IsNullOrWhiteSpace(searchPattern))
            {
                throw new ArgumentException("Search pattern must not be empty.", nameof(searchPattern));
            }

            var parts = uri.Split(new[] { ':' }, 2);
            if (parts.Length != 2)
            {
                throw new ArgumentException($"Invalid URI format: {uri}. Expected ModId:Path");
            }

            var modId = parts[0];
            var relativePath = parts[1];

            if (!_mountPoints.TryGetValue(modId, out var rootPath))
            {
                throw new FileNotFoundException($"Mod '{modId}' is not mounted.");
            }

            if (!TryResolveUnderRoot(rootPath, relativePath, out var fullPath))
            {
                throw new UnauthorizedAccessException($"Path escapes mount root: {uri}");
            }

            if (!Directory.Exists(fullPath))
            {
                return Array.Empty<string>();
            }

            var files = new List<string>();
            foreach (string file in Directory.EnumerateFiles(fullPath, searchPattern, SearchOption.TopDirectoryOnly))
            {
                var full = Path.GetFullPath(file);
                if (!TryMakeRelativeUri(modId, rootPath, full, out string fileUri))
                {
                    throw new UnauthorizedAccessException($"Enumerated file escapes mount root: {full}");
                }

                files.Add(fileUri);
            }

            files.Sort(StringComparer.Ordinal);
            return files;
        }

        private static bool TryResolveUnderRoot(string rootPath, string relativePath, out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(relativePath)) return false;

            var rootFull = Path.GetFullPath(rootPath);
            var rel = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            rel = rel.TrimStart(Path.DirectorySeparatorChar);

            var candidate = Path.GetFullPath(Path.Combine(rootFull, rel));

            // Prefix check with separator to avoid sibling directory false-positives.
            var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(rootWithSep, StringComparison.Ordinal))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }

        private static bool TryMakeRelativeUri(string modId, string rootPath, string fullPath, out string uri)
        {
            uri = string.Empty;
            var rootFull = Path.GetFullPath(rootPath);
            var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;
            var fileFull = Path.GetFullPath(fullPath);

            if (!fileFull.StartsWith(rootWithSep, StringComparison.Ordinal))
            {
                return false;
            }

            string relative = fileFull.Substring(rootWithSep.Length)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            uri = $"{modId}:{relative}";
            return true;
        }
    }
}
