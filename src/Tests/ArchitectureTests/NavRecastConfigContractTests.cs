using System;
using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavRecastConfigContractTests
    {
        [Test]
        public void AssetsNavmesh_LoadsExplicitRecastSmallProfileRaster()
        {
            string repoRoot = FindRepoRoot();
            NavMeshBakeConfig config = NavMeshBakeConfigLoader.LoadFromRepoRoot(repoRoot);
            Assert.That(config.Recast, Is.Not.Null);
            Assert.That(config.Recast.RasterCellSizeCm, Is.EqualTo(10));
            Assert.That(config.Recast.RasterCellHeightCm, Is.EqualTo(5));
        }

        [Test]
        public void Loader_RequiresExplicitRecastObject()
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => LoadTemp(OmitRecast(ValidNavmeshJson())))!;
            Assert.That(ex.Message, Does.Contain("recast"));
        }

        [Test]
        public void Loader_RejectsUnknownRecastProperty()
        {
            JsonObject root = JsonNode.Parse(ValidNavmeshJson())!.AsObject();
            root["recast"]!.AsObject()["unknown"] = 1;
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => LoadTemp(root.ToJsonString()))!;
            Assert.That(ex.Message, Does.Contain("unknown"));
            Assert.That(ex.Message, Does.Contain("NavMeshBakeConfig.recast"));
        }

        [Test]
        public void Loader_RejectsMissingRecastField()
        {
            JsonObject root = JsonNode.Parse(ValidNavmeshJson())!.AsObject();
            root["recast"]!.AsObject().Remove("rasterCellHeightCm");
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => LoadTemp(root.ToJsonString()))!;
            Assert.That(ex.Message, Does.Contain("rasterCellHeightCm"));
        }

        [Test]
        public void Loader_RejectsNonPositiveRecastValues()
        {
            JsonObject root = JsonNode.Parse(ValidNavmeshJson())!.AsObject();
            root["recast"]!["rasterCellSizeCm"] = 0;
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => LoadTemp(root.ToJsonString()))!;
            Assert.That(ex.Message, Does.Contain("rasterCellSizeCm"));
            Assert.That(ex.Message, Does.Contain("> 0"));
        }

        [Test]
        public void Validate_RejectsNonPositiveFieldsWithPath()
        {
            var config = new NavRecastConfig { RasterCellSizeCm = 10, RasterCellHeightCm = 0 };
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => config.Validate())!;
            Assert.That(ex.Message, Does.Contain("NavMeshBakeConfig.recast.RasterCellHeightCm"));
        }

        private static string OmitRecast(string json)
        {
            JsonObject root = JsonNode.Parse(json)!.AsObject();
            root.Remove("recast");
            return root.ToJsonString();
        }

        private static NavMeshBakeConfig LoadTemp(string navmeshJson)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ludots-recast-config-" + Guid.NewGuid().ToString("N"));
            string coreConfigs = Path.Combine(tempRoot, "Configs");
            Directory.CreateDirectory(Path.Combine(coreConfigs, "Navigation"));
            File.WriteAllText(Path.Combine(coreConfigs, "config_catalog.json"),
                """
                [
                  { "Path": "Navigation/navmesh.json", "Policy": "DeepObject" },
                  { "Path": "Navigation/agent_profiles.json", "Policy": "ArrayById", "IdField": "id" }
                ]
                """);
            File.WriteAllText(Path.Combine(coreConfigs, "Navigation", "navmesh.json"), navmeshJson);
            File.WriteAllText(Path.Combine(coreConfigs, "Navigation", "agent_profiles.json"),
                """
                [
                  {
                    "id": "Small",
                    "radiusCm": 30,
                    "heightCm": 180,
                    "clearanceCm": 40,
                    "draftCm": 0,
                    "beamCm": 0,
                    "mass": 1,
                    "layer": 0
                  }
                ]
                """);
            try
            {
                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", tempRoot);
                var pipeline = new ConfigPipeline(vfs, modLoader: null!);
                var catalog = ConfigCatalogLoader.Load(pipeline);
                var agentProfiles = new AgentProfileConfigLoader(pipeline).Load(catalog);
                return new NavMeshBakeConfigLoader(pipeline, agentProfiles).Load(catalog);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        private static string ValidNavmeshJson()
        {
            return """
            {
              "mode": "offline",
              "algorithm": "recast",
              "profiles": [
                { "id": "Small", "maxClimbCm": 40, "maxSlopeDeg": 45 }
              ],
              "layers": [
                { "id": "Ground", "layer": 0 }
              ],
              "areas": [],
              "runtimeIncremental": {
                "tileBudgetPerFixedTick": 1,
                "includeNeighborTiles": true,
                "heightScaleMeters": 1,
                "minWalkableUpDot": 0.6,
                "cliffHeightThreshold": 1,
                "trackedStructuralEntityCapacity": 256,
                "obstaclePrimitiveCapacity": 512,
                "polygonVertexCapacity": 4096,
                "dirtyTileCapacity": 64,
                "stagedEntryCapacity": 64,
                "publishedTileCapacity": 64,
                "storeGroupCapacity": 8,
                "residentTileCapacity": 128,
                "outputVertexCapacity": 256,
                "outputTriangleCapacity": 512,
                "outputPortalCapacity": 64,
                "initialResidentChunkX": 0,
                "initialResidentChunkZ": 0,
                "initialResidentWidthChunks": 1,
                "initialResidentHeightChunks": 1
              },
              "layeredSpan": {
                "scratchSlotCount": 2,
                "rasterCellSizeCm": 100,
                "rasterHaloCells": 1,
                "sameSurfaceToleranceCm": 5,
                "maxSimplificationErrorCm": 0,
                "heightRounding": "roundHalfAwayFromZero",
                "maxLawsonFlipCount": 100000,
                "columnCapacity": 64,
                "spanCapacity": 128,
                "classifiedSpanCapacity": 128,
                "walkableSpanCapacity": 128,
                "linkCapacity": 256,
                "sheetCapacity": 128,
                "portalIntervalCapacity": 256,
                "regionCapacity": 64,
                "chartCapacity": 32,
                "ringCapacity": 32,
                "contourVertexCapacity": 256,
                "contourEdgeCapacity": 256,
                "seamCapacity": 64,
                "canonicalLinkCapacity": 256,
                "splitPointCapacity": 64,
                "triangulationVertexCapacity": 256,
                "triangulationTriangleCapacity": 512,
                "constrainedEdgeCapacity": 512,
                "borderPortalCapacity": 64,
                "polygonVertexCapacity": 256,
                "adjacencyEdgeCapacity": 1536,
                "bridgeCandidateCapacity": 256,
                "ringWorkCapacity": 64,
                "temporaryConstraintFlagCapacity": 512
              },
              "triangleSurface": {
                "haloPaddingCm": 100
              },
              "recast": {
                "rasterCellSizeCm": 10,
                "rasterCellHeightCm": 5
              }
            }
            """;
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

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }
    }
}
