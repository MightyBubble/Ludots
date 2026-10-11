using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavigationSpatialScaleMagicNumberContractTests
    {
        private static readonly Regex ForbiddenScaleLiteral = new(
            @"(?<![\w.])(?<value>256|64|100)(?:\.0+)?(?:[fFdDmM]|[uUlL]{1,2})?(?![\w.])",
            RegexOptions.Compiled);

        [Test]
        public void BoardBakeAndMassNavigationFlowCode_DoNotInlineSpatialScaleMagicNumbers()
        {
            string repoRoot = FindRepoRoot();
            string[] files = Directory
                .GetFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => IsProductionSource(repoRoot, path))
                .Where(path => IsSpatialScaleSensitiveSource(repoRoot, path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.That(files, Is.Not.Empty, "Navigation spatial scale contract scanned no source files.");

            var violations = new List<string>();
            foreach (string file in files)
            {
                string source = File.ReadAllText(file);
                foreach ((int lineNumber, string code) in StripCommentsAndStrings(source))
                {
                    foreach (Match match in ForbiddenScaleLiteral.Matches(code))
                    {
                        violations.Add($"{ToRepoRelativePath(repoRoot, file)}:{lineNumber}: literal {match.Value}");
                    }
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "Board, nav bake, MassNavigationFlow, and crowd simulation spatial scale code must use named scale constants instead of inline 256/64/100 literals.\n" +
                string.Join(Environment.NewLine, violations));
        }

        /// <summary>
        /// 白名单实际扫到的 src/Core 一级目录。新目录必须进这里或 <see cref="NonSpatialCoreDirectories"/>。
        /// </summary>
        private static readonly string[] CoreScanRoots =
        [
            "CrowdSimulation",
            "Map",
            "MassNavigation",
            "Navigation",
        ];

        /// <summary>不产板格、导航烘焙或群体导航格的 src/Core 一级目录。</summary>
        private static readonly string[] NonSpatialCoreDirectories =
        [
            "Association",
            "Client",
            "Collections",
            "Commands",
            "Components",
            "Config",
            "Diagnostics",
            "Engine",
            "EntityCollections",
            "EntityQueries",
            "Fields",
            "Gameplay",
            "GraphRuntime",
            "Hosting",
            "Input",
            "Knowledge",
            "Layers",
            "Ludots.Movement.Physics2DBridge",
            "Ludots.Physics.Broadphase",
            "Ludots.Physics2D",
            "Maps",
            "Math",
            "Modding",
            "Movement",
            "MovePlanning",
            "Networking",
            "NodeLibraries",
            "ParticipantVisibility",
            "Persistence",
            "Physics",
            "Physics2D",
            "Presentation",
            "Properties",
            "Registry",
            "Scripting",
            "Spatial",
            "StructureCollision",
            "Systems",
            "TransportNetwork",
            "Tweening",
            "TypedCollections",
            "UI",
            "Vision",
        ];

        [Test]
        public void CoreTopLevelDirectories_AreRegisteredAsScanRootOrNonSpatial()
        {
            string repoRoot = FindRepoRoot();
            string[] actual = Directory.GetDirectories(Path.Combine(repoRoot, "src", "Core"))
                .Select(Path.GetFileName)
                .Where(name => name is not null && name is not "bin" and not "obj")
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            string[] registered = CoreScanRoots
                .Concat(NonSpatialCoreDirectories)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] overlap = CoreScanRoots.Intersect(NonSpatialCoreDirectories, StringComparer.Ordinal).ToArray();
            string[] missing = actual.Except(registered, StringComparer.Ordinal).ToArray();
            string[] extra = registered.Except(actual, StringComparer.Ordinal).ToArray();

            Assert.That(overlap, Is.Empty, "扫描根与非空间短名单重叠。\n" + string.Join(Environment.NewLine, overlap));
            Assert.That(missing, Is.Empty, "src/Core 一级目录未登记为扫描根或非空间子系统。\n" + string.Join(Environment.NewLine, missing));
            Assert.That(extra, Is.Empty, "登记了不存在的 src/Core 一级目录。\n" + string.Join(Environment.NewLine, extra));
            Assert.That(registered, Is.EqualTo(actual));

            foreach (string root in CoreScanRoots)
            {
                string directory = Path.Combine(repoRoot, "src", "Core", root);
                bool scanned = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                    .Any(file => IsSpatialScaleSensitiveSource(repoRoot, file));
                Assert.That(scanned, Is.True, $"{root} 登记为扫描根，但白名单没有扫到其中的 .cs。");
            }
        }

        private static bool IsProductionSource(string repoRoot, string file)
        {
            string relative = ToRepoRelativePath(repoRoot, file);
            if (relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("src/Tests/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return relative.StartsWith("src/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("mods/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSpatialScaleSensitiveSource(string repoRoot, string file)
        {
            string relative = ToRepoRelativePath(repoRoot, file);
            if (relative.Equals("src/Core/Spatial/SpatialScaleDefaults.cs", StringComparison.OrdinalIgnoreCase) ||
                relative.Equals("src/Core/CrowdSimulation/CrowdSimulationLimits.cs", StringComparison.OrdinalIgnoreCase) ||
                relative.Contains("/Ludots.Physics2D/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string fileName = Path.GetFileNameWithoutExtension(file);
            return relative.Contains("/Map/Board/", StringComparison.OrdinalIgnoreCase) ||
                relative.Contains("/NavMesh/Bake/", StringComparison.OrdinalIgnoreCase) ||
                relative.Contains("/NavBake.", StringComparison.OrdinalIgnoreCase) ||
                (relative.Contains("/Ludots.Tool/", StringComparison.OrdinalIgnoreCase) && (
                    fileName.Contains("ReactMapDataBinConverter", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("MapVtxmGenerator", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("Program", StringComparison.OrdinalIgnoreCase))) ||
                fileName.Contains("NavTileBuilder", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("MassNavigationFlow", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("FlowField", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("src/Core/CrowdSimulation/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("mods/capabilities/navigation/CrowdSimulationMod/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("mods/showcases/crowd_simulation/", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<(int LineNumber, string Code)> StripCommentsAndStrings(string source)
        {
            bool inBlockComment = false;
            bool inVerbatimString = false;
            string[] lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                char[] chars = lines[lineIndex].ToCharArray();
                int i = 0;
                while (i < chars.Length)
                {
                    if (inBlockComment)
                    {
                        if (chars[i] == '*' && i + 1 < chars.Length && chars[i + 1] == '/')
                        {
                            chars[i] = ' ';
                            chars[i + 1] = ' ';
                            i += 2;
                            inBlockComment = false;
                            continue;
                        }

                        chars[i++] = ' ';
                        continue;
                    }

                    if (inVerbatimString)
                    {
                        if (chars[i] == '"' && i + 1 < chars.Length && chars[i + 1] == '"')
                        {
                            chars[i] = ' ';
                            chars[i + 1] = ' ';
                            i += 2;
                            continue;
                        }

                        if (chars[i] == '"')
                        {
                            chars[i++] = ' ';
                            inVerbatimString = false;
                            continue;
                        }

                        chars[i++] = ' ';
                        continue;
                    }

                    if (chars[i] == '/' && i + 1 < chars.Length && chars[i + 1] == '/')
                    {
                        Clear(chars, i, chars.Length - i);
                        break;
                    }

                    if (chars[i] == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
                    {
                        chars[i] = ' ';
                        chars[i + 1] = ' ';
                        i += 2;
                        inBlockComment = true;
                        continue;
                    }

                    if (StartsVerbatimString(chars, i, out int verbatimPrefixLength))
                    {
                        Clear(chars, i, verbatimPrefixLength);
                        i += verbatimPrefixLength;
                        inVerbatimString = true;
                        continue;
                    }

                    if (chars[i] == '"')
                    {
                        i = ClearNormalString(chars, i);
                        continue;
                    }

                    if (chars[i] == '\'')
                    {
                        i = ClearCharLiteral(chars, i);
                        continue;
                    }

                    i++;
                }

                yield return (lineIndex + 1, new string(chars));
            }
        }

        private static bool StartsVerbatimString(char[] chars, int index, out int prefixLength)
        {
            prefixLength = 0;
            if (chars[index] == '@' && index + 1 < chars.Length && chars[index + 1] == '"')
            {
                prefixLength = 2;
                return true;
            }

            if (chars[index] == '$' && index + 2 < chars.Length && chars[index + 1] == '@' && chars[index + 2] == '"')
            {
                prefixLength = 3;
                return true;
            }

            if (chars[index] == '@' && index + 2 < chars.Length && chars[index + 1] == '$' && chars[index + 2] == '"')
            {
                prefixLength = 3;
                return true;
            }

            return false;
        }

        private static int ClearNormalString(char[] chars, int start)
        {
            chars[start] = ' ';
            int i = start + 1;
            while (i < chars.Length)
            {
                bool escaped = chars[i] == '\\';
                char current = chars[i];
                chars[i] = ' ';
                if (escaped && i + 1 < chars.Length)
                {
                    chars[i + 1] = ' ';
                    i += 2;
                    continue;
                }

                if (!escaped && current == '"')
                {
                    return i + 1;
                }

                i++;
            }

            return i;
        }

        private static int ClearCharLiteral(char[] chars, int start)
        {
            chars[start] = ' ';
            int i = start + 1;
            while (i < chars.Length)
            {
                bool escaped = chars[i] == '\\';
                char current = chars[i];
                chars[i] = ' ';
                if (escaped && i + 1 < chars.Length)
                {
                    chars[i + 1] = ' ';
                    i += 2;
                    continue;
                }

                if (!escaped && current == '\'')
                {
                    return i + 1;
                }

                i++;
            }

            return i;
        }

        private static void Clear(char[] chars, int start, int count)
        {
            Array.Fill(chars, ' ', start, count);
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                var candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }

        private static string ToRepoRelativePath(string repoRoot, string file)
        {
            return Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
        }
    }
}
