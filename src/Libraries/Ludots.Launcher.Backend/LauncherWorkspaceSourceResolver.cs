namespace Ludots.Launcher.Backend;

public static class LauncherWorkspaceSourceResolver
{
    public static IReadOnlyList<string> ResolveSources(string repoRoot, LauncherConfig config)
    {
        var sources = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in config.ScanRoots ?? Enumerable.Empty<LauncherScanRoot>())
        {
            if (root == null || !root.Enabled || string.IsNullOrWhiteSpace(root.Path))
            {
                continue;
            }

            var fullPath = ResolvePath(repoRoot, root.Path);
            if (!Directory.Exists(fullPath))
            {
                continue;
            }

            if (seen.Add(fullPath))
            {
                sources.Add(fullPath);
            }
        }

        return sources;
    }

    public static string ResolvePath(string repoRoot, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(repoRoot, path));
    }
}
