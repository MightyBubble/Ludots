using System;
using System.Collections.Generic;
using System.IO;

namespace Ludots.Core.Modding
{
    public class VirtualFileSystem : IVirtualFileSystem
    {
        private readonly Dictionary<string, string> _mountPoints = new Dictionary<string, string>();

        public void Mount(string modId, string physicalPath)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(physicalPath))
                return;
                
            _mountPoints[modId] = physicalPath;
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

            if (!candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
    }
}
