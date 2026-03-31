using System;
using System.IO;
using System.Text;
using RoadNetworkShowcaseMod.UI;

namespace RoadNetworkShowcaseMod.Runtime
{
    internal sealed class RoadNetworkShowcaseDebugLogWriter
    {
        private string? _snapshotPath;
        private string? _lastSnapshot;

        public string? SnapshotPath => _snapshotPath;

        public void WriteLatest(in RoadNetworkShowcasePanelState state)
        {
            string path = ResolveSnapshotPath();
            string snapshot = BuildSnapshot(state);
            if (string.Equals(snapshot, _lastSnapshot, StringComparison.Ordinal))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, snapshot, Encoding.UTF8);
            _snapshotPath = path;
            _lastSnapshot = snapshot;
        }

        private static string BuildSnapshot(in RoadNetworkShowcasePanelState state)
        {
            var text = new StringBuilder(4096);
            text.AppendLine($"timestamp: {DateTimeOffset.Now:O}");
            text.AppendLine($"title: {state.Title}");
            text.AppendLine($"status: {state.Status}");
            text.AppendLine($"selection: {state.Selection}");
            text.AppendLine("input:");
            AppendIndentedBlock(text, state.Input, 2);
            text.AppendLine($"chunks: {state.Chunks}");
            text.AppendLine($"hint: {state.Hint}");
            text.AppendLine($"actors: {state.Actors.Length}");

            for (int i = 0; i < state.Actors.Length; i++)
            {
                ref readonly RoadNetworkShowcaseActorPanelState actor = ref state.Actors[i];
                text.AppendLine();
                text.AppendLine($"[{i}] {actor.Header}");
                text.AppendLine(actor.Queue);
                text.AppendLine(actor.Query);
                text.AppendLine(actor.Plan);
                text.AppendLine(actor.Select);
                text.AppendLine(actor.Execute);
                text.AppendLine(actor.Check);
                AppendIndentedBlock(text, actor.Path, 2);
            }

            return text.ToString();
        }

        private static void AppendIndentedBlock(StringBuilder text, string content, int spaces)
        {
            string indent = new string(' ', spaces);
            using var reader = new StringReader(content ?? string.Empty);
            while (reader.ReadLine() is string line)
            {
                text.Append(indent).AppendLine(line);
            }
        }

        private static string ResolveSnapshotPath()
        {
            string? repoRoot = TryFindRepoRoot(AppContext.BaseDirectory) ??
                               TryFindRepoRoot(Directory.GetCurrentDirectory());
            string baseDir = string.IsNullOrWhiteSpace(repoRoot)
                ? Path.Combine(AppContext.BaseDirectory, "artifacts")
                : Path.Combine(repoRoot, "artifacts");
            return Path.Combine(baseDir, "runtime", "road_network_showcase", "latest_snapshot.txt");
        }

        private static string? TryFindRepoRoot(string? startPath)
        {
            if (string.IsNullOrWhiteSpace(startPath))
            {
                return null;
            }

            var directory = new DirectoryInfo(startPath);
            if (!directory.Exists && directory.Parent != null)
            {
                directory = directory.Parent;
            }

            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "assets")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "mods")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }
    }
}
