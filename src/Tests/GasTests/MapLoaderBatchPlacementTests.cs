using System;
using System.Collections.Generic;
using System.IO;
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
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
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
        [TestCase("position-facing-health", false, false, 0f)]
        public void TryBuildBatchRequest_ClassifiesPlacementOverrides(
            string? overrideShape,
            bool expectedFastPath,
            bool expectedHasFacing,
            float expectedFacing)
        {
            var spawn = CreateSpawn(CreateOverrides(overrideShape));
            bool fastPath = InvokeTryBuildBatchRequest(spawn, out object request);

            That(fastPath, Is.EqualTo(expectedFastPath));
            if (!expectedFastPath)
            {
                return;
            }

            That(GetRequestBool(request, "HasFacing"), Is.EqualTo(expectedHasFacing));
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

        private static MapLoader CreateLoader(World world)
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_MapLoaderBatchPlacementTests", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "Entities"));
                File.WriteAllText(
                    Path.Combine(root, "Configs", "config_catalog.json"),
                    @"[{ ""Path"": ""Entities/templates.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
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
                "position-facing-health" => new Dictionary<string, JsonNode>
                {
                    ["WorldPositionCm"] = WorldPosition(777, 888),
                    ["FacingDirection"] = Facing(3.25f),
                    ["Health"] = JsonNode.Parse(@"{ ""Current"": 7, ""Max"": 11 }")!,
                },
                _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
            };
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
