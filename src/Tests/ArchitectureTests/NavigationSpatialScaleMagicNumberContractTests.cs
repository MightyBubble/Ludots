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
                "Production src/ and mods/ code must use named scale constants instead of inline 256/64/100 literals.\n" +
                string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void StockExemptionList_MayOnlyShrink_AndExcludesCrowdSimulation()
        {
            Assert.That(
                NavigationSpatialScaleMagicNumberExemptions.StockFiles.Length,
                Is.LessThanOrEqualTo(NavigationSpatialScaleMagicNumberExemptions.StockFileCeiling),
                "存量豁免只许减少。清掉一个文件里的字面量后，从清单删掉该文件并下调 StockFileCeiling。");

            var crowd = NavigationSpatialScaleMagicNumberExemptions.StockFiles
                .Where(path =>
                    path.Contains("/CrowdSimulation/", StringComparison.Ordinal) ||
                    path.StartsWith("mods/showcases/crowd_simulation/", StringComparison.Ordinal) ||
                    path.StartsWith("src/Tools/CrowdSimulationMapProbe/", StringComparison.Ordinal) ||
                    path.Contains("/CrowdSimulationMod/", StringComparison.Ordinal))
                .ToArray();
            Assert.That(crowd, Is.Empty, "群体模拟不进存量豁免。\n" + string.Join(Environment.NewLine, crowd));
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
                relative.Contains("/Ludots.Physics2D/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !NavigationSpatialScaleMagicNumberExemptions.StockFiles.Contains(relative);
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
