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

            var fullPath = Path.Combine(rootPath, relativePath);
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

            fullPath = Path.Combine(rootPath, relativePath);
            return true;
        }
    }
}
