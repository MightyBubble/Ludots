using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Platform.Abstractions;

namespace Ludots.Platform.Desktop
{
    /// <summary>
    /// File-system <see cref="ISaveStorage"/> for desktop hosts. Keys are repo-style relative paths
    /// ('saves/manual/slot.ldsave') rooted at the constructor directory; commits rename a fully
    /// written temp file over the final key so a crash never leaves a half-written slot behind.
    /// </summary>
    public sealed class DesktopSaveStorage : ISaveStorage
    {
        private readonly string _rootDirectory;

        public DesktopSaveStorage(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("Root directory is empty.", nameof(rootDirectory));
            _rootDirectory = Path.GetFullPath(rootDirectory);
        }

        public string RootDirectory => _rootDirectory;

        public string DisplayRoot => _rootDirectory;

        public IReadOnlyList<string> ListFileKeys(string prefix)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));

            string prefixPath = Resolve(prefix);
            if (!Directory.Exists(prefixPath))
            {
                return Array.Empty<string>();
            }

            var keys = new List<string>();
            foreach (string file in Directory.EnumerateFiles(prefixPath, "*", SearchOption.AllDirectories))
            {
                keys.Add(ToFileKey(file));
            }

            return keys;
        }

        public bool Exists(string key)
        {
            return File.Exists(Resolve(key));
        }

        public byte[] ReadAllBytes(string key)
        {
            return File.ReadAllBytes(Resolve(key));
        }

        public void WriteAllBytes(string key, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            string path = Resolve(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        public void CommitTempFile(string tempKey, string finalKey)
        {
            string tempPath = Resolve(tempKey);
            string finalPath = Resolve(finalKey);
            if (!File.Exists(tempPath))
            {
                throw new FileNotFoundException($"Temp file '{tempKey}' does not exist.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            File.Move(tempPath, finalPath, overwrite: true);
        }

        public void Delete(string key)
        {
            File.Delete(Resolve(key));
        }

        private string Resolve(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Storage key is empty.", nameof(key));
            }

            string path = Path.GetFullPath(Path.Combine(_rootDirectory, key.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(_rootDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Storage key '{key}' escapes the save root.");
            }

            return path;
        }

        private string ToFileKey(string absolutePath)
        {
            return Path.GetRelativePath(_rootDirectory, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
