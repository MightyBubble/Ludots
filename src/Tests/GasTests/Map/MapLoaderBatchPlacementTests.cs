using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;
using Ludots.Platform.Abstractions;

namespace GasTests
{
    [TestFixture]
    public sealed class MapLoaderBatchPlacementTests
    {
        private const string TemplateId = "test.map.batch.unit";
        private const string TemplateName = "Template:MapBatchUnit";
        private const string HeroTemplateId = "test.map.batch.hero";
        private const string HeroTemplateName = "Template:MapBatchHero";
        private const string VitalityAttribute = "MapBatch.Vitality";
        private const string PaceAttribute = "MapBatch.Pace";
        private const string MapId = "map_batch_placement";

        [TestCase(null, true, false, 0f)]
        [TestCase("position", true, false, 0f)]
        [TestCase("facing", true, true, 1.25f)]
        [TestCase("position-facing", true, true, 2.5f)]
        [TestCase("position-name", true, false, 0f)]
        [TestCase("position-facing-presenter-param", true, true, 2.5f)]
        [TestCase("position-team", false, false, 0f)]
        [TestCase("position-facing-health", false, false, 0f)]
        public void TryBuildBatchRequest_ClassifiesPlacementOverrides(
            string? overrideShape,
            bool expectedFastPath,
            bool expectedHasFacing,
            float expectedFacing)
        {
            var spawn = CreateSpawnForShape(overrideShape);
            bool fastPath = InvokeTryBuildBatchRequest(spawn, out object request);

            That(fastPath, Is.EqualTo(expectedFastPath));
            if (!expectedFastPath)
            {
                return;
            }

            That(GetRequestBool(request, "HasFacing"), Is.EqualTo(expectedHasFacing));
            That(GetRequestBool(request, "HasNameOverride"), Is.EqualTo(overrideShape == "position-name"));
            That(GetRequestParamOverrideCount(request), Is.EqualTo(
                overrideShape == "position-facing-presenter-param" ? 1 : 0));
            if (overrideShape == "position-name")
            {
                That(GetRequestName(request), Is.EqualTo("刘备"));
            }
            if (expectedHasFacing)
            {
                That(GetRequestFloat(request, "FacingAngleRad"), Is.EqualTo(expectedFacing).Within(0.0001f));
            }
        }

        [Test]
        public void LoadEntities_BatchTemplate_AcceptsSupportedPlacementOverrides()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawn(null));
            map.Entities.Add(CreateSpawn(new Dictionary<string, JsonNode>
            {
                ["WorldPositionCm"] = WorldPosition(1000, 2000),
            }));
            map.Entities.Add(CreateSpawn(new Dictionary<string, JsonNode>
            {
                ["FacingDirection"] = Facing(1.25f),
            }));
            map.Entities.Add(CreateSpawn(new Dictionary<string, JsonNode>
            {
                ["WorldPositionCm"] = WorldPosition(-300, 400),
                ["FacingDirection"] = Facing(2.5f),
            }));

            loader.LoadEntities(map);

            var entities = FindTemplateEntities(world);
            That(entities.Count, Is.EqualTo(4));
            AssertPlacement(world, entities[0], 10, 20, 0.5f);
            AssertPlacement(world, entities[1], 1000, 2000, 0.5f);
            AssertPlacement(world, entities[2], 10, 20, 1.25f);
            AssertPlacement(world, entities[3], -300, 400, 2.5f);
        }

        [Test]
        public void LoadEntities_UnsupportedExtraOverride_StaysOnComponentApplicationPath()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawn(new Dictionary<string, JsonNode>
            {
                ["WorldPositionCm"] = WorldPosition(777, 888),
                ["FacingDirection"] = Facing(3.25f),
                ["Health"] = JsonNode.Parse(@"{ ""Current"": 7, ""Max"": 11 }")!,
            }));

            loader.LoadEntities(map);

            var entities = FindTemplateEntities(world);
            That(entities.Count, Is.EqualTo(1));
            Entity entity = entities[0];
            AssertPlacement(world, entity, 777, 888, 3.25f);
            That(world.Has<Health>(entity), Is.True);
            ref readonly Health health = ref world.Get<Health>(entity);
            That(health.Current, Is.EqualTo(7));
            That(health.Max, Is.EqualTo(11));
        }

        [Test]
        public void LoadEntities_EmptyNameOverride_FailsOnBatchPath()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawn(new Dictionary<string, JsonNode>
            {
                ["Name"] = JsonNode.Parse(@"{ ""Value"": "" "" }")!,
            }));

            InvalidOperationException ex = Throws<InvalidOperationException>(() => loader.LoadEntities(map))!;
            That(ex.Message, Does.Contain("Name.Value requires a non-empty string value"));
        }

        [Test]
        public void LoadEntities_TeamOverrideWithoutTemplateComponent_LeavesBatchPath()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawnWithPresenterParam(new Dictionary<string, JsonNode>
            {
                ["WorldPositionCm"] = WorldPosition(10, 20),
                ["Team"] = JsonNode.Parse(@"{ ""Id"": 2 }")!,
            }));

            InvalidOperationException ex = Throws<InvalidOperationException>(() => loader.LoadEntities(map))!;
            That(ex.Message, Does.Contain("not compatible with the map template batch path"));
        }

        [Test]
        public void LoadEntities_BatchTemplate_AppliesPerInstanceIdentityAndAttributeOverrides()
        {
            using var world = World.Create();
            var loader = CreateLoader(world, includeIdentityTemplate: true);
            var presenterRuntime = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int slopeParamKey = PresenterParamKeyRegistry.Register("test.map.batch.slope");
            int templateKeyId = loader.EntityTemplateKeys.GetId(HeroTemplateId);
            int rootDefinitionId = definitions.GetOrRegisterId("test.map.batch.hero.root");
            definitions.Register("test.map.batch.hero.root", new PresenterDefinition
            {
                ParamDefaults =
                [
                    new ParamDefault
                    {
                        ParamKey = slopeParamKey,
                        Lane = ParamLane.Float,
                        FloatValue = 0f,
                    },
                ],
                Rules =
                [
                    new PresenterRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.EntitySpawned,
                            KeyId = templateKeyId,
                        },
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = rootDefinitionId,
                            ScopeSource = PresenterCommandScopeSource.EventPayloadA,
                            AnchorKind = PresentationAnchorKind.Entity,
                        },
                    },
                ],
            });
            presenterRuntime.BindDefinitions(definitions);
            loader.SetPresentationRuntime(
                new PresentationStableIdAllocator(),
                presenterRuntime,
                definitions,
                new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 4),
                new WorldSizeSpec(new Ludots.Platform.Abstractions.WorldAabbCm(-10_000, -10_000, 20_000, 20_000), 100));

            int vitalityId = AttributeRegistry.RequireId(VitalityAttribute);
            int paceId = AttributeRegistry.RequireId(PaceAttribute);
            That(vitalityId, Is.LessThan(AttributeBuffer.MAX_ATTRS));
            That(paceId, Is.LessThan(AttributeBuffer.MAX_ATTRS));

            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateHeroSpawn(
                "刘备",
                teamId: 1,
                playerId: 1,
                attributes: JsonNode.Parse(@$"{{ ""base"": {{ ""{VitalityAttribute}"": 180 }} }}")!,
                x: 100,
                y: 200,
                facing: 0.2f,
                slope: 0.1f));
            map.Entities.Add(CreateHeroSpawn(
                "关羽",
                teamId: 2,
                playerId: 2,
                attributes: null,
                x: 300,
                y: 400,
                facing: 0.4f,
                slope: 0.2f));
            map.Entities.Add(CreateHeroSpawn(
                "张飞",
                teamId: null,
                playerId: null,
                attributes: JsonNode.Parse(@$"{{ ""current"": {{ ""{PaceAttribute}"": 8 }} }}")!,
                x: 500,
                y: 600,
                facing: 0.6f,
                slope: 0.3f));
            map.Entities.Add(CreateHeroSpawn(
                "赵云",
                teamId: 4,
                playerId: 4,
                attributes: JsonNode.Parse(@$"{{ ""__replace"": true, ""base"": {{ ""{VitalityAttribute}"": 1 }} }}")!,
                x: 700,
                y: 800,
                facing: 0.8f,
                slope: 0.4f));

            loader.LoadEntities(map);

            var owners = FindNamedEntities(world, "刘备", "关羽", "张飞", "赵云");
            That(owners.Count, Is.EqualTo(4));
            AssertHero(world, owners[0], "刘备", 1, 1, 100, 200, 0.2f, vitalityId, 180f, 180f, paceId, 5f, 5f);
            AssertHero(world, owners[1], "关羽", 2, 2, 300, 400, 0.4f, vitalityId, 100f, 100f, paceId, 5f, 5f);
            AssertHero(world, owners[2], "张飞", 1, 9, 500, 600, 0.6f, vitalityId, 100f, 100f, paceId, 5f, 8f);
            AssertHero(world, owners[3], "赵云", 4, 4, 700, 800, 0.8f, vitalityId, 1f, 1f, paceId, hasPace: false);

            Entity rootLiu = world.Get<PresentationOwnerHasPresenterPayload>(owners[0]).SingleRootPresenter;
            Entity rootZhao = world.Get<PresentationOwnerHasPresenterPayload>(owners[3]).SingleRootPresenter;
            That(presenterRuntime.TryResolveFloat(rootLiu, slopeParamKey, out float slopeLiu), Is.True);
            That(presenterRuntime.TryResolveFloat(rootZhao, slopeParamKey, out float slopeZhao), Is.True);
            That(slopeLiu, Is.EqualTo(0.1f).Within(0.0001f));
            That(slopeZhao, Is.EqualTo(0.4f).Within(0.0001f));
        }

        [Test]
        public void TryBuildBatchRequest_PresenterParamOverride_RequiresExplicitLane()
        {
            var spawn = CreateSpawn(CreateOverrides("position-facing"));
            spawn.PresenterParamOverrides.Add(new ParamOverrideData
            {
                ParamKey = "test.map.batch.slope",
                FloatValue = 0.5f,
            });

            TargetInvocationException ex = Throws<TargetInvocationException>(() => InvokeTryBuildBatchRequest(spawn, out _))!;
            That(ex.InnerException, Is.TypeOf<InvalidOperationException>());
            That(ex.InnerException!.Message, Does.Contain("Lane requires an explicit param lane"));
        }

        [Test]
        public void TryBuildBatchRequest_VectorPresenterParamOverride_RequiresExactlyFourValues()
        {
            var spawn = CreateSpawn(CreateOverrides("position-facing"));
            spawn.PresenterParamOverrides.Add(new ParamOverrideData
            {
                ParamKey = "test.map.batch.vector",
                Lane = ParamLane.Vector,
                VectorValue = [1f, 2f, 3f],
            });

            TargetInvocationException ex = Throws<TargetInvocationException>(() => InvokeTryBuildBatchRequest(spawn, out _))!;
            That(ex.InnerException, Is.TypeOf<InvalidOperationException>());
            That(ex.InnerException!.Message, Does.Contain("VectorValue requires four numeric values"));
        }

        [Test]
        public void LoadEntities_PresenterParamOverride_RequiresPresentationRuntime()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawnWithPresenterParam(CreateOverrides("position-facing"), 0.25f));

            InvalidOperationException ex = Throws<InvalidOperationException>(() => loader.LoadEntities(map))!;
            That(ex.Message, Does.Contain("presentation runtime is not installed"));
        }

        [Test]
        public void LoadEntities_PresenterParamOverride_RequiresDirectPresenterBootstrap()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var definitions = new PresenterDefinitionRegistry();
            loader.SetPresentationRuntime(
                new PresentationStableIdAllocator(),
                new PresenterEntityRuntime(world),
                definitions,
                new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 4),
                new WorldSizeSpec(new Ludots.Platform.Abstractions.WorldAabbCm(-10_000, -10_000, 20_000, 20_000), 100));
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawnWithPresenterParam(CreateOverrides("position-facing"), 0.25f));

            InvalidOperationException ex = Throws<InvalidOperationException>(() => loader.LoadEntities(map))!;
            That(ex.Message, Does.Contain("has no direct presenter bootstrap"));
        }

        [Test]
        public void LoadEntities_PresenterParamOverride_RequiresBatchPath()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawnWithPresenterParam(CreateOverrides("position-facing-health"), 0.25f));

            InvalidOperationException ex = Throws<InvalidOperationException>(() => loader.LoadEntities(map))!;
            That(ex.Message, Does.Contain("not compatible with the map template batch path"));
        }

        [Test]
        public void LoadEntities_BatchTemplate_AppliesPerInstancePresenterParamOverrides()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var presenterRuntime = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int slopeParamKey = PresenterParamKeyRegistry.Register("test.map.batch.slope");
            int templateKeyId = loader.EntityTemplateKeys.GetId(TemplateId);
            int rootDefinitionId = definitions.GetOrRegisterId("test.map.batch.root");
            definitions.Register("test.map.batch.root", new PresenterDefinition
            {
                ParamDefaults =
                [
                    new ParamDefault
                    {
                        ParamKey = slopeParamKey,
                        Lane = ParamLane.Float,
                        FloatValue = 0f,
                    },
                ],
                Rules =
                [
                    new PresenterRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.EntitySpawned,
                            KeyId = templateKeyId,
                        },
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = rootDefinitionId,
                            ScopeSource = PresenterCommandScopeSource.EventPayloadA,
                            AnchorKind = PresentationAnchorKind.Entity,
                        },
                    },
                ],
            });
            presenterRuntime.BindDefinitions(definitions);
            loader.SetPresentationRuntime(
                new PresentationStableIdAllocator(),
                presenterRuntime,
                definitions,
                new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 4),
                new WorldSizeSpec(new Ludots.Platform.Abstractions.WorldAabbCm(-10_000, -10_000, 20_000, 20_000), 100));

            var map = new MapConfig { Id = MapId };
            EntitySpawnData first = CreateSpawnWithPresenterParam(CreateOverrides("position-facing"), -0.375f);
            first.InstanceId = "batch.liu";
            map.Entities.Add(first);
            map.Entities.Add(CreateSpawnWithPresenterParam(
                new Dictionary<string, JsonNode>
                {
                    ["WorldPositionCm"] = WorldPosition(1200, -800),
                    ["FacingDirection"] = Facing(-1.25f),
                },
                0.875f));

            loader.LoadEntities(map);

            var owners = FindTemplateEntities(world);
            That(owners.Count, Is.EqualTo(2));
            Entity rootA = world.Get<PresentationOwnerHasPresenterPayload>(owners[0]).SingleRootPresenter;
            Entity rootB = world.Get<PresentationOwnerHasPresenterPayload>(owners[1]).SingleRootPresenter;
            That(world.IsAlive(rootA), Is.True);
            That(world.IsAlive(rootB), Is.True);
            That(world.Get<PresenterState>(rootA).DefId, Is.EqualTo(rootDefinitionId));
            That(world.Get<PresenterState>(rootB).DefId, Is.EqualTo(rootDefinitionId));
            That(presenterRuntime.TryResolveFloat(rootA, slopeParamKey, out float slopeA), Is.True);
            That(presenterRuntime.TryResolveFloat(rootB, slopeParamKey, out float slopeB), Is.True);
            That(slopeA, Is.EqualTo(-0.375f).Within(0.0001f));
            That(slopeB, Is.EqualTo(0.875f).Within(0.0001f));
            That(world.Get<Name>(owners[0]).Value, Is.EqualTo(TemplateName));
            That(world.Get<PlacedInstanceId>(owners[0]).Value, Is.EqualTo("batch.liu"));
            That(world.Get<Name>(owners[1]).Value, Is.EqualTo(TemplateName));
            That(world.Has<PlacedInstanceId>(owners[1]), Is.True);
            That(world.Get<PlacedInstanceId>(owners[1]).Value, Is.Null);
        }

        [Test]
        public void LoadEntities_DynamicHeightBatch_BootstrapsPresenterFromMapAuthoredPlacement()
        {
            const int authoredXCm = 125_000;
            const int authoredYCm = -87_000;
            const float authoredFacing = 1.75f;
            const float slopeOverride = 0.625f;

            using var world = World.Create();
            var loader = CreateLoader(world, includeDynamicHeightSampling: true);
            var presenterRuntime = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int slopeParamKey = PresenterParamKeyRegistry.Register("test.map.batch.slope");
            int templateKeyId = loader.EntityTemplateKeys.GetId(TemplateId);
            int rootDefinitionId = definitions.GetOrRegisterId("test.map.batch.dynamic.height.root");
            definitions.Register("test.map.batch.dynamic.height.root", new PresenterDefinition
            {
                ParamDefaults =
                [
                    new ParamDefault
                    {
                        ParamKey = slopeParamKey,
                        Lane = ParamLane.Float,
                        FloatValue = 0f,
                    },
                ],
                Rules =
                [
                    new PresenterRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.EntitySpawned,
                            KeyId = templateKeyId,
                        },
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = rootDefinitionId,
                            ScopeSource = PresenterCommandScopeSource.EventPayloadA,
                            AnchorKind = PresentationAnchorKind.Entity,
                        },
                    },
                ],
            });
            presenterRuntime.BindDefinitions(definitions);
            loader.SetPresentationRuntime(
                new PresentationStableIdAllocator(),
                presenterRuntime,
                definitions,
                new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 4),
                new WorldSizeSpec(new Ludots.Platform.Abstractions.WorldAabbCm(-200_000, -200_000, 400_000, 400_000), 100));

            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawnWithPresenterParam(
                new Dictionary<string, JsonNode>
                {
                    ["WorldPositionCm"] = WorldPosition(authoredXCm, authoredYCm),
                    ["FacingDirection"] = Facing(authoredFacing),
                },
                slopeOverride));

            loader.LoadEntities(map);

            var owners = FindTemplateEntities(world);
            That(owners.Count, Is.EqualTo(1));
            Entity owner = owners[0];
            That(world.Has<ContinuousHeightmapSampleState>(owner), Is.True);
            That(world.Has<PresentationStaticTransform>(owner), Is.False);
            AssertPlacement(world, owner, authoredXCm, authoredYCm, authoredFacing);

            Vector3 ownerVisual = world.Get<VisualTransform>(owner).Position;
            That(ownerVisual.X, Is.EqualTo(authoredXCm * 0.01f).Within(0.0001f));
            That(ownerVisual.Z, Is.EqualTo(authoredYCm * 0.01f).Within(0.0001f));

            Entity root = world.Get<PresentationOwnerHasPresenterPayload>(owner).SingleRootPresenter;
            That(world.IsAlive(root), Is.True);
            Vector3 rootPosition = world.Get<PresenterWorldPosition>(root).Value;
            That(rootPosition.X, Is.EqualTo(authoredXCm * 0.01f).Within(0.0001f));
            That(rootPosition.Z, Is.EqualTo(authoredYCm * 0.01f).Within(0.0001f));
            That(
                world.Get<PresenterWorldPlanePosition>(root).ValueCm,
                Is.EqualTo(new Vector2(authoredXCm, authoredYCm)));
            That(presenterRuntime.TryResolveFloat(root, slopeParamKey, out float slope), Is.True);
            That(slope, Is.EqualTo(slopeOverride).Within(0.0001f));
        }

        [Test]
        public void LoadEntities_DirectBootstrapBatch_PreseededOwnerPayloadDrivesRootTransformSync()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var presenterRuntime = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int templateKeyId = loader.EntityTemplateKeys.GetId(TemplateId);
            int rootDefinitionId = definitions.GetOrRegisterId("test.map.batch.ownerpayload.root");
            definitions.Register("test.map.batch.ownerpayload.root", new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 1,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            AssetIdParamKey = -1,
                        },
                    },
                ],
                Rules =
                [
                    new PresenterRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.EntitySpawned,
                            KeyId = templateKeyId,
                        },
                        Command = new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = rootDefinitionId,
                            ScopeSource = PresenterCommandScopeSource.EventPayloadA,
                            AnchorKind = PresentationAnchorKind.Entity,
                        },
                    },
                ],
            });
            presenterRuntime.BindDefinitions(definitions);
            loader.SetPresentationRuntime(
                new PresentationStableIdAllocator(),
                presenterRuntime,
                definitions,
                new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 4),
                new WorldSizeSpec(new Ludots.Platform.Abstractions.WorldAabbCm(-10_000, -10_000, 20_000, 20_000), 100));

            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawn(null));
            map.Entities.Add(CreateSpawn(new Dictionary<string, JsonNode>
            {
                ["WorldPositionCm"] = WorldPosition(1200, -800),
            }));

            loader.LoadEntities(map);

            That(
                presenterRuntime.LastRootBatchOwnerPayloadCount,
                Is.EqualTo(2),
                "MapLoader direct bootstrap batches must preseed owner payload markers so the presenter root batch writes transform-sync payloads in bulk.");
            var owners = FindTemplateEntities(world);
            That(owners.Count, Is.EqualTo(2));
            Entity owner = owners[0];
            That(world.Has<PresentationOwnerHasPresenterPayload>(owner), Is.True);
            ref readonly PresentationOwnerHasPresenterPayload payload = ref world.Get<PresentationOwnerHasPresenterPayload>(owner);
            That(payload.RootCount, Is.EqualTo(1));
            That(payload.SingleRootTransformSync, Is.EqualTo(1));
            Entity root = payload.SingleRootPresenter;
            That(world.IsAlive(root), Is.True);
            That(world.Has<PerfOwnerPayloadTransformSync>(root), Is.True);

            var movedWorld = Fix64Vec2.FromInt(1800, -600);
            world.Get<WorldPositionCm>(owner).Value = movedWorld;
            world.Get<VisualTransform>(owner).Position = new Vector3(18f, 0f, -6f);

            using var transformSync = new PresenterEntityTransformSyncSystem(world, presenterRuntime, definitions);
            transformSync.Update(0.016f);

            That(world.Get<PresenterWorldPosition>(root).Value, Is.EqualTo(new Vector3(18f, 0f, -6f)));
            That(world.Get<PresenterWorldPlanePosition>(root).ValueCm, Is.EqualTo(new Vector2(1800f, -600f)));
        }

        [Test]
        public void LoadEntities_FacingOverride_UsesTemplateFacingAuthoringValidation()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawn(new Dictionary<string, JsonNode>
            {
                ["FacingDirection"] = JsonNode.Parse(@"{ ""AngleRad"": ""east"" }")!,
            }));

            InvalidOperationException ex = Throws<InvalidOperationException>(() => loader.LoadEntities(map))!;
            That(ex.Message, Does.Contain("FacingDirection.AngleRad requires a numeric value"));
        }

        private static MapLoader CreateLoader(
            World world,
            bool includeDynamicHeightSampling = false,
            bool includeIdentityTemplate = false)
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_MapLoaderBatchPlacementTests", Guid.NewGuid().ToString("N"));
            try
            {
                if (includeIdentityTemplate)
                {
                    AttributeRegistry.Register(VitalityAttribute);
                    AttributeRegistry.Register(PaceAttribute);
                }

                Directory.CreateDirectory(Path.Combine(root, "Entities"));
                File.WriteAllText(
                    Path.Combine(root, "config_catalog.json"),
                    @"[{ ""Path"": ""Entities/templates.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
                string dynamicHeightComponent = includeDynamicHeightSampling
                    ? """
                          "ContinuousHeightmapSampleState": {},
                    """
                    : string.Empty;
                string identityTemplate = includeIdentityTemplate
                    ? $$"""
                      ,
                      {
                        "id": "{{HeroTemplateId}}",
                        "components": {
                          "Name": { "Value": "{{HeroTemplateName}}" },
                          "WorldPositionCm": { "Value": { "X": 10, "Y": 20 } },
                          "FacingDirection": { "AngleRad": 0.5 },
                          "Team": { "Id": 1 },
                          "PlayerOwner": { "PlayerId": 9 },
                          "AttributeBuffer": {
                            "base": { "{{VitalityAttribute}}": 100, "{{PaceAttribute}}": 5 }
                          },
                          "GameplayTagContainer": {},
                          "TagCountContainer": {}
                        }
                      }
                    """
                    : string.Empty;
                File.WriteAllText(
                    Path.Combine(root, "Entities", "templates.json"),
                    $$"""
                    [
                      {
                        "id": "{{TemplateId}}",
                        "components": {
                          "Name": { "Value": "{{TemplateName}}" },
                          "WorldPositionCm": { "Value": { "X": 10, "Y": 20 } },
                          "FacingDirection": { "AngleRad": 0.5 },
                          {{dynamicHeightComponent}}
                          "AttributeBuffer": { "base": {} },
                          "GameplayTagContainer": {},
                          "TagCountContainer": {}
                        }
                      }{{identityTemplate}}
                    ]
                    """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
                var loader = new MapLoader(world, new WorldMap(), pipeline);
                loader.LoadTemplates(ConfigCatalogLoader.Load(pipeline));
                return loader;
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static EntitySpawnData CreateSpawn(Dictionary<string, JsonNode>? overrides)
        {
            var spawn = new EntitySpawnData
            {
                Template = TemplateId,
            };
            if (overrides != null)
            {
                spawn.Overrides = overrides;
            }

            return spawn;
        }

        private static EntitySpawnData CreateSpawnWithPresenterParam(
            Dictionary<string, JsonNode>? overrides,
            float value = 0.25f)
        {
            var spawn = CreateSpawn(overrides);
            spawn.PresenterParamOverrides.Add(new ParamOverrideData
            {
                ParamKey = "test.map.batch.slope",
                Lane = ParamLane.Float,
                FloatValue = value,
            });
            return spawn;
        }

        private static Dictionary<string, JsonNode>? CreateOverrides(string? shape)
        {
            return shape switch
            {
                null => null,
                "position" => new Dictionary<string, JsonNode>
                {
                    ["WorldPositionCm"] = WorldPosition(1000, 2000),
                },
                "facing" => new Dictionary<string, JsonNode>
                {
                    ["FacingDirection"] = Facing(1.25f),
                },
                "position-facing" => new Dictionary<string, JsonNode>
                {
                    ["WorldPositionCm"] = WorldPosition(-300, 400),
                    ["FacingDirection"] = Facing(2.5f),
                },
                "position-name" => new Dictionary<string, JsonNode>
                {
                    ["WorldPositionCm"] = WorldPosition(1000, 2000),
                    ["Name"] = JsonNode.Parse(@"{ ""Value"": ""刘备"" }")!,
                },
                "position-team" => new Dictionary<string, JsonNode>
                {
                    ["WorldPositionCm"] = WorldPosition(1000, 2000),
                    ["Team"] = JsonNode.Parse(@"{ ""Id"": 2 }")!,
                },
                "position-facing-presenter-param" => new Dictionary<string, JsonNode>
                {
                    ["WorldPositionCm"] = WorldPosition(-300, 400),
                    ["FacingDirection"] = Facing(2.5f),
                },
                "position-facing-health" => new Dictionary<string, JsonNode>
                {
                    ["WorldPositionCm"] = WorldPosition(777, 888),
                    ["FacingDirection"] = Facing(3.25f),
                    ["Health"] = JsonNode.Parse(@"{ ""Current"": 7, ""Max"": 11 }")!,
                },
                _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
            };
        }

        private static EntitySpawnData CreateSpawnForShape(string? shape)
        {
            return shape == "position-facing-presenter-param"
                ? CreateSpawnWithPresenterParam(CreateOverrides(shape))
                : CreateSpawn(CreateOverrides(shape));
        }

        private static JsonNode WorldPosition(int x, int y)
        {
            return JsonNode.Parse(@$"{{ ""Value"": {{ ""X"": {x}, ""Y"": {y} }} }}")!;
        }

        private static JsonNode Facing(float angleRad)
        {
            return JsonNode.Parse(FormattableString.Invariant(@$"{{ ""AngleRad"": {angleRad} }}"))!;
        }

        private static bool InvokeTryBuildBatchRequest(EntitySpawnData spawn, out object request)
        {
            MethodInfo method = typeof(MapLoader).GetMethod(
                "TryBuildBatchRequest",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            That(method, Is.Not.Null);

            object[] args =
            {
                MapId,
                spawn,
                CreateClassificationTemplate(),
                new MapEntity { MapId = new MapId(MapId) },
                null!,
            };
            bool result = (bool)method.Invoke(null, args)!;
            request = args[4];
            return result;
        }

        private static EntityTemplate CreateClassificationTemplate()
        {
            return new EntityTemplate
            {
                Id = TemplateId,
                Components = new Dictionary<string, JsonNode>
                {
                    ["Name"] = JsonNode.Parse($$"""{ "Value": "{{TemplateName}}" }""")!,
                    ["WorldPositionCm"] = WorldPosition(10, 20),
                    ["FacingDirection"] = Facing(0.5f),
                    ["AttributeBuffer"] = JsonNode.Parse(@"{ ""base"": {} }")!,
                },
            };
        }

        private static EntitySpawnData CreateHeroSpawn(
            string name,
            int? teamId,
            int? playerId,
            JsonNode? attributes,
            int x,
            int y,
            float facing,
            float slope)
        {
            var overrides = new Dictionary<string, JsonNode>
            {
                ["Name"] = JsonNode.Parse($$$"""{ "Value": "{{{name}}}" }""")!,
                ["WorldPositionCm"] = WorldPosition(x, y),
                ["FacingDirection"] = Facing(facing),
            };
            if (teamId.HasValue)
            {
                overrides["Team"] = JsonNode.Parse($$"""{ "Id": {{teamId.Value}} }""")!;
            }

            if (playerId.HasValue)
            {
                overrides["PlayerOwner"] = JsonNode.Parse($$"""{ "PlayerId": {{playerId.Value}} }""")!;
            }

            if (attributes != null)
            {
                overrides["AttributeBuffer"] = attributes;
            }

            var spawn = CreateSpawn(overrides);
            spawn.Template = HeroTemplateId;
            spawn.PresenterParamOverrides.Add(new ParamOverrideData
            {
                ParamKey = "test.map.batch.slope",
                Lane = ParamLane.Float,
                FloatValue = slope,
            });
            return spawn;
        }

        private static bool GetRequestBool(object request, string propertyName)
        {
            PropertyInfo property = request.GetType().GetProperty(propertyName)!;
            That(property, Is.Not.Null);
            return (bool)property.GetValue(request)!;
        }

        private static string GetRequestName(object request)
        {
            PropertyInfo property = request.GetType().GetProperty("NameOverride")!;
            That(property, Is.Not.Null);
            object name = property.GetValue(request)!;
            return (string)name.GetType().GetField("Value")!.GetValue(name)!;
        }

        private static float GetRequestFloat(object request, string propertyName)
        {
            PropertyInfo property = request.GetType().GetProperty(propertyName)!;
            That(property, Is.Not.Null);
            return (float)property.GetValue(request)!;
        }

        private static int GetRequestParamOverrideCount(object request)
        {
            PropertyInfo property = request.GetType().GetProperty("PresenterParamOverrides")!;
            That(property, Is.Not.Null);
            var overrides = (ParamDefault[])property.GetValue(request)!;
            return overrides.Length;
        }

        private static List<Entity> FindTemplateEntities(World world)
        {
            var found = new List<Entity>();
            var query = new QueryDescription().WithAll<Name, MapEntity>();
            world.Query(in query, (Entity entity, ref Name name, ref MapEntity mapEntity) =>
            {
                if (string.Equals(name.Value, TemplateName, StringComparison.Ordinal) &&
                    string.Equals(mapEntity.MapId.Value, MapId, StringComparison.Ordinal))
                {
                    found.Add(entity);
                }
            });

            found.Sort((left, right) => left.Id.CompareTo(right.Id));
            return found;
        }

        private static List<Entity> FindNamedEntities(World world, params string[] names)
        {
            var wanted = new HashSet<string>(names, StringComparer.Ordinal);
            var found = new List<Entity>();
            var query = new QueryDescription().WithAll<Name, MapEntity>();
            world.Query(in query, (Entity entity, ref Name name, ref MapEntity mapEntity) =>
            {
                if (wanted.Contains(name.Value) &&
                    string.Equals(mapEntity.MapId.Value, MapId, StringComparison.Ordinal))
                {
                    found.Add(entity);
                }
            });

            found.Sort((left, right) => left.Id.CompareTo(right.Id));
            return found;
        }

        private static void AssertHero(
            World world,
            Entity entity,
            string expectedName,
            int expectedTeam,
            int expectedPlayer,
            int expectedX,
            int expectedY,
            float expectedFacing,
            int vitalityId,
            float expectedVitalityBase,
            float expectedVitalityCurrent,
            int paceId,
            float expectedPaceBase = 0f,
            float expectedPaceCurrent = 0f,
            bool hasPace = true)
        {
            AssertPlacement(world, entity, expectedX, expectedY, expectedFacing);
            That(world.Get<Name>(entity).Value, Is.EqualTo(expectedName));
            That(world.Has<Team>(entity), Is.True);
            That(world.Has<PlayerOwner>(entity), Is.True);
            That(world.Get<Team>(entity).Id, Is.EqualTo(expectedTeam));
            That(world.Get<PlayerOwner>(entity).PlayerId, Is.EqualTo(expectedPlayer));

            AttributeBuffer attributes = world.Get<AttributeBuffer>(entity);
            That(attributes.HasAttribute(vitalityId), Is.True);
            That(attributes.GetBase(vitalityId), Is.EqualTo(expectedVitalityBase).Within(0.0001f));
            That(attributes.GetCurrent(vitalityId), Is.EqualTo(expectedVitalityCurrent).Within(0.0001f));
            That(attributes.HasAttribute(paceId), Is.EqualTo(hasPace));
            if (hasPace)
            {
                That(attributes.GetBase(paceId), Is.EqualTo(expectedPaceBase).Within(0.0001f));
                That(attributes.GetCurrent(paceId), Is.EqualTo(expectedPaceCurrent).Within(0.0001f));
            }
        }

        private static void AssertPlacement(
            World world,
            Entity entity,
            int expectedX,
            int expectedY,
            float expectedFacing)
        {
            That(world.Has<WorldPositionCm>(entity), Is.True);
            That(world.Has<PreviousWorldPositionCm>(entity), Is.True);
            That(world.Has<VisualTransform>(entity), Is.True);
            That(world.Has<CullState>(entity), Is.True);
            That(world.Has<AttributeBuffer>(entity), Is.True);
            That(world.Has<GameplayTagContainer>(entity), Is.True);
            That(world.Has<TagCountContainer>(entity), Is.True);

            var expectedPosition = Fix64Vec2.FromInt(expectedX, expectedY);
            That(world.Get<WorldPositionCm>(entity).Value, Is.EqualTo(expectedPosition));
            That(world.Get<PreviousWorldPositionCm>(entity).Value, Is.EqualTo(expectedPosition));
            That(world.Get<FacingDirection>(entity).AngleRad, Is.EqualTo(expectedFacing).Within(0.0001f));
        }
    }
}
