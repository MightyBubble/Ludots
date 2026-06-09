using System.IO;
using System.Text.Json;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Diagnostics;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavBakeDiagnosticsContractTests
    {
        [Test]
        public void NavBakeDiagnostics_UsesStableSchemaAndOutputFileName()
        {
            Assert.That(NavBakeDiagnosticsContract.SchemaVersion, Is.EqualTo("ludots.nav-bake-diagnostics.v1"));
            Assert.That(NavMeshConfigPaths.BakeDiagnosticsFileName, Is.EqualTo("nav-bake-diagnostics.json"));
            Assert.That(
                NavAssetPaths.GetBakeDiagnosticsRelativePath("mass_navigation"),
                Is.EqualTo("assets/Data/Nav/mass_navigation/nav-bake-diagnostics.json"));
        }

        [Test]
        public void NavBakeLayerProfileSummary_SeparatesObservedStates()
        {
            NavBakeLayerProfileSummary summary = NavBakeLayerProfileSummary.Create(
                layer: 2,
                layerId: "Air",
                profileId: "AirScout",
                targetChunks: 65_536,
                bakedTiles: 32_768,
                failedTiles: 2,
                missingTiles: 3,
                dirtyTiles: 4,
                notLoadedTiles: 32_759);

            Assert.That(summary.CoveragePercent, Is.EqualTo(50));
            Assert.That(summary.IsComplete, Is.False);
            Assert.That(summary.FailedTiles, Is.EqualTo(2));
            Assert.That(summary.MissingTiles, Is.EqualTo(3));
            Assert.That(summary.DirtyTiles, Is.EqualTo(4));
            Assert.That(summary.NotLoadedTiles, Is.EqualTo(32_759));
        }

        [Test]
        public void NavBakeDiagnosticsLoader_LoadsUniqueDocumentFromMountedAssets()
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "nav-bake-diagnostics-loader-test");
            string coreRoot = Path.Combine(root, "assets");
            string docDir = Path.Combine(coreRoot, "Data", "Nav", "mass_navigation");
            Directory.CreateDirectory(docDir);
            try
            {
                File.WriteAllText(
                    Path.Combine(docDir, NavMeshConfigPaths.BakeDiagnosticsFileName),
                    JsonSerializer.Serialize(new NavBakeDiagnosticsDocument
                    {
                        SchemaVersion = NavBakeDiagnosticsContract.SchemaVersion,
                        MapId = "mass_navigation",
                        TargetChunkCount = 65_536,
                        WorldChunkCount = 65_536,
                    }));

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", coreRoot);

                NavBakeDiagnosticsDocument? loaded = NavBakeDiagnosticsLoader.TryLoad(
                    vfs,
                    loadedModIds: null,
                    "mass_navigation");

                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.MapId, Is.EqualTo("mass_navigation"));
                Assert.That(loaded.TargetChunkCount, Is.EqualTo(65_536));
                Assert.That(loaded.WorldChunkCount, Is.EqualTo(65_536));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
