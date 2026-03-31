using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Launcher.Backend;
using NUnit.Framework;
using TimeFlowShowcaseMod;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
public sealed class TimeFlowMiniGameEntryResolveTests
{
    [Test]
    public void TimeFlowMiniGameEntries_ResolveStandaloneStartupMaps()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "timeflow-mini-entries");
        Directory.CreateDirectory(artifactDir);

        var service = new LauncherService(repoRoot);
        var rows = new List<EntryResolveRow>();

        foreach (TimeFlowMiniGameEntry entry in TimeFlowShowcaseMiniGames.EntryMods)
        {
            LauncherResolveResult resolved = service.Resolve(
                new[] { $"mod:{entry.ModId}" },
                LauncherPlatformIds.Raylib,
                LauncherBuildMode.Never);

            LauncherResolvedSetting startupMapSetting = resolved.Plan.Diagnostics.Settings
                .First(setting => string.Equals(setting.Key, "startupMapId", StringComparison.Ordinal));
            string startupMapId = startupMapSetting.EffectiveValue?.GetValue<string>() ?? string.Empty;

            Assert.That(resolved.Plan.RootModIds, Does.Contain(entry.ModId), $"Root mod mismatch for {entry.ModId}.");
            Assert.That(resolved.Plan.OrderedModIds, Does.Contain("TimeFlowShowcaseMod"), $"Showcase dependency missing for {entry.ModId}.");
            Assert.That(startupMapId, Is.EqualTo(entry.MapId), $"Startup map mismatch for {entry.ModId}.");

            rows.Add(new EntryResolveRow(
                entry.ModId,
                entry.MapId,
                entry.MenuTitle,
                startupMapId,
                resolved.Plan.OrderedModIds.ToArray(),
                startupMapSetting.EffectiveSource ?? "(unknown)"));
        }

        File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(rows));
        File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(rows));
        File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid(rows));
    }

    private static string BuildTraceJsonl(IReadOnlyList<EntryResolveRow> rows)
    {
        return string.Join(Environment.NewLine, rows.Select((row, index) => JsonSerializer.Serialize(new
        {
            event_id = $"timeflow-mini-entry-{index + 1:000}",
            mod_id = row.ModId,
            map_id = row.MapId,
            title = row.MenuTitle,
            startup_map_id = row.StartupMapId,
            ordered_mod_ids = row.OrderedModIds,
            startup_map_source = row.StartupMapSource,
            status = "resolved"
        }))) + Environment.NewLine;
    }

    private static string BuildBattleReport(IReadOnlyList<EntryResolveRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: timeflow-mini-entries");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: launch each time-system case as its own mini-game entry instead of entering a single combined debug-heavy pack.");
        sb.AppendLine("- Gameplay domain: launcher resolve path, root-mod selection, dependency ordering, and startup-map overrides for the six standalone entries.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Adapter: `raylib`");
        sb.AppendLine("- Build mode: `never` for plan resolution only");
        sb.AppendLine("- Shared dependency: `TimeFlowShowcaseMod`");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (EntryResolveRow row in rows)
        {
            sb.AppendLine($"- `{row.ModId}` -> `{row.StartupMapId}` | title=`{row.MenuTitle}` | ordered=`{string.Join(", ", row.OrderedModIds)}`");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine("- success: yes");
        sb.AppendLine($"- entries resolved: `{rows.Count}`");
        sb.AppendLine("- verdict: every standalone mini-game mod resolved to its intended startup map while keeping the shared TimeFlow showcase dependency chain.");
        return sb.ToString();
    }

    private static string BuildPathMermaid(IReadOnlyList<EntryResolveRow> rows)
    {
        var lines = new List<string> { "flowchart TD" };
        for (int i = 0; i < rows.Count; i++)
        {
            EntryResolveRow row = rows[i];
            string nodeA = $"A{i}";
            string nodeB = $"B{i}";
            lines.Add($"    {nodeA}[{row.ModId}] --> {nodeB}[{row.StartupMapId}]");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++)
        {
            string candidate = Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj");
            if (File.Exists(candidate))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }

    private sealed record EntryResolveRow(
        string ModId,
        string MapId,
        string MenuTitle,
        string StartupMapId,
        IReadOnlyList<string> OrderedModIds,
        string StartupMapSource);
}
