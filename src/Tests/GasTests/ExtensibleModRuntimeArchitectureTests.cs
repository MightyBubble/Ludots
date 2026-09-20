using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ExtensibleModRuntimeArchitectureTests
    {
        [Test]
        public void ModContext_ExposesRegistrationFacadeInsteadOfMutableHub()
        {
            PropertyInfo property = typeof(IModContext).GetProperty(nameof(IModContext.Extensions))!;

            Assert.That(property, Is.Not.Null);
            Assert.That(property.PropertyType, Is.EqualTo(typeof(IModExtensionRegistration)));
            Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(ModExtensionHub)));
        }

        [Test]
        public void GameEngine_KeepsModExtensionHubInternal()
        {
            PropertyInfo property = typeof(GameEngine).GetProperty(
                "ModExtensions",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

            Assert.That(property, Is.Not.Null);
            Assert.That(property.GetMethod, Is.Not.Null);
            Assert.That(property.GetMethod!.IsAssembly, Is.True);
        }

        [Test]
        public void CoreServiceKeys_DoNotPublishStartupExtensionRegistries()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Core", "Scripting", "CoreServiceKeys.cs"));

            Assert.That(source, Does.Not.Contain("ServiceKey<ModExtensionHub>"));
            Assert.That(source, Does.Not.Contain("ServiceKey<GasGraphOpRegistry>"));
            Assert.That(source, Does.Not.Contain("ServiceKey<BuiltinHandlerRegistry>"));
            Assert.That(source, Does.Not.Contain("ServiceKey<PresetTypeRegistry>"));
        }


        [Test]
        public void ProductionCode_DoesNotResolveBuiltinHandlersThroughLegacyEnumParser()
        {
            IReadOnlyList<string> offenders = FindSourceFilesContaining(
                "ParseBuiltinHandlerId",
                "src/Core",
                "mods");

            var filtered = new List<string>(offenders.Count);
            for (int i = 0; i < offenders.Count; i++)
            {
                if (!offenders[i].EndsWith(
                    Path.Combine("Gameplay", "GAS", "Config", "GasEnumParser.cs"),
                    StringComparison.Ordinal))
                {
                    filtered.Add(offenders[i]);
                }
            }

            Assert.That(filtered, Is.Empty);
        }

        private static IReadOnlyList<string> FindSourceFilesContaining(string text, params string[] relativeRoots)
        {
            string repoRoot = FindRepoRoot();
            var offenders = new List<string>();
            for (int i = 0; i < relativeRoots.Length; i++)
            {
                string root = Path.Combine(repoRoot, relativeRoots[i].Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (File.ReadAllText(file).Contains(text, StringComparison.Ordinal))
                    {
                        offenders.Add(Path.GetRelativePath(repoRoot, file));
                    }
                }
            }

            offenders.Sort(StringComparer.Ordinal);
            return offenders;
        }

        private static string FindRepoRoot()
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "assets")) &&
                    Directory.Exists(Path.Combine(dir, "src")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new InvalidOperationException("Cannot find repo root.");
        }
    }
}
