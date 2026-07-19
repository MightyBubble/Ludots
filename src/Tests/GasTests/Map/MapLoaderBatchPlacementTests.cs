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
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace GasTests
{
    [TestFixture]
    public sealed class MapLoaderBatchPlacementTests
    {
        private const string TemplateId = "test.map.batch.unit";
        private const string TemplateName = "Template:MapBatchUnit";
        private const string MapId = "map_batch_placement";

        [TestCase(null, true, false, 0f)]
        [TestCase("position", true, false, 0f)]
        [TestCase("facing", true, true, 1.25f)]
        [TestCase("position-facing", true, true, 2.5f)]
        [TestCase("position-facing-performer-param", true, true, 2.5f)]
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
            That(GetRequestParamOverrideCount(request), Is.EqualTo(
                overrideShape == "position-facing-performer-param" ? 1 : 0));
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
        public void TryBuildBatchRequest_PerformerParamOverride_RequiresExplicitLane()
        {
            var spawn = CreateSpawn(CreateOverrides("position-facing"));
            spawn.PerformerParamOverrides.Add(new ParamOverrideData
            {
                ParamKey = "test.map.batch.slope",
                FloatValue = 0.5f,
            });

            TargetInvocationException ex = Throws<TargetInvocationException>(() => InvokeTryBuildBatchRequest(spawn, out _))!;
            That(ex.InnerException, Is.TypeOf<InvalidOperationException>());
            That(ex.InnerException!.Message, Does.Contain("Lane requires an explicit param lane"));
        }

        [Test]
        public void TryBuildBatchRequest_VectorPerformerParamOverride_RequiresExactlyFourValues()
        {
            var spawn = CreateSpawn(CreateOverrides("position-facing"));
            spawn.PerformerParamOverrides.Add(new ParamOverrideData
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
        public void LoadEntities_PerformerParamOverride_RequiresPresentationRuntime()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawnWithPerformerParam(CreateOverrides("position-facing"), 0.25f));

            InvalidOperationException ex = Throws<InvalidOperationException>(() => loader.LoadEntities(map))!;
            That(ex.Message, Does.Contain("presentation runtime is not installed"));
        }

        [Test]
        public void LoadEntities_PerformerParamOverride_RequiresDirectPerformerBootstrap()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var definitions = new PerformerDefinitionRegistry();
            loader.SetPresentationRuntime(
                new PresentationStableIdAllocator(),
                new PerformerEntityRuntime(world),
                definitions,
                new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 4),
                new WorldSizeSpec(new Ludots.Core.Mathematics.WorldAabbCm(-10_000, -10_000, 20_000, 20_000), 100));
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawnWithPerformerParam(CreateOverrides("position-facing"), 0.25f));

            InvalidOperationException ex = Throws<InvalidOperationException>(() => loader.LoadEntities(map))!;
            That(ex.Message, Does.Contain("has no direct performer bootstrap"));
        }

        [Test]
        public void LoadEntities_PerformerParamOverride_RequiresBatchPath()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawnWithPerformerParam(CreateOverrides("position-facing-health"), 0.25f));

            InvalidOperationException ex = Throws<InvalidOperationException>(() => loader.LoadEntities(map))!;
            That(ex.Message, Does.Contain("not compatible with the map template batch path"));
        }

        [Test]
        public void LoadEntities_BatchTemplate_AppliesPerInstancePerformerParamOverrides()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var performerRuntime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int slopeParamKey = PerformerParamKeyRegistry.Register("test.map.batch.slope");
            int templateKeyId = loader.EntityTemplateKeys.GetId(TemplateId);
            int rootDefinitionId = definitions.GetOrRegisterId("test.map.batch.root");
            definitions.Register("test.map.batch.root", new PerformerDefinition
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
                    new PerformerRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.EntitySpawned,
                            KeyId = templateKeyId,
                        },
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = rootDefinitionId,
                            ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                            AnchorKind = PresentationAnchorKind.Entity,
                        },
                    },
                ],
            });
            performerRuntime.BindDefinitions(definitions);
            loader.SetPresentationRuntime(
                new PresentationStableIdAllocator(),
                performerRuntime,
                definitions,
                new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 4),
                new WorldSizeSpec(new Ludots.Core.Mathematics.WorldAabbCm(-10_000, -10_000, 20_000, 20_000), 100));

            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawnWithPerformerParam(
                CreateOverrides("position-facing"),
                -0.375f));
            map.Entities.Add(CreateSpawnWithPerformerParam(
                new Dictionary<string, JsonNode>
                {
                    ["WorldPositionCm"] = WorldPosition(1200, -800),
                    ["FacingDirection"] = Facing(-1.25f),
                },
                0.875f));

            loader.LoadEntities(map);

            var owners = FindTemplateEntities(world);
            That(owners.Count, Is.EqualTo(2));
            Entity rootA = world.Get<PresentationOwnerHasPerformerPayload>(owners[0]).SingleRootPerformer;
            Entity rootB = world.Get<PresentationOwnerHasPerformerPayload>(owners[1]).SingleRootPerformer;
            That(world.IsAlive(rootA), Is.True);
            That(world.IsAlive(rootB), Is.True);
            That(world.Get<PerformerState>(rootA).DefId, Is.EqualTo(rootDefinitionId));
            That(world.Get<PerformerState>(rootB).DefId, Is.EqualTo(rootDefinitionId));
            That(performerRuntime.TryResolveFloat(rootA, slopeParamKey, out float slopeA), Is.True);
            That(performerRuntime.TryResolveFloat(rootB, slopeParamKey, out float slopeB), Is.True);
            That(slopeA, Is.EqualTo(-0.375f).Within(0.0001f));
            That(slopeB, Is.EqualTo(0.875f).Within(0.0001f));
        }

        [Test]
        public void LoadEntities_DynamicHeightBatch_BootstrapsPerformerFromMapAuthoredPlacement()
        {
            const int authoredXCm = 125_000;
            const int authoredYCm = -87_000;
            const float authoredFacing = 1.75f;
            const float slopeOverride = 0.625f;

            using var world = World.Create();
            var loader = CreateLoader(world, includeDynamicHeightSampling: true);
            var performerRuntime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int slopeParamKey = PerformerParamKeyRegistry.Register("test.map.batch.slope");
            int templateKeyId = loader.EntityTemplateKeys.GetId(TemplateId);
            int rootDefinitionId = definitions.GetOrRegisterId("test.map.batch.dynamic.height.root");
            definitions.Register("test.map.batch.dynamic.height.root", new PerformerDefinition
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
                    new PerformerRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.EntitySpawned,
                            KeyId = templateKeyId,
                        },
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = rootDefinitionId,
                            ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                            AnchorKind = PresentationAnchorKind.Entity,
                        },
                    },
                ],
            });
            performerRuntime.BindDefinitions(definitions);
            loader.SetPresentationRuntime(
                new PresentationStableIdAllocator(),
                performerRuntime,
                definitions,
                new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 4),
                new WorldSizeSpec(new Ludots.Core.Mathematics.WorldAabbCm(-200_000, -200_000, 400_000, 400_000), 100));

            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawnWithPerformerParam(
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
            That(world.Has<VisualHeightmapSampleState>(owner), Is.True);
            That(world.Has<PresentationStaticTransform>(owner), Is.False);
            AssertPlacement(world, owner, authoredXCm, authoredYCm, authoredFacing);

            Vector3 ownerVisual = world.Get<VisualTransform>(owner).Position;
            That(ownerVisual.X, Is.EqualTo(authoredXCm * 0.01f).Within(0.0001f));
            That(ownerVisual.Z, Is.EqualTo(authoredYCm * 0.01f).Within(0.0001f));

            Entity root = world.Get<PresentationOwnerHasPerformerPayload>(owner).SingleRootPerformer;
            That(world.IsAlive(root), Is.True);
            Vector3 rootPosition = world.Get<PerformerWorldPosition>(root).Value;
            That(rootPosition.X, Is.EqualTo(authoredXCm * 0.01f).Within(0.0001f));
            That(rootPosition.Z, Is.EqualTo(authoredYCm * 0.01f).Within(0.0001f));
            That(
                world.Get<PerformerWorldPlanePosition>(root).ValueCm,
                Is.EqualTo(new Vector2(authoredXCm, authoredYCm)));
            That(performerRuntime.TryResolveFloat(root, slopeParamKey, out float slope), Is.True);
            That(slope, Is.EqualTo(slopeOverride).Within(0.0001f));
        }

        [Test]
        public void LoadEntities_DirectBootstrapBatch_PreseededOwnerPayloadDrivesRootTransformSync()
        {
            using var world = World.Create();
            var loader = CreateLoader(world);
            var performerRuntime = new PerformerEntityRuntime(world);
            var definitions = new PerformerDefinitionRegistry();
            int templateKeyId = loader.EntityTemplateKeys.GetId(TemplateId);
            int rootDefinitionId = definitions.GetOrRegisterId("test.map.batch.ownerpayload.root");
            definitions.Register("test.map.batch.ownerpayload.root", new PerformerDefinition
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
                    new PerformerRule
                    {
                        Event = new EventFilter
                        {
                            Kind = PresentationEventKind.EntitySpawned,
                            KeyId = templateKeyId,
                        },
                        Command = new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = rootDefinitionId,
                            ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                            AnchorKind = PresentationAnchorKind.Entity,
                        },
                    },
                ],
            });
            performerRuntime.BindDefinitions(definitions);
            loader.SetPresentationRuntime(
                new PresentationStableIdAllocator(),
                performerRuntime,
                definitions,
                new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 4),
                new WorldSizeSpec(new Ludots.Core.Mathematics.WorldAabbCm(-10_000, -10_000, 20_000, 20_000), 100));

            var map = new MapConfig { Id = MapId };
            map.Entities.Add(CreateSpawn(null));
            map.Entities.Add(CreateSpawn(new Dictionary<string, JsonNode>
            {
                ["WorldPositionCm"] = WorldPosition(1200, -800),
            }));

            loader.LoadEntities(map);

            That(
                performerRuntime.LastRootBatchOwnerPayloadCount,
                Is.EqualTo(2),
                "MapLoader direct bootstrap batches must preseed owner payload markers so the performer root batch writes transform-sync payloads in bulk.");
            var owners = FindTemplateEntities(world);
            That(owners.Count, Is.EqualTo(2));
            Entity owner = owners[0];
            That(world.Has<PresentationOwnerHasPerformerPayload>(owner), Is.True);
            ref readonly PresentationOwnerHasPerformerPayload payload = ref world.Get<PresentationOwnerHasPerformerPayload>(owner);
            That(payload.RootCount, Is.EqualTo(1));
            That(payload.SingleRootTransformSync, Is.EqualTo(1));
            Entity root = payload.SingleRootPerformer;
            That(world.IsAlive(root), Is.True);
            That(world.Has<PerfOwnerPayloadTransformSync>(root), Is.True);

            var movedWorld = Fix64Vec2.FromInt(1800, -600);
            world.Get<WorldPositionCm>(owner).Value = movedWorld;
            world.Get<VisualTransform>(owner).Position = new Vector3(18f, 0f, -6f);

            using var transformSync = new PerformerEntityTransformSyncSystem(world, performerRuntime, definitions);
            transformSync.Update(0.016f);

            That(world.Get<PerformerWorldPosition>(root).Value, Is.EqualTo(new Vector3(18f, 0f, -6f)));
            That(world.Get<PerformerWorldPlanePosition>(root).ValueCm, Is.EqualTo(new Vector2(1800f, -600f)));
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

        private static MapLoader CreateLoader(World world, bool includeDynamicHeightSampling = false)
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_MapLoaderBatchPlacementTests", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "Entities"));
                File.WriteAllText(
                    Path.Combine(root, "Configs", "config_catalog.json"),
                    @"[{ ""Path"": ""Entities/templates.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
                string dynamicHeightComponent = includeDynamicHeightSampling
                    ? """
                          "VisualHeightmapSampleState": {},
                    """
                    : string.Empty;
                File.WriteAllText(
                    Path.Combine(root, "Configs", "Entities", "templates.json"),
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
                      }
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

        private static EntitySpawnData CreateSpawnWithPerformerParam(
            Dictionary<string, JsonNode>? overrides,
            float value = 0.25f)
        {
            var spawn = CreateSpawn(overrides);
            spawn.PerformerParamOverrides.Add(new ParamOverrideData
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
                "position-facing-performer-param" => new Dictionary<string, JsonNode>
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
            return shape == "position-facing-performer-param"
                ? CreateSpawnWithPerformerParam(CreateOverrides(shape))
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
                new MapEntity { MapId = new MapId(MapId) },
                null!,
            };
            bool result = (bool)method.Invoke(null, args)!;
            request = args[3];
            return result;
        }

        private static bool GetRequestBool(object request, string propertyName)
        {
            PropertyInfo property = request.GetType().GetProperty(propertyName)!;
            That(property, Is.Not.Null);
            return (bool)property.GetValue(request)!;
        }

        private static float GetRequestFloat(object request, string propertyName)
        {
            PropertyInfo property = request.GetType().GetProperty(propertyName)!;
            That(property, Is.Not.Null);
            return (float)property.GetValue(request)!;
        }

        private static int GetRequestParamOverrideCount(object request)
        {
            PropertyInfo property = request.GetType().GetProperty("PerformerParamOverrides")!;
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
