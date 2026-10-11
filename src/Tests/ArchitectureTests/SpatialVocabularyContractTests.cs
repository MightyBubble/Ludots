using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class SpatialVocabularyContractTests
{
    private static readonly string[] UnitSuffixes =
    [
        "CmPerSecond", "NavCellArea", "NavCells", "NavTiles", "Fraction",
        "Capacity", "Count", "Scale", "Ratio", "Ticks", "Tries", "Rings", "Slices",
        "Cm", "Dot", "Deg", "Rad", "Hz", "Sec", "SubSteps",
    ];

    private static readonly string[] BareLengthWords = ["Len", "Width", "Padding", "Spacing", "Error", "Area", "LookAhead"];

    private static readonly HashSet<string> DimensionlessConfigNames = new(StringComparer.Ordinal)
    {
        "Version", "Seed", "WorldSeed", "Cost", "Layer", "PushPriority", "NavProfileIndex",
        "CostByArea", "AgentTypeIndex", "NavContextId", "MaxVertsPerPoly",
        "NavCellsPerTile", "NavCellsPerSide", "NavCellsPerFogCell", "FogCellsPerSide",
        "MaxRebuildsPerTick", "MaxRefreshesPerTick", "LeaderSpeed",
        "CatchUp", "ClumpSlack", "Aspect", "MaxSubSteps", "SimSecondsPerTickSecond", "SimSecondsPerTickSecondRaw",
        "BlendCommit", "SightHysteresis",
        "MaxNeighbors", "MaxScan", "SeparationWeight",
        "RestDeadband", "MaxPush", "Smoothing", "MovingBonus", "DominantShare",
        "PlayerA", "PlayerB", "MaxUnits", "BlockCoverage", "AreaIndex",
        "Priority", "LifetimeSecRaw", "InitialUnits", "CentersPerGroup", "PlayerId", "Ema",
        "IndexByPlayerId", "PlayerCount",
    };

    private static readonly HashSet<string> ParityStructuralKeys = new(StringComparer.Ordinal)
    {
        "additive", "agentType", "bakes", "blockers", "box", "bridges", "cases", "cells",
        "clearance", "cmd", "contexts", "count", "entryHashes", "expect", "fnv1a",
        "fogCellsPerSide", "fogTerrain", "frames", "fromFogCell", "got", "hAvgTRaw", "hMaxRaw",
        "hits", "hpaEdges", "hpaNodes", "jumpCandidates", "kind", "mapId", "misses", "name",
        "navCellsPerFogCell", "navCellsPerSide", "navId", "navTileEntries", "note", "opaque",
        "pairs", "player", "polys", "rIdx", "reachable", "rect", "script", "seed", "shape",
        "spawnSeq", "template", "tick", "ticks", "toFogCell", "type", "unitType", "value", "with",
    };

    private static readonly string[] MeterDomainFiles =
    [
        "src/Core/CrowdSimulation/CrowdReferenceMeters.cs",
        "src/Core/CrowdSimulation/Fog/CrowdFogArea.cs",
        "src/Core/CrowdSimulation/Fog/CrowdFog.cs",
        "src/Core/CrowdSimulation/Nav/UpperLayerBake.cs",
    ];

    private static readonly HashSet<string> ConversionIdentifiers = new(StringComparer.Ordinal)
    {
        "CrowdReferenceMeters", "CmToM", "CmToMPrecise", "CmToMRaw", "CmToMFix",
        "TruncatedCmToM", "HalfWidthM", "WorldCmToVisualMeters",
    };

    private static readonly Regex BannedToken = new(
        @"\b(ClusterSize|ClustersPerSide|CellCells|Fcs\w*|fcsM|fcsCm)\b",
        RegexOptions.Compiled);

    private static readonly Regex SingleLetterMember = new(
        @"\b(public|internal)\s+(?:(?:static|readonly|const|required|volatile)\s+)*[A-Za-z_][A-Za-z0-9_<>,\.\[\]\?]*\s+([A-Za-z])\s*(?:[{;=]|$)",
        RegexOptions.Compiled);

    private static readonly Regex MeterIdentifier = new(
        @"\b([A-Za-z_][A-Za-z0-9_]*(?:Meters|M))\b",
        RegexOptions.Compiled);

    [Test]
    public void CrowdConfigNumericProperties_UseUnitSuffixesOrDimensionlessWhitelist()
    {
        var violations = new List<string>();
        foreach (Type type in ConfigTypes())
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || !IsNumeric(property.PropertyType))
                {
                    continue;
                }

                string name = property.Name;
                if (DimensionlessConfigNames.Contains(name))
                {
                    if (IsMeterOrBareCells(name))
                    {
                        violations.Add($"{type.Name}.{name}");
                    }

                    continue;
                }

                if (IsBannedName(name) || !HasUnitSuffix(name))
                {
                    violations.Add($"{type.Name}.{name}");
                }
            }
        }

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    [Test]
    public void CrowdSources_RejectRetiredNames_AndKeepMeterSuffixInTheMeterFiles()
    {
        string repoRoot = FindRepoRoot();
        var violations = new List<string>();
        foreach (string file in CrowdSourceFiles(repoRoot))
        {
            string relative = ToRepoRelative(repoRoot, file);
            string code = StripComments(File.ReadAllText(file));
            foreach (Match match in BannedToken.Matches(code))
            {
                violations.Add($"{relative}: {match.Value}");
            }

            foreach (Match match in SingleLetterMember.Matches(code))
            {
                violations.Add($"{relative}: public/internal member '{match.Groups[2].Value}'");
            }

            if (MeterDomainFiles.Contains(relative, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (Match match in MeterIdentifier.Matches(code))
            {
                if (!ConversionIdentifiers.Contains(match.Value))
                {
                    violations.Add($"{relative}: meter identifier {match.Value}");
                }
            }
        }

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    [Test]
    public void ParityJsonKeys_FollowTheSameSuffixRules()
    {
        string repoRoot = FindRepoRoot();
        string showcase = Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation");
        var violations = new List<string>();
        foreach (string file in Directory.GetFiles(showcase, "*.json", SearchOption.AllDirectories))
        {
            string relative = ToRepoRelative(repoRoot, file);
            if (!relative.Contains("/assets/CrowdSimulation/parity/", StringComparison.Ordinal))
            {
                continue;
            }

            var root = JsonNode.Parse(File.ReadAllText(file)) ?? throw new InvalidOperationException(relative);
            WalkKeys(root, relative, violations);
        }

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations.Distinct()));
    }

    [Test]
    public void CrowdSizeCmKeys_AppearInTheSpatialSsotConfigTable()
    {
        string repoRoot = FindRepoRoot();
        string configPath = Path.Combine(
            repoRoot,
            "mods/capabilities/navigation/CrowdSimulationStandardMod/assets/CrowdSimulationConfig.json");
        var root = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
        var keys = new List<string>();
        CollectSizeCmPaths(root, "", keys);

        string ssot = File.ReadAllText(Path.Combine(repoRoot, "gitbook/architecture/spatial-scale-and-resolution-ssot.md"));
        int start = ssot.IndexOf("## 配置指南", StringComparison.Ordinal);
        int end = ssot.IndexOf("## 配置到行为联动", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        string table = ssot[start..end];

        var missing = keys.Where(key => !table.Contains(key, StringComparison.Ordinal)).ToArray();
        Assert.That(missing, Is.Empty, "SSOT 配置表缺这些 *SizeCm 键:\n" + string.Join(Environment.NewLine, missing));
        Assert.That(keys, Is.Not.Empty);
    }

    private static void WalkKeys(JsonNode node, string relative, List<string> violations)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj)
            {
                if (!IsStructuralOrUnitKey(key))
                {
                    violations.Add($"{relative}: {key}");
                }

                if (value != null)
                {
                    WalkKeys(value, relative, violations);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                if (item != null)
                {
                    WalkKeys(item, relative, violations);
                }
            }
        }
    }

    private static bool IsStructuralOrUnitKey(string key)
    {
        if (key.Length == 1)
        {
            return false;
        }

        if (Regex.IsMatch(key, "^[0-9a-fA-F]{4,}$") || Regex.IsMatch(key, "^[0-9]+$"))
        {
            return true;
        }

        if (key is "clusterSize" or "clustersPerSide" or "cellCells" or "fcsM" or "fcsCm" || key.StartsWith("Fcs", StringComparison.Ordinal))
        {
            return false;
        }

        if (IsBannedName(key) && key != "cells")
        {
            return false;
        }

        return HasUnitSuffix(key) || ParityStructuralKeys.Contains(key);
    }

    private static bool IsBannedName(string name)
        => IsMeterOrBareCells(name) ||
           (BareLengthWords.Any(word => name.EndsWith(word, StringComparison.Ordinal)) && !HasUnitSuffix(name));

    private static bool IsMeterOrBareCells(string name)
    {
        if (name.Equals("Cells", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.EndsWith("Cells", StringComparison.Ordinal) &&
            !name.EndsWith("NavCells", StringComparison.Ordinal) &&
            !name.EndsWith("FogCells", StringComparison.Ordinal))
        {
            return true;
        }

        return name.EndsWith("Meters", StringComparison.Ordinal) ||
            (name.EndsWith("M", StringComparison.Ordinal) && !name.EndsWith("Cm", StringComparison.Ordinal));
    }

    private static bool HasUnitSuffix(string name)
        => UnitSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal));

    private static void CollectSizeCmPaths(JsonObject obj, string prefix, List<string> keys)
    {
        foreach (var (key, value) in obj)
        {
            string path = prefix.Length == 0 ? key : prefix + "." + key;
            if (key.EndsWith("SizeCm", StringComparison.Ordinal))
            {
                keys.Add(path);
            }

            if (value is JsonObject child)
            {
                CollectSizeCmPaths(child, path, keys);
            }
        }
    }

    private static IEnumerable<Type> ConfigTypes()
    {
        return typeof(CrowdSimulationConfig).Assembly.GetTypes()
            .Where(type => type.Namespace == "Ludots.Core.CrowdSimulation.Config" && type.IsClass);
    }

    private static bool IsNumeric(Type type)
    {
        if (type.IsArray)
        {
            type = type.GetElementType()!;
        }

        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
            || type == typeof(float) || type == typeof(double) || type == typeof(decimal)
            || type == typeof(Fix64);
    }

    private static IEnumerable<string> CrowdSourceFiles(string repoRoot)
    {
        string[] roots =
        [
            Path.Combine(repoRoot, "src/Core/CrowdSimulation"),
            Path.Combine(repoRoot, "src/Tools/CrowdSimulationMapProbe"),
            Path.Combine(repoRoot, "mods/showcases/crowd_simulation"),
            Path.Combine(repoRoot, "mods/capabilities/navigation/CrowdSimulationMod"),
        ];
        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string relative = ToRepoRelative(repoRoot, file);
                if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        source = Regex.Replace(source, @"//.*?$", " ", RegexOptions.Multiline);
        return source;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
    }

    private static string ToRepoRelative(string repoRoot, string file)
        => Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
}
