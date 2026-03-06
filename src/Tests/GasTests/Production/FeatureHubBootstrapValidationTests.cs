using System.Collections.Generic;
using System.IO;
using Ludots.Core.Engine;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    public sealed class FeatureHubBootstrapValidationTests
    {
        [Test]
        public void HubModSet_StartupMap_UsesFeatureHub()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string modsRoot = Path.Combine(repoRoot, "mods");

            var modPaths = new List<string>
            {
                Path.Combine(modsRoot, "LudotsCoreMod"),
                Path.Combine(modsRoot, "FeatureHubMod"),
                Path.Combine(modsRoot, "RtsShowcaseMod"),
            };

            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);

            Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo("feature_hub"),
                "Hub composition should land in feature_hub instead of being overridden by showcase mods.");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                var srcDir = Path.Combine(dir.FullName, "src");
                var assetsDir = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(srcDir) && Directory.Exists(assetsDir))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }
    }
}
