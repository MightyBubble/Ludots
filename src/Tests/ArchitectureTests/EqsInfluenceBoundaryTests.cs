using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public class EqsInfluenceBoundaryTests
    {
        [Test]
        public void EqsAndInfluence_DoNotReference_PresentationOrRaylib()
        {
            var repoRoot = FindRepoRoot();
            var eqsFiles = Directory.GetFiles(Path.Combine(repoRoot, "src", "Core", "Spatial", "Eqs"), "*.cs", SearchOption.AllDirectories);
            var influenceFiles = Directory.GetFiles(Path.Combine(repoRoot, "src", "Core", "Fields", "Influence"), "*.cs", SearchOption.AllDirectories);

            var forbidden = new[] { "Presentation", "Raylib", "Skia" };
            var violations = eqsFiles.Concat(influenceFiles)
                .SelectMany(file => File.ReadAllLines(file)
                    .Select((line, idx) => new { file, line, lineNum = idx + 1 })
                    .Where(x => x.line.Contains("using") && forbidden.Any(f => x.line.Contains(f, StringComparison.OrdinalIgnoreCase))))
                .ToArray();

            Assert.That(violations, Is.Empty,
                $"EQS and Influence layers must not reference Presentation/Raylib/Skia. Violations:\n" +
                string.Join("\n", violations.Select(v => $"{Path.GetFileName(v.file)}:{v.lineNum} {v.line}")));
        }

        [Test]
        public void Influence_OnlyReads_NeverWritesChunkedField()
        {
            // Contract test: InfluenceField wraps ChunkedField2D but never exposes mutating methods outside Stamp/Decay/Clear.
            // This test verifies the API surface does not leak raw field write access.
            var repoRoot = FindRepoRoot();
            var influenceFieldCs = Path.Combine(repoRoot, "src", "Core", "Fields", "Influence", "InfluenceField.cs");
            var content = File.ReadAllText(influenceFieldCs);

            // InfluenceField should not expose "_field.Set" or direct mutating ChunkedField2D methods as public.
            // The only allowed mutations are Stamp/Decay/Clear. This is a simple heuristic check.
            Assert.That(content, Does.Not.Contain("public void Set("), "InfluenceField should not expose raw Set");
            Assert.That(content, Does.Not.Contain("public ChunkedField2D"), "InfluenceField should not expose raw field");
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
    }
}
