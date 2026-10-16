using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance;

[TestFixture]
public sealed class TestSourceRepositoryWriteGuardTests
{
    [Test]
    public void TestSources_DoNotWriteTrackedRepositoryPaths()
    {
        string repoRoot = FindRepoRoot();
        var failures = new List<string>();
        string testsRoot = Path.Combine(repoRoot, "src", "Tests");

        var writeLinePattern = new Regex(
            @"File\.(WriteAllText|WriteAllBytes|AppendAllText|WriteAllLines)\s*\([^;]*",
            RegexOptions.Compiled);

        foreach (string file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("TestSourceRepositoryWriteGuardTests.cs", StringComparison.Ordinal))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!writeLinePattern.IsMatch(line))
                {
                    continue;
                }

                if (line.Contains("docs/benchmarks/", StringComparison.Ordinal) ||
                    line.Contains("artifacts/acceptance/", StringComparison.Ordinal) ||
                    line.Contains("artifacts/benchmarks/", StringComparison.Ordinal) ||
                    line.Contains("artifacts/GasProductionReport.md", StringComparison.Ordinal) ||
                    line.Contains("Path.Combine(repoRoot", StringComparison.Ordinal) ||
                    line.Contains("Path.Combine(FindRepoRoot()", StringComparison.Ordinal))
                {
                    failures.Add($"{Path.GetRelativePath(repoRoot, file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
