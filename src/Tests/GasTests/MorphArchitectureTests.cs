using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Morph;
using Ludots.Core.Gameplay.Morph.Config;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GasTests
{
    [TestFixture]
    public sealed class MorphArchitectureTests
    {
        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            AttributeRegistry.Register("Health");
            TagRegistry.Register("Unit.Mobile");
        }

        [Test]
        public void BuiltinHandlers_MorphEntity_EnqueuesRuntimeMorphRequests()
        {
            using var world = World.Create();
            var source = world.Create(
                WorldPositionCm.FromCm(1200, 3400),
                new PresentationStableId { Value = 42 });
            var effect = world.Create();
            var queue = new RuntimeEntityMorphQueue(capacity: 4);
            var runtime = new BuiltinHandlerExecutionContext
            {
                MorphRequests = queue,
            };
            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            var ctx = new EffectContext { Source = source, Target = source };
            var tpl = new EffectTemplateData
            {
                Morph = new MorphDescriptor
                {
                    Subject = RelationEntitySlot.Source,
                    TargetTemplateId = "morph_target",
                    MorphProfileId = 3,
                    OnMorphEffectTemplateId = 55,
                },
            };

            var mergedParams = new EffectConfigParams();
            registry.Invoke(BuiltinHandlerId.MorphEntity, world, effect, ref ctx, in mergedParams, in tpl, runtime);

            That(queue.TryDequeue(out RuntimeEntityMorphRequest request), Is.True);
            That(request.Source, Is.EqualTo(source));
            That(request.TargetTemplateId, Is.EqualTo("morph_target"));
            That(request.MorphProfileId, Is.EqualTo(3));
            That(request.OnMorphEffectTemplateId, Is.EqualTo(55));
            That(queue.TryDequeue(out _), Is.False);
        }

        [Test]
        public void RuntimeEntityMorphSystem_TransfersTemplateAndConsumesSource()
        {
            string templatesJson = @"[
              {
                ""id"": ""morph_source"",
                ""components"": {
                  ""Name"": { ""Value"": ""Source"" },
                  ""WorldPositionCm"": { ""Value"": { ""X"": 1000, ""Y"": 2000 } },
                  ""AttributeBuffer"": { ""base"": { ""Health"": 75 } },
                  ""GameplayTagContainer"": { ""tags"": [""Unit.Mobile""] },
                  ""TagCountContainer"": {}
                }
              },
              {
                ""id"": ""morph_target"",
                ""components"": {
                  ""Name"": { ""Value"": ""Target"" },
                  ""WorldPositionCm"": { ""Value"": { ""X"": 0, ""Y"": 0 } },
                  ""AttributeBuffer"": { ""base"": { ""Health"": 10 } },
                  ""GameplayTagContainer"": {},
                  ""TagCountContainer"": {}
                }
              }
            ]";

            var pipeline = CreateTemplatesPipeline(templatesJson);
            var templates = new DataRegistry<EntityTemplate>(pipeline);
            templates.Load("Entities/templates.json", ConfigCatalogLoader.Load(pipeline));

            var profiles = new MorphProfileRegistry();
            profiles.Register("morph.test", new MorphProfileDescriptor
            {
                Placement = MorphPlacementMode.AtSource,
                StableIdPolicy = MorphStableIdPolicy.Transfer,
                DestroySource = true,
                AttributeInheritMode = MorphAttributeInheritMode.IntersectByName,
                InheritAttributeIds = [AttributeRegistry.GetId("Health")],
                StripTagIds = [TagRegistry.GetId("Unit.Mobile")],
            });

            using var world = World.Create();
            var source = world.Create(
                WorldPositionCm.FromCm(1000, 2000),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(1000, 2000) },
                new PresentationStableId { Value = 77 },
                new AttributeBuffer(),
                new GameplayTagContainer());
            world.Get<AttributeBuffer>(source).SetBase(AttributeRegistry.GetId("Health"), 75f);
            world.Get<GameplayTagContainer>(source).AddTag(TagRegistry.GetId("Unit.Mobile"));

            var requests = new RuntimeEntityMorphQueue(capacity: 2);
            var templateKeys = new EntityTemplateKeyRegistry();
            var stableIds = new PresentationStableIdAllocator();
            var system = new RuntimeEntityMorphSystem(
                world,
                requests,
                profiles,
                templates,
                templateKeys,
                stableIds);

            That(requests.TryEnqueue(new RuntimeEntityMorphRequest
            {
                Source = source,
                TargetTemplateId = "morph_target",
                MorphProfileId = profiles.GetId("morph.test"),
            }), Is.True);

            system.Update(0f);

            That(world.IsAlive(source), Is.True);
            That(world.Has<PresentationDestroyPending>(source), Is.True);

            Entity target = default;
            int targetCount = 0;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (name.Value == "Target")
                {
                    target = entity;
                    targetCount++;
                }
            });

            That(targetCount, Is.EqualTo(1));
            That(world.IsAlive(target), Is.True);
            That(world.Get<PresentationStableId>(target).Value, Is.EqualTo(77));
            That(world.Get<WorldPositionCm>(target).Value, Is.EqualTo(Fix64Vec2.FromInt(1000, 2000)));
            That(world.Get<AttributeBuffer>(target).GetBase(AttributeRegistry.GetId("Health")), Is.EqualTo(75f).Within(0.001f));
            That(world.Get<AttributeBuffer>(target).GetCurrent(AttributeRegistry.GetId("Health")), Is.EqualTo(75f).Within(0.001f));
            That(world.Get<GameplayTagContainer>(target).HasTag(TagRegistry.GetId("Unit.Mobile")), Is.False);
        }

        [Test]
        public void RuntimeEntityMorphSystem_AtTargetPoint_WithoutTargetPoint_FailsAndLeavesSourceIntact()
        {
            string templatesJson = @"[
              {
                ""id"": ""morph_target_only"",
                ""components"": {
                  ""Name"": { ""Value"": ""Target"" },
                  ""WorldPositionCm"": { ""Value"": { ""X"": 0, ""Y"": 0 } },
                  ""AttributeBuffer"": { ""base"": {} },
                  ""GameplayTagContainer"": {},
                  ""TagCountContainer"": {}
                }
              }
            ]";

            var pipeline = CreateTemplatesPipeline(templatesJson);
            var templates = new DataRegistry<EntityTemplate>(pipeline);
            templates.Load("Entities/templates.json", ConfigCatalogLoader.Load(pipeline));

            var profiles = new MorphProfileRegistry();
            profiles.Register("morph.at_target", new MorphProfileDescriptor
            {
                Placement = MorphPlacementMode.AtTargetPoint,
                StableIdPolicy = MorphStableIdPolicy.AllocateNew,
                DestroySource = true,
            });

            using var world = World.Create();
            var source = world.Create(
                WorldPositionCm.FromCm(500, 600),
                new PresentationStableId { Value = 12 });

            var requests = new RuntimeEntityMorphQueue(capacity: 1);
            var system = new RuntimeEntityMorphSystem(
                world,
                requests,
                profiles,
                templates,
                new EntityTemplateKeyRegistry(),
                new PresentationStableIdAllocator());

            That(requests.TryEnqueue(new RuntimeEntityMorphRequest
            {
                Source = source,
                TargetTemplateId = "morph_target_only",
                MorphProfileId = profiles.GetId("morph.at_target"),
            }), Is.True);

            var ex = Throws<MorphExecutionException>(() => system.Update(0f));
            That(ex!.Message, Does.Contain("placement mode"));

            That(world.IsAlive(source), Is.True);
            That(world.Has<PresentationDestroyPending>(source), Is.False);

            int targetCount = 0;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity _, ref Name name) =>
            {
                if (name.Value == "Target")
                {
                    targetCount++;
                }
            });
            That(targetCount, Is.EqualTo(0));
        }

        [Test]
        public void RuntimeEntityMorphSystem_AtTargetPoint_UsesAbilityTargetPosition()
        {
            string templatesJson = @"[
              {
                ""id"": ""morph_target_only"",
                ""components"": {
                  ""Name"": { ""Value"": ""Target"" },
                  ""WorldPositionCm"": { ""Value"": { ""X"": 0, ""Y"": 0 } },
                  ""AttributeBuffer"": { ""base"": {} },
                  ""GameplayTagContainer"": {},
                  ""TagCountContainer"": {}
                }
              }
            ]";

            var pipeline = CreateTemplatesPipeline(templatesJson);
            var templates = new DataRegistry<EntityTemplate>(pipeline);
            templates.Load("Entities/templates.json", ConfigCatalogLoader.Load(pipeline));

            var profiles = new MorphProfileRegistry();
            profiles.Register("morph.at_target", new MorphProfileDescriptor
            {
                Placement = MorphPlacementMode.AtTargetPoint,
                StableIdPolicy = MorphStableIdPolicy.AllocateNew,
                DestroySource = false,
            });

            using var world = World.Create();
            var source = world.Create(
                WorldPositionCm.FromCm(100, 200),
                new PresentationStableId { Value = 12 },
                new AbilityExecInstance
                {
                    HasTargetPos = 1,
                    TargetPosCm = Fix64Vec2.FromInt(900, 800),
                });

            var requests = new RuntimeEntityMorphQueue(capacity: 1);
            var system = new RuntimeEntityMorphSystem(
                world,
                requests,
                profiles,
                templates,
                new EntityTemplateKeyRegistry(),
                new PresentationStableIdAllocator());

            That(requests.TryEnqueue(new RuntimeEntityMorphRequest
            {
                Source = source,
                EffectContextSource = source,
                TargetTemplateId = "morph_target_only",
                MorphProfileId = profiles.GetId("morph.at_target"),
            }), Is.True);

            system.Update(0f);

            Entity target = default;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (name.Value == "Target")
                {
                    target = entity;
                }
            });

            That(world.IsAlive(target), Is.True);
            That(world.Get<WorldPositionCm>(target).Value, Is.EqualTo(Fix64Vec2.FromInt(900, 800)));
        }

        [Test]
        public void MorphProfileLoader_CompilesDeployProfile()
        {
            const string json = @"[
              {
                ""id"": ""morph.rts.deploy_consume_source"",
                ""placement"": ""AtTargetPoint"",
                ""stableIdPolicy"": ""Transfer"",
                ""destroySource"": true,
                ""inherit"": {
                  ""identity"": [""PlayerOwner"", ""Team""],
                  ""attributes"": { ""mode"": ""IntersectByName"", ""names"": [""Health""] },
                  ""tags"": { ""strip"": [""Unit.Mobile""] },
                  ""effects"": { ""mode"": ""StripAll"" },
                  ""selection"": { ""replaceSourceInAllSets"": true }
                }
              }
            ]";

            var registry = new MorphProfileRegistry();
            LoadMorphProfilesFromJson(registry, json);
            var profile = registry.Get(registry.GetId("morph.rts.deploy_consume_source"));

            That(profile.Placement, Is.EqualTo(MorphPlacementMode.AtTargetPoint));
            That(profile.StableIdPolicy, Is.EqualTo(MorphStableIdPolicy.Transfer));
            That(profile.DestroySource, Is.True);
            That(profile.CopyPlayerOwner, Is.True);
            That(profile.CopyTeam, Is.True);
            That(profile.ReplaceSelection, Is.True);
            That(profile.InheritAttributeIds, Has.Length.EqualTo(1));
        }

        private static void LoadMorphProfilesFromJson(MorphProfileRegistry registry, string json)
        {
            var array = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsArray()
                ?? throw new InvalidOperationException("Morph profile JSON root must be an array.");

            for (int i = 0; i < array.Count; i++)
            {
                var cfg = array[i]?.Deserialize<MorphProfileConfig>(new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    IncludeFields = true,
                    UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
                }) ?? throw new InvalidOperationException("Morph profile entry must be an object.");

                registry.Register(cfg.Id, MorphProfileLoader.Compile(cfg, "test.json"));
            }
        }

        private static ConfigPipeline CreateTemplatesPipeline(string templatesJson)
        {
            var root = Path.Combine(Path.GetTempPath(), $"MorphTest_{Guid.NewGuid():N}");
            var entityDir = Path.Combine(root, "Configs", "Entities");
            Directory.CreateDirectory(entityDir);
            File.WriteAllText(Path.Combine(entityDir, "templates.json"), templatesJson);
            File.WriteAllText(
                Path.Combine(root, "Configs", "config_catalog.json"),
                @"[
  { ""Path"": ""Entities/templates.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }
]");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            return new ConfigPipeline(vfs, modLoader);
        }
    }
}
