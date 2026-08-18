using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class LayeredSpanLawsonEdgeIndexContractTests
    {
        private static readonly Regex[] UnwrappedSuccessorEdgeIndex =
        {
            new(@"EdgeVertex\s*\(\s*[^,()]+\s*,\s*[A-Za-z_][A-Za-z0-9_]*\s*\+\s*1\s*,", RegexOptions.Compiled),
            new(@"EdgeVertex\s*\(\s*[^,()]+\s*,\s*\(\s*[A-Za-z_][A-Za-z0-9_]*\s*\+\s*1\s*\)\s*,", RegexOptions.Compiled),
        };

        [Test]
        public void NavigationTriangulation_EdgeVertexSuccessorIndex_AlwaysWrapsModuloThree()
        {
            string repoRoot = FindRepoRoot();
            string[] files = Directory
                .GetFiles(Path.Combine(repoRoot, "src", "Core", "Navigation"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                               !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.That(files, Is.Not.Empty, "Lawson edge index contract scanned no source files.");

            var violations = new List<string>();
            foreach (string file in files)
            {
                string source = StripComments(File.ReadAllText(file));
                foreach (Regex pattern in UnwrappedSuccessorEdgeIndex)
                {
                    foreach (Match match in pattern.Matches(source))
                    {
                        violations.Add(
                            $"{Path.GetFileName(file)}: `{match.Value.TrimEnd(',')}` — successor slot must be `(e + 1) % 3`; " +
                            "an unwrapped index hits the switch default, collapses the third edge (C->C) and it never flips.");
                    }
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "Every EdgeVertex successor-slot lookup in Navigation triangulation code must wrap with % 3.\n" +
                string.Join(Environment.NewLine, violations));
        }

        private static string StripComments(string source)
        {
            source = source.Replace("\r\n", "\n");
            source = Regex.Replace(
                source,
                @"/\*.*?\*/",
                match => new string(' ', match.Length),
                RegexOptions.Singleline);
            source = Regex.Replace(
                source,
                @"//[^\n]*",
                match => new string(' ', match.Length));
            return source;
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Repository root (src/Core/Ludots.Core.csproj) not found.");
        }
    }
}
