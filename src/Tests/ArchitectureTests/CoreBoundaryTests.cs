using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public class CoreBoundaryTests
    {
        [Test]
        public void LudotsCore_DoesNotReference_Raylib_Client_OrAdapter()
        {
            var repoRoot = FindRepoRoot();
            var coreCsprojPath = Path.Combine(repoRoot, "src", "Core", "Ludots.Core.csproj");
            Assert.That(File.Exists(coreCsprojPath), Is.True, $"Missing: {coreCsprojPath}");

            var doc = XDocument.Load(coreCsprojPath);
            var includes =
                doc.Descendants()
                    .Where(e => e.Name.LocalName is "ProjectReference" or "PackageReference")
                    .Select(e => e.Attribute("Include")?.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToArray();

            var forbidden = new[]
            {
                "Raylib",
                "Ludots.Client.Raylib",
                "Ludots.Adapter.Raylib"
            };

            var offenders =
                includes.Where(i => forbidden.Any(f => i.Contains(f, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

            Assert.That(offenders, Is.Empty, $"Core should not reference platform SDK/impl projects. Offenders: {string.Join(", ", offenders)}");
        }

        [Test]
        public void ScreenProjectionImplementations_MustLiveInCoreOnly()
        {
            var repoRoot = FindRepoRoot();
            var roots = new[]
            {
                Path.Combine(repoRoot, "src"),
                Path.Combine(repoRoot, "mods")
            };

            var pattern = new Regex(@"\b(class|struct)\s+\w+\s*:\s*[^\r\n{]*\bIScreen(Projector|RayProvider)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
            var offenders = new List<string>();

            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    var relativePath = ToRepoRelativePath(repoRoot, file);
                    if (ShouldSkipArchitectureScan(relativePath))
                    {
                        continue;
                    }

                    var content = File.ReadAllText(file);
                    if (!pattern.IsMatch(content))
                    {
                        continue;
                    }

                    if (!relativePath.StartsWith("src/Core/", StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add(relativePath);
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Screen projector/ray provider implementations must live in Core only. Offenders: " + string.Join(", ", offenders));
        }

        [Test]
        public void SelectionInputHandlers_MustLiveInCoreOnly()
        {
            var repoRoot = FindRepoRoot();
            var roots = new[]
            {
                Path.Combine(repoRoot, "src"),
                Path.Combine(repoRoot, "mods")
            };

            var pattern = new Regex(@"\b(class|struct)\s+\w+\s*:\s*[^\r\n{]*\bISelectionInputHandler\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
            var offenders = new List<string>();

            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    var relativePath = ToRepoRelativePath(repoRoot, file);
                    if (ShouldSkipArchitectureScan(relativePath))
                    {
                        continue;
                    }

                    var content = File.ReadAllText(file);
                    if (!pattern.IsMatch(content))
                    {
                        continue;
                    }

                    if (!relativePath.StartsWith("src/Core/", StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add(relativePath);
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Selection input handlers must live in Core only. Offenders: " + string.Join(", ", offenders));
        }

        [Test]
        public void Adapters_AndApps_MustNotContainProjectionMatrixMath()
        {
            var repoRoot = FindRepoRoot();
            var roots = new[]
            {
                Path.Combine(repoRoot, "src", "Adapters"),
                Path.Combine(repoRoot, "src", "Apps")
            };

            var patterns = new[]
            {
                "Matrix4x4.CreateLookAt(",
                "Matrix4x4.CreatePerspectiveFieldOfView(",
                "Matrix4x4.Invert(",
                "Vector4.Transform("
            };

            var offenders = new List<string>();
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        if (patterns.Any(line.Contains))
                        {
                            offenders.Add($"{ToRepoRelativePath(repoRoot, file)}:{i + 1}");
                        }
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Projection/raycast matrix math must stay in Core camera utilities. Offenders: " + string.Join(", ", offenders));
        }

        [Test]
        public void SelectedTag_MustBeDeclaredInCoreOnly()
        {
            var repoRoot = FindRepoRoot();
            var roots = new[]
            {
                Path.Combine(repoRoot, "src"),
                Path.Combine(repoRoot, "mods")
            };

            var offenders = new List<string>();
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    var relativePath = ToRepoRelativePath(repoRoot, file);
                    if (ShouldSkipArchitectureScan(relativePath))
                    {
                        continue;
                    }

                    var content = File.ReadAllText(file);
                    if (!content.Contains("struct SelectedTag", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!string.Equals(relativePath, "src/Core/Input/Selection/SelectedTag.cs", StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add(relativePath);
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "SelectedTag must be declared once from Core only. Offenders: " + string.Join(", ", offenders));
        }

        private static bool ShouldSkipArchitectureScan(string relativePath)
        {
            return relativePath.StartsWith("src/Tests/", StringComparison.OrdinalIgnoreCase);
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

        private static string ToRepoRelativePath(string repoRoot, string absolutePath)
        {
            return Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');
        }
    }
}
