using System;
using System.IO;
using System.Linq;
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
