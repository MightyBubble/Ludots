using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ludots.Tests.Architecture;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance;

[Category("ci-gate")]
[Category("arch-guard")]
[TestFixture]
public sealed class CrowdSimulationCommentHygieneTests
{
    private static readonly Regex BoardItemReference = new(
        @"(?<![\w])(?:[LEGCDMT]\d{2}|F0\d|S\d{2})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IssueReference = new(
        @"#\d+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Test]
    public void CrowdSimulationSources_DoNotEmbedBoardOrIssueReferencesInComments()
    {
        string repoRoot = FindRepoRoot();
        string[] files = CollectCrowdSimulationSources(repoRoot);
        Assert.That(files, Is.Not.Empty, "CrowdSimulation comment hygiene scan found no source files.");

        var violations = new List<string>();
        foreach (string file in files)
        {
            foreach ((int lineNumber, string comment) in SourceTextScanner.ReadCommentSegments(file))
            {
                foreach (Match match in BoardItemReference.Matches(comment))
                {
                    violations.Add($"{ToRepoRelative(repoRoot, file)}:{lineNumber}: board reference '{match.Value}'");
                }

                foreach (Match match in IssueReference.Matches(comment))
                {
                    violations.Add($"{ToRepoRelative(repoRoot, file)}:{lineNumber}: issue reference '{match.Value}'");
                }
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "CrowdSimulation sources must not carry kanban item ids or issue numbers in comments; keep those in commits and issues only.\n" +
            string.Join(Environment.NewLine, violations));
    }

    private static string[] CollectCrowdSimulationSources(string repoRoot)
    {
        var roots = new List<string>
        {
            Path.Combine(repoRoot, "src", "Core", "CrowdSimulation"),
            Path.Combine(repoRoot, "src", "Tests", "CrowdSimulationTests"),
            Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation"),
        };

        string navigationRoot = Path.Combine(repoRoot, "mods", "capabilities", "navigation");
        if (Directory.Exists(navigationRoot))
        {
            foreach (string directory in Directory.EnumerateDirectories(navigationRoot))
            {
                string name = Path.GetFileName(directory);
                if (name.StartsWith("CrowdSimulation", StringComparison.Ordinal) &&
                    name.EndsWith("Mod", StringComparison.Ordinal))
                {
                    roots.Add(directory);
                }
            }
        }

        return roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FindRepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null && !File.Exists(Path.Combine(directory, "showcase.registry.json")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string ToRepoRelative(string repoRoot, string absolutePath)
        => Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');
}
