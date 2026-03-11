namespace Ludots.Launcher.Backend;

public static class LauncherWorkspaceSourceResolver
{
    public static IReadOnlyList<string> ResolveSources(string repoRoot, LauncherConfig config)
    {
        var sources = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
            {
                return;
            }

            if (seen.Add(fullPath))
            {
                sources.Add(fullPath);
            }
        }

        Add(Path.Combine(repoRoot, "mods"));
        foreach (var source in config.WorkspaceSources)
        {
            Add(source);
        }

        return sources;
    }
}
