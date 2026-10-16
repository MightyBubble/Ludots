using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance;

/// <summary>
/// 群体内核和群体工具只认区域、移动类型、链接种类、两个层这些固定的词；地形、区域、模板、移动类型的 id 属于 Mod 数据。
/// 英文按驼峰分段数，中文按词数，两处上限都是 0。
/// 豁免只给共享 API 的确切名字：它们声明在扫描范围之外，群体代码只是调用或引用。
/// </summary>
[Category("ci-gate")]
[Category("arch-guard")]
public sealed class CrowdSimulationThemeWordRatchetTests
{
    public const int MaxCoreThemeWords = 0;
    public const int MaxCrowdToolThemeWords = 0;

    private static readonly HashSet<string> ThemeWords = new(StringComparer.Ordinal)
    {
        "bridge", "bridges", "deck", "decks", "jump", "jumps", "jumping", "jumped",
        "water", "shore", "mountain", "cliff", "building", "road", "roads",
        "flooded", "burning", "barrier", "leaper", "climber", "amphib", "ground",
    };

    private static readonly string[] ChineseThemeWords =
    {
        "桥", "跳跃", "甲板", "地面", "建筑", "道路", "水域", "水面", "泛洪", "火场", "岸边", "岸上", "山体", "悬崖", "路障",
    };

    private static readonly SharedName[] SharedNames =
    {
        new("TryRaycastGround", "src/Platform/Ludots.Platform.Abstractions/IContinuousHeightmap.cs"),
        new("LogicTerrainSurfaceFlags.Water", "src/Core/Navigation/Terrain/LogicTerrainField.cs"),
        new("WaterHeightLevel", "src/Core/Navigation/Terrain/LogicTerrainField.cs"),
        new("SnapToGround", "src/Core/Presentation/Presenters/BehaviorSlot.cs"),
        new("Ludots.Editor.Bridge", "src/Tools/Ludots.Editor.Bridge/Ludots.Editor.Bridge.csproj"),
        new("Ludots.AgentBridge", "src/Libraries/Ludots.AgentBridge/Ludots.AgentBridge.csproj"),
    };

    private static readonly string[] CoreScope = { "src/Core/CrowdSimulation" };

    private static readonly string[] CrowdToolScope =
    {
        "src/Tools/Ludots.Tool/Crowd*.cs",
        "src/Tools/CrowdSimulationMapProbe",
        "src/Tools/CrowdSimulationRegress",
        "src/Tools/Ludots.Editor.React/src/pages/CrowdConfigEditorPage.tsx",
        "src/Tools/Ludots.Editor.Bridge/CrowdSimulationEditorEndpoints.cs",
        "src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibTerrainTypeOverlayBinding.cs",
        "src/Libraries/Ludots.AgentBridge/Tools/CrowdConfigTools.cs",
    };

    private static readonly Regex Word = new(@"[A-Za-z]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CamelSegment = new(
        @"[A-Z]+(?=[A-Z][a-z])|[A-Z]?[a-z]+|[A-Z]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Test]
    public void CoreCrowdSimulation_ThemeWords_StayAtCap() =>
        AssertWithinCap("src/Core/CrowdSimulation", CoreScope, MaxCoreThemeWords);

    [Test]
    public void CrowdTools_ThemeWords_StayAtCap() =>
        AssertWithinCap("crowd tools", CrowdToolScope, MaxCrowdToolThemeWords);

    [Test]
    public void SharedNames_AreDeclaredOutsideScannedScopes()
    {
        string repoRoot = FindRepoRoot();
        var scanned = new HashSet<string>(
            CoreScope.Concat(CrowdToolScope).SelectMany(entry => Expand(repoRoot, entry)),
            StringComparer.Ordinal);
        foreach (SharedName shared in SharedNames)
        {
            string path = Path.Combine(repoRoot, shared.DeclaredIn);
            Assert.That(File.Exists(path), Is.True, $"{shared.Name}: {shared.DeclaredIn} 不存在。");
            Assert.That(scanned.Contains(Path.GetFullPath(path)), Is.False, $"{shared.Name}: {shared.DeclaredIn} 在扫描范围里，不是共享 API。");
            string lastSegment = shared.Name.Split('.')[^1];
            bool declared = Path.GetFileNameWithoutExtension(path) == shared.Name || File.ReadAllText(path).Contains(lastSegment, StringComparison.Ordinal);
            Assert.That(declared, Is.True, $"{shared.DeclaredIn} 里找不到 {shared.Name}。");
        }
    }

    private static void AssertWithinCap(string label, IEnumerable<string> scope, int cap)
    {
        string repoRoot = FindRepoRoot();
        var hits = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int total = 0;
        foreach (string file in scope.SelectMany(entry => Expand(repoRoot, entry)))
        {
            string text = StripSharedNames(File.ReadAllText(file));
            string relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            foreach (Match word in Word.Matches(text))
            {
                foreach (Match segment in CamelSegment.Matches(word.Value))
                {
                    if (!ThemeWords.Contains(segment.Value.ToLowerInvariant())) continue;
                    Count(hits, $"{relative}:{word.Value}");
                    total++;
                }
            }

            foreach (string chinese in ChineseThemeWords)
            {
                for (int at = text.IndexOf(chinese, StringComparison.Ordinal); at >= 0; at = text.IndexOf(chinese, at + chinese.Length, StringComparison.Ordinal))
                {
                    Count(hits, $"{relative}:{chinese}");
                    total++;
                }
            }
        }

        string breakdown = string.Join(", ", hits.Select(p => $"{p.Key}={p.Value}"));
        TestContext.Out.WriteLine($"{label} theme words {total}: {breakdown}");
        Assert.That(
            total,
            Is.LessThanOrEqualTo(cap),
            $"{label} theme words {total} exceeded ratchet {cap} ({breakdown}). " +
            "Data ids belong in mod config; the constant may only decrease.");
    }

    private static void Count(SortedDictionary<string, int> hits, string key) =>
        hits[key] = hits.TryGetValue(key, out int n) ? n + 1 : 1;

    private static string StripSharedNames(string text)
    {
        foreach (SharedName shared in SharedNames)
        {
            text = Regex.Replace(text, $@"(?<![A-Za-z0-9_]){Regex.Escape(shared.Name)}(?![A-Za-z0-9_])", " ");
        }

        return text;
    }

    private static IEnumerable<string> Expand(string repoRoot, string entry)
    {
        string path = Path.GetFullPath(Path.Combine(repoRoot, entry));
        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(file => !IsBuildArtifact(file) && IsSource(file))
                .OrderBy(file => file, StringComparer.Ordinal);
        }

        string directory = Path.GetDirectoryName(path)!;
        string pattern = Path.GetFileName(path);
        string[] files = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
        Assert.That(files, Is.Not.Empty, $"扫描范围 {entry} 没有匹配的文件。");
        return files;
    }

    private static bool IsSource(string path) => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".tsx", StringComparison.Ordinal) || path.EndsWith(".ts", StringComparison.Ordinal);

    private static bool IsBuildArtifact(string path)
    {
        foreach (string part in path.Replace('\\', '/').Split('/'))
        {
            if (part is "bin" or "obj" or "node_modules") return true;
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

    private sealed record SharedName(string Name, string DeclaredIn);
}
