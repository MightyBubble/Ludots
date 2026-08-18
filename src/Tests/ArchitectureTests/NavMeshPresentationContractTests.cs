using System;
using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Presentation.Navigation;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavMeshPresentationContractTests
    {
        private const string GroundLayerId = "Ground";

        [Test]
        public void Buffer_PublishesExactNavTileReferencesAndTopology_NotSmoke()
        {
            var buffer = new NavMeshPresentationBuffer(tileCapacity: 4);
            NavMeshPresentationStyle style = CreateStyle();
            buffer.BeginFrame(
                layer: 3,
                profile: 1,
                profileId: "Small",
                NavBakeMode.Offline,
                NavBakeAlgorithmKind.Recast,
                storeRevision: 9u,
                stateRevision: 4u,
                new NavBuildConfig(1f, 0.6f, 1),
                in style);

            NavTile first = CreateFlatTile(chunkX: 1, chunkY: 0, version: 2);
            NavTile second = CreateFlatTile(chunkX: 0, chunkY: 1, version: 3);
            buffer.AddTile(first);
            buffer.AddTile(second);

            Assert.That(buffer.TileCount, Is.EqualTo(2));
            Assert.That(buffer.Layer, Is.EqualTo(3));
            Assert.That(buffer.Profile, Is.EqualTo(1));
            Assert.That(buffer.ProfileId, Is.EqualTo("Small"));
            Assert.That(buffer.Mode, Is.EqualTo(NavBakeMode.Offline));
            Assert.That(buffer.Algorithm, Is.EqualTo(NavBakeAlgorithmKind.Recast));
            Assert.That(buffer.StoreRevision, Is.EqualTo(9u));
            Assert.That(buffer.StateRevision, Is.EqualTo(4u));
            Assert.That(buffer.BuildConfig.HeightScaleMeters, Is.EqualTo(1f));
            Assert.That(buffer.BuildConfig.CliffHeightThreshold, Is.EqualTo(1));
            Assert.That(buffer.TileVersions[0], Is.EqualTo(2u));
            Assert.That(buffer.TileVersions[1], Is.EqualTo(3u));

            // Zero-copy: the buffer publishes the exact authoritative NavTile references,
            // and triangle/vertex arrays must be the same arrays the store produced.
            Assert.That(buffer.Tiles[0], Is.SameAs(first));
            Assert.That(buffer.Tiles[1], Is.SameAs(second));
            Assert.That(ReferenceEquals(buffer.Tiles[0].TriA, first.TriA), Is.True);
            Assert.That(ReferenceEquals(buffer.Tiles[0].TriB, first.TriB), Is.True);
            Assert.That(ReferenceEquals(buffer.Tiles[0].TriC, first.TriC), Is.True);
            Assert.That(ReferenceEquals(buffer.Tiles[0].VertexXcm, first.VertexXcm), Is.True);
            Assert.That(ReferenceEquals(buffer.Tiles[0].VertexYcm, first.VertexYcm), Is.True);
            Assert.That(ReferenceEquals(buffer.Tiles[0].VertexZcm, first.VertexZcm), Is.True);

            // Topology round-trip: triangle indices reference the published vertex arrays.
            Assert.That(first.TriangleCount, Is.EqualTo(2));
            for (int triangle = 0; triangle < first.TriangleCount; triangle++)
            {
                Assert.That(first.TriA[triangle], Is.LessThan(first.VertexCount));
                Assert.That(first.TriB[triangle], Is.LessThan(first.VertexCount));
                Assert.That(first.TriC[triangle], Is.LessThan(first.VertexCount));
            }
        }

        [Test]
        public void Buffer_CapacityExhaustion_HardFailsAndNamesCapacityOwner()
        {
            var buffer = new NavMeshPresentationBuffer(tileCapacity: 1);
            buffer.BeginFrame(
                0,
                0,
                "Small",
                NavBakeMode.Offline,
                NavBakeAlgorithmKind.Recast,
                storeRevision: 1u,
                stateRevision: 1u,
                new NavBuildConfig(1f, 0.6f, 1),
                CreateStyle());
            buffer.AddTile(CreateFlatTile(chunkX: 0, chunkY: 0, version: 1));

            InvalidOperationException? overflow = Assert.Throws<InvalidOperationException>(
                () => buffer.AddTile(CreateFlatTile(chunkX: 1, chunkY: 0, version: 1)));
            Assert.That(overflow!.Message, Does.Contain("navMeshTileCapacity"));
        }

        [Test]
        public void System_PublishesSourceMetadataAccurately()
        {
            PresentationHarness harness = CreateHarness();
            harness.Store.Replace(CreateFlatTile(chunkX: 0, chunkY: 0, version: 1));
            harness.Store.Replace(CreateFlatTile(chunkX: 1, chunkY: 0, version: 2));
            harness.State.Configure(enabled: true, layer: 0, profile: 0, CreateStyle());

            float dt = 0f;
            harness.System.Update(in dt);

            Assert.That(harness.Buffer.TileCount, Is.EqualTo(2));
            Assert.That(harness.Buffer.Layer, Is.EqualTo(0));
            Assert.That(harness.Buffer.Profile, Is.EqualTo(0));
            Assert.That(harness.Buffer.ProfileId, Is.EqualTo("Small"));
            Assert.That(harness.Buffer.Mode, Is.EqualTo(NavBakeMode.Offline));
            Assert.That(harness.Buffer.Algorithm, Is.EqualTo(NavBakeAlgorithmKind.Recast));
            Assert.That(harness.Buffer.StoreRevision, Is.EqualTo(harness.Store.Revision));
            Assert.That(harness.Buffer.BuildConfig.HeightScaleMeters, Is.EqualTo(1f));
            Assert.That(harness.Buffer.BuildConfig.MinWalkableUpDot, Is.EqualTo(0.6f));
            Assert.That(harness.Buffer.BuildConfig.CliffHeightThreshold, Is.EqualTo(1));
            Assert.That(harness.Buffer.TileVersions[0], Is.EqualTo(1u));
            Assert.That(harness.Buffer.TileVersions[1], Is.EqualTo(2u));
        }

        [Test]
        public void System_Disabled_PublishesNotReadyFrameWithZeroTiles()
        {
            PresentationHarness harness = CreateHarness();
            harness.Store.Replace(CreateFlatTile(chunkX: 0, chunkY: 0, version: 1));

            float dt = 0f;
            harness.System.Update(in dt);
            Assert.That(harness.Buffer.TileCount, Is.EqualTo(0));
        }

        [Test]
        public void System_EnabledWithMissingNavQueryServices_HardFailsDiagnosably()
        {
            PresentationHarness harness = CreateHarness();
            harness.State.Configure(enabled: true, layer: 0, profile: 0, CreateStyle());
            harness.Engine.RemoveService(CoreServiceKeys.NavQueryServices);

            float dt = 0f;
            InvalidOperationException? missing = Assert.Throws<InvalidOperationException>(
                () => harness.System.Update(in dt));
            Assert.That(missing!.Message, Does.Contain("NavQueryServices"));
            Assert.That(missing.Message, Does.Contain("Feature.NavMesh:On"));
        }

        [Test]
        public void System_EnabledWithMissingBakeConfigOrProfiles_HardFailsDiagnosably()
        {
            PresentationHarness harness = CreateHarness();
            harness.State.Configure(enabled: true, layer: 0, profile: 0, CreateStyle());
            harness.Engine.RemoveService(CoreServiceKeys.NavMeshBakeConfig);

            float dt = 0f;
            InvalidOperationException? missingConfig = Assert.Throws<InvalidOperationException>(
                () => harness.System.Update(in dt));
            Assert.That(missingConfig!.Message, Does.Contain("NavMeshBakeConfig"));

            harness.Engine.RemoveService(CoreServiceKeys.NavMeshProfiles);
            harness.Engine.SetService(CoreServiceKeys.NavMeshBakeConfig, CreateBakeConfig());
            InvalidOperationException? missingProfiles = Assert.Throws<InvalidOperationException>(
                () => harness.System.Update(in dt));
            Assert.That(missingProfiles!.Message, Does.Contain("NavMeshProfiles"));
        }

        [Test]
        public void System_SelectedStoreMissingWhileEnabled_HardFailsDiagnosably()
        {
            PresentationHarness harness = CreateHarness();
            harness.State.Configure(enabled: true, layer: 7, profile: 0, CreateStyle());

            float dt = 0f;
            InvalidOperationException? missing = Assert.Throws<InvalidOperationException>(
                () => harness.System.Update(in dt));
            Assert.That(missing!.Message, Does.Contain("layer=7"));
            Assert.That(missing.Message, Does.Contain("profile=0"));
        }

        [Test]
        public void System_Enabled_RequiresAdapterCapability_FailFast()
        {
            PresentationHarness harness = CreateHarness(capabilities: null);
            harness.Store.Replace(CreateFlatTile(chunkX: 0, chunkY: 0, version: 1));
            harness.State.Configure(enabled: true, layer: 0, profile: 0, CreateStyle());

            float dt = 0f;
            InvalidOperationException? unsupported = Assert.Throws<InvalidOperationException>(
                () => harness.System.Update(in dt));
            Assert.That(unsupported!.Message, Does.Contain("NavMeshTileGeometry"));
        }

        [Test]
        public void CapabilityValidator_RequiresExplicitNavMeshTileGeometry()
        {
            InvalidOperationException? missing = Assert.Throws<InvalidOperationException>(
                () => NavMeshPresentationCapabilityValidator.Require(null));
            Assert.That(missing!.Message, Does.Contain("NavMeshTileGeometry"));

            var withoutFlag = new PresentationAdapterCapabilities(PresentationVisualCapabilities.Decal);
            InvalidOperationException? unsupported = Assert.Throws<InvalidOperationException>(
                () => NavMeshPresentationCapabilityValidator.Require(withoutFlag));
            Assert.That(unsupported!.Message, Does.Contain("NavMeshTileGeometry"));

            var withFlag = new PresentationAdapterCapabilities(PresentationVisualCapabilities.NavMeshTileGeometry);
            Assert.DoesNotThrow(() => NavMeshPresentationCapabilityValidator.Require(withFlag));
        }

        [Test]
        public void MetadataFormatter_IsDeterministic_AndHardFailsUnknownEnums()
        {
            PresentationHarness harness = CreateHarness();
            harness.Store.Replace(CreateFlatTile(chunkX: 0, chunkY: 0, version: 7));
            harness.State.Configure(enabled: true, layer: 0, profile: 0, CreateStyle());

            float dt = 0f;
            harness.System.Update(in dt);

            string line = harness.Buffer.FormatMetadataLine();
            Assert.That(line, Does.Contain("navmesh layer=0 profile=0 id=Small mode=offline algorithm=recast"));
            Assert.That(line, Does.Contain("storeRev="));
            Assert.That(line, Does.Contain("tiles=1"));
            Assert.That(line, Does.Contain("v7"));
            Assert.That(harness.Buffer.FormatMetadataLine(), Is.EqualTo(line));

            var buffer = new NavMeshPresentationBuffer(1);
            buffer.BeginFrame(
                0,
                0,
                string.Empty,
                (NavBakeMode)byte.MaxValue,
                NavBakeAlgorithmKind.Recast,
                storeRevision: 0u,
                stateRevision: 0u,
                default,
                CreateStyle());
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.FormatMetadataLine());
        }

        [Test]
        public void System_SteadyStateUpdate_AllocatesZeroManagedBytesAfterWarmup()
        {
            PresentationHarness harness = CreateHarness();
            harness.Store.Replace(CreateFlatTile(chunkX: 0, chunkY: 0, version: 1));
            harness.Store.Replace(CreateFlatTile(chunkX: 1, chunkY: 0, version: 1));
            harness.State.Configure(enabled: true, layer: 0, profile: 0, CreateStyle());

            float dt = 0f;
            for (int i = 0; i < 8; i++)
            {
                harness.System.Update(in dt);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++)
            {
                harness.System.Update(in dt);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0L), $"Steady-state NavMesh presentation allocated {allocated} managed bytes.");
            Assert.That(harness.Buffer.TileCount, Is.EqualTo(2));
        }

        private static PresentationHarness CreateHarness()
            => CreateHarness(
                new PresentationAdapterCapabilities(PresentationVisualCapabilities.NavMeshTileGeometry));

        private static PresentationHarness CreateHarness(PresentationAdapterCapabilities? capabilities)
        {
            var engine = new GameEngine();
            var store = new NavTileStore(_ => throw new InvalidOperationException("NavMesh presentation contract publishes before disk load."));
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore>
                {
                    [new NavQueryServiceKey(0, 0)] = store
                });
            NavMeshBakeConfig config = CreateBakeConfig();
            var profileRegistry = new NavMeshProfileRegistry(config, CreateAgentProfiles());

            engine.SetService(CoreServiceKeys.NavQueryServices, registry);
            engine.SetService(CoreServiceKeys.NavMeshBakeConfig, config);
            engine.SetService(CoreServiceKeys.NavMeshProfiles, profileRegistry);
            if (capabilities != null)
            {
                engine.SetService(CoreServiceKeys.PresentationAdapterCapabilities, capabilities);
            }

            var state = new NavMeshPresentationState();
            var buffer = new NavMeshPresentationBuffer(tileCapacity: 8);
            var system = new NavMeshPresentationSystem(engine, state, buffer);
            return new PresentationHarness(engine, store, state, buffer, system);
        }

        private static NavMeshBakeConfig CreateBakeConfig()
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeOffline,
                Algorithm = NavBakeNames.AlgorithmRecast,
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 }
                },
                Layers = new List<NavLayerConfig>
                {
                    new NavLayerConfig { Id = GroundLayerId, Layer = 0 }
                },
                Areas = new List<NavAreaCostConfig>(),
                RuntimeIncremental = new NavRuntimeIncrementalConfig
                {
                    TileBudgetPerFixedTick = 1,
                    IncludeNeighborTiles = true,
                    HeightScaleMeters = 1f,
                    MinWalkableUpDot = 0.6f,
                    CliffHeightThreshold = 1
                }
            };
        }

        private static AgentProfileRegistry CreateAgentProfiles()
        {
            return new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig
                {
                    Id = "Small",
                    RadiusCm = 30,
                    HeightCm = 180,
                    ClearanceCm = 40,
                    Mass = 1,
                    Layer = 0
                }
            });
        }

        private static NavMeshPresentationStyle CreateStyle()
            => new NavMeshPresentationStyle(
                new NavMeshPresentationColor(0.1f, 0.2f, 0.3f, 0.4f),
                new NavMeshPresentationColor(0.2f, 0.3f, 0.4f, 0.5f),
                heightOffsetMeters: 0.05f,
                drawFill: true,
                drawEdges: true);

        private static NavTile CreateFlatTile(int chunkX, int chunkY, uint version, int layer = 0)
            => DefaultGridNavTileFactory.CreateFlatTile(
                chunkX,
                chunkY,
                layer,
                version,
                chunkSizeCells: 4,
                cellSizeCm: 100);

        private sealed class PresentationHarness
        {
            public PresentationHarness(
                GameEngine engine,
                NavTileStore store,
                NavMeshPresentationState state,
                NavMeshPresentationBuffer buffer,
                NavMeshPresentationSystem system)
            {
                Engine = engine;
                Store = store;
                State = state;
                Buffer = buffer;
                System = system;
            }

            public GameEngine Engine { get; }
            public NavTileStore Store { get; }
            public NavMeshPresentationState State { get; }
            public NavMeshPresentationBuffer Buffer { get; }
            public NavMeshPresentationSystem System { get; }
        }
    }
}
