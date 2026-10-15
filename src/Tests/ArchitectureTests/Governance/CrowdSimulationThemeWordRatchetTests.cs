using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance;

/// <summary>
/// 群体内核只认区域、移动类型、链接种类、两个层这些固定的词；地形、区域、模板、移动类型的 id 属于 Mod 数据。
/// 这里按驼峰分段数 <c>src/Core/CrowdSimulation</c> 里还剩多少这类词，计数只许下降。
/// </summary>
[Category("ci-gate")]
[Category("arch-guard")]
public sealed class CrowdSimulationThemeWordRatchetTests
{
    public const int MaxThemeWordSegments = 675;

    private static readonly HashSet<string> ThemeWords = new(StringComparer.Ordinal)
    {
        "bridge", "bridges", "deck", "decks", "jump", "jumps", "jumping", "jumped",
        "water", "shore", "mountain", "cliff", "building", "road", "roads",
        "flooded", "burning", "barrier", "leaper", "climber", "amphib", "ground",
    };

    private static readonly Regex Word = new(@"[A-Za-z]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CamelSegment = new(
        @"[A-Z]+(?=[A-Z][a-z])|[A-Z]?[a-z]+|[A-Z]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Test]
    public void CoreCrowdSimulation_ThemeWordSegments_DoNotIncrease()
    {
        string root = Path.Combine(FindRepoRoot(), "src", "Core", "CrowdSimulation");
        var perWord = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int total = 0;
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(file)) continue;
            foreach (Match word in Word.Matches(File.ReadAllText(file)))
            {
                foreach (Match segment in CamelSegment.Matches(word.Value))
                {
                    string lower = segment.Value.ToLowerInvariant();
                    if (!ThemeWords.Contains(lower)) continue;
                    perWord[lower] = perWord.TryGetValue(lower, out int n) ? n + 1 : 1;
                    total++;
                }
            }
        }

        string breakdown = string.Join(", ", perWord.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}"));
        TestContext.Out.WriteLine($"theme word segments {total}: {breakdown}");
        Assert.That(
            total,
            Is.LessThanOrEqualTo(MaxThemeWordSegments),
            $"src/Core/CrowdSimulation theme word segments {total} exceeded ratchet {MaxThemeWordSegments} ({breakdown}). " +
            "Data ids belong in mod config; the constant may only decrease.");
    }

    private static bool IsBuildArtifact(string path)
    {
        foreach (string part in path.Replace('\\', '/').Split('/'))
        {
            if (part is "bin" or "obj") return true;
        }

        return false;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "assets")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
    }
}
