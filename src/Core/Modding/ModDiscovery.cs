using System;
using System.Collections.Generic;
using System.IO;

namespace Ludots.Core.Modding
{
    public readonly record struct DiscoveredMod(string DirectoryPath, string ManifestPath, ModManifest Manifest);

    public static class ModDiscovery
    {
        public static List<string> DiscoverModDirectories(string rootPath)
        {
            return DiscoverModDirectories(new[] { rootPath });
        }

        public static List<string> DiscoverModDirectories(IEnumerable<string> roots)
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (roots == null)
            {
                throw new ArgumentNullException(nameof(roots));
            }

            foreach (var root in roots)
            {
                foreach (var directory in DiscoverModDirectoriesFromRoot(root))
                {
                    if (seen.Add(directory))
                    {
                        results.Add(directory);
                    }
                }
            }

            return results;
        }

        public static List<DiscoveredMod> DiscoverMods(IEnumerable<string> roots, Action<string, Exception>? onError = null)
        {
            var directories = DiscoverModDirectories(roots);
            var results = new List<DiscoveredMod>(directories.Count);

            for (int i = 0; i < directories.Count; i++)
            {
                var directory = directories[i];
                if (!TryGetExactChildFile(directory, "mod.json", out var manifestPath))
                {
                    throw new FileNotFoundException($"Exact manifest file 'mod.json' was not found in discovered mod directory: {directory}");
                }

                try
                {
                    var manifest = ModManifestJson.ParseStrict(File.ReadAllText(manifestPath), manifestPath);
                    results.Add(new DiscoveredMod(directory, manifestPath, manifest));
                }
                catch (Exception ex)
                {
                    onError?.Invoke(directory, ex);
                    throw;
                }
            }

            return results;
        }

        private static IEnumerable<string> DiscoverModDirectoriesFromRoot(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("Mod discovery root path is required.", nameof(rootPath));
            }

            if (!Directory.Exists(rootPath))
            {
                throw new DirectoryNotFoundException($"Mod discovery root path was not found: {Path.GetFullPath(rootPath)}");
            }

            var pending = new Stack<string>();
            pending.Push(Path.GetFullPath(rootPath));

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (ShouldIgnoreDirectory(current))
                {
                    continue;
                }

                if (TryGetExactChildFile(current, "mod.json", out _))
                {
                    yield return current;
                    continue;
                }

                string[] children;
                try
                {
                    children = Directory.GetDirectories(current);
                }
                catch (Exception ex)
                {
                    throw new IOException($"Failed to enumerate mod discovery directory: {current}", ex);
                }

                Array.Sort(children, StringComparer.Ordinal);
                for (int i = children.Length - 1; i >= 0; i--)
                {
                    pending.Push(children[i]);
                }
            }
        }

        private static bool ShouldIgnoreDirectory(string path)
        {
            var normalized = path.Replace('\\', '/');
            return normalized.EndsWith("/bin", StringComparison.Ordinal)
                || normalized.EndsWith("/obj", StringComparison.Ordinal)
                || normalized.Contains("/bin/", StringComparison.Ordinal)
                || normalized.Contains("/obj/", StringComparison.Ordinal);
        }

        private static bool TryGetExactChildFile(string directory, string fileName, out string fullPath)
        {
            foreach (var candidate in Directory.EnumerateFiles(directory))
            {
                if (string.Equals(Path.GetFileName(candidate), fileName, StringComparison.Ordinal))
                {
                    fullPath = Path.GetFullPath(candidate);
                    return true;
                }
            }

            fullPath = string.Empty;
            return false;
        }
    }
}
