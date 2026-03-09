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
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (roots == null)
            {
                return results;
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
                var manifestPath = Path.Combine(directory, "mod.json");

                try
                {
                    var manifest = ModManifestJson.ParseStrict(File.ReadAllText(manifestPath), manifestPath);
                    results.Add(new DiscoveredMod(directory, manifestPath, manifest));
                }
                catch (Exception ex)
                {
                    onError?.Invoke(directory, ex);
                }
            }

            return results;
        }

        private static IEnumerable<string> DiscoverModDirectoriesFromRoot(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                yield break;
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

                var manifestPath = Path.Combine(current, "mod.json");
                if (File.Exists(manifestPath))
                {
                    yield return current;
                    continue;
                }

                string[] children;
                try
                {
                    children = Directory.GetDirectories(current);
                }
                catch
                {
                    continue;
                }

                Array.Sort(children, StringComparer.OrdinalIgnoreCase);
                for (int i = children.Length - 1; i >= 0; i--)
                {
                    pending.Push(children[i]);
                }
            }
        }

        private static bool ShouldIgnoreDirectory(string path)
        {
            var normalized = path.Replace('\\', '/');
            return normalized.EndsWith("/bin", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("/obj", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
