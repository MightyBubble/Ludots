using System;
using System.IO;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
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
    public sealed class LifecycleArchitectureTests
    {
        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            AttributeRegistry.Register("Health");
            EffectParamKeys.Initialize();
            GraphIdRegistry.Clear();
        }

        [Test]
        public void LifecycleTransactionPrograms_DeployConsumeSource_HasSevenAtomicOps()
        {
            var ops = LifecycleTransactionPrograms.DeployConsumeSource;
            That(ops.Length, Is.EqualTo(7));
            That(ops[0], Is.EqualTo(LifecycleOpId.MaterializeTemplate));
            That(ops[1], Is.EqualTo(LifecycleOpId.CopyIdentityComponents));
            That(ops[2], Is.EqualTo(LifecycleOpId.CopyAttributeSlice));
            That(ops[3], Is.EqualTo(LifecycleOpId.ClearActiveEffects));
            That(ops[4], Is.EqualTo(LifecycleOpId.TransferStableId));
            That(ops[5], Is.EqualTo(LifecycleOpId.RewireSelection));
            That(ops[6], Is.EqualTo(LifecycleOpId.ConsumeEntity));
        }

        [Test]
        public void RuntimeEntityLifecycleTransactionExecutor_RunsDeployProgram()
        {
            string templatesJson = @"[
              {
                ""id"": ""lifecycle_target"",
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

            using var world = World.Create();
            var source = world.Create(
                WorldPositionCm.FromCm(9000, 8000),
                new PresentationStableId { Value = 11 },
                new AttributeBuffer(),
                new AbilityExecInstance { HasTargetPos = 1, TargetPosCm = Fix64Vec2.FromInt(9000, 8000) });
            world.Get<AttributeBuffer>(source).SetBase(AttributeRegistry.GetId("Health"), 55f);

            var services = new EntityLifecycleRuntimeServices(
                world,
                templates,
                new EntityTemplateKeyRegistry(),
                new PresentationStableIdAllocator());

            var state = new LifecycleTransactionState
            {
                Source = source,
                TargetTemplateId = "lifecycle_target",
                PlacementCm = Fix64Vec2.FromInt(9000, 8000),
                Snapshot = LifecycleSnapshot.Capture(world, source),
            };
            RuntimeEntityLifecycleTransactionExecutor.ConfigureDeployConsumeSourceDefaults(state);

            Entity target = RuntimeEntityLifecycleTransactionExecutor.Execute(
                services,
                state,
                LifecycleTransactionPrograms.DeployConsumeSource);

            That(world.IsAlive(target), Is.True);
            That(world.Has<PresentationDestroyPending>(source), Is.True);
            That(world.Get<PresentationStableId>(target).Value, Is.EqualTo(11));
        }

        [Test]
        public void DeployConsumeSource_GraphPreset_TransfersTemplateAndConsumesSource()
        {
            string templatesJson = @"[
              {
                ""id"": ""lifecycle_target"",
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
            var templateKeys = new EntityTemplateKeyRegistry();
            templateKeys.Register("lifecycle_target");

            using var world = World.Create();
            var source = world.Create(
                WorldPositionCm.FromCm(9000, 8000),
                new PresentationStableId { Value = 42 },
                new AttributeBuffer(),
                new AbilityExecInstance { HasTargetPos = 1, TargetPosCm = Fix64Vec2.FromInt(9000, 8000) });
            world.Get<AttributeBuffer>(source).SetBase(AttributeRegistry.GetId("Health"), 55f);

            var programs = new GraphProgramRegistry();
            int graphId = RegisterDeployConsumeSourceGraph(programs);
            var presetTypes = new PresetTypeRegistry();
            var preset = new PresetTypeDefinition { Type = EffectPresetType.DeployConsumeSource };
            preset.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Graph(graphId);
            presetTypes.Register(preset);

            var templateRegistry = new EffectTemplateRegistry();
            var tpl = new EffectTemplateData
            {
                PresetType = EffectPresetType.DeployConsumeSource,
            };
            tpl.ConfigParams.TryAddEntityTemplateKeyId(EffectParamKeys.TargetEntityTemplateKeyId, templateKeys.GetId("lifecycle_target"));
            EffectTemplateIdRegistry.Register("Effect.Test.DeployConsumeSource");
            int effectTemplateId = EffectTemplateIdRegistry.GetId("Effect.Test.DeployConsumeSource");
            templateRegistry.Register(effectTemplateId, tpl);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            var executor = new EffectPhaseExecutor(
                programs,
                presetTypes,
                builtinHandlers,
                GasGraphOpHandlerTable.Instance,
                templateRegistry);
            var graphApi = new GasGraphRuntimeApi(world);
            var lifecycleServices = new EntityLifecycleRuntimeServices(
                world,
                templates,
                templateKeys,
                new PresentationStableIdAllocator());
            var runtime = new BuiltinHandlerExecutionContext { LifecycleServices = lifecycleServices };

            executor.ExecutePhase(
                world,
                graphApi,
                source,
                source,
                source,
                default,
                EffectPhaseId.OnApply,
                default,
                EffectPresetType.DeployConsumeSource,
                0,
                effectTemplateId,
                in tpl.ConfigParams,
                runtime);

            That(world.Has<PresentationDestroyPending>(source), Is.True);

            Entity target = default;
            world.Query(new QueryDescription().WithAll<Name>(), (Entity entity, ref Name name) =>
            {
                if (name.Value == "Target")
                {
                    target = entity;
                }
            });

            That(world.IsAlive(target), Is.True);
            That(world.Get<PresentationStableId>(target).Value, Is.EqualTo(42));
            That(world.Get<AttributeBuffer>(target).GetBase(AttributeRegistry.GetId("Health")), Is.EqualTo(55f).Within(0.001f));
        }

        [Test]
        public void DeployConsumeSource_GraphPreset_RequiresTargetEntityTemplateConfig()
        {
            string effectsJson = @"[
              {
                ""id"": ""Effect.Test.DeployMissingTemplate"",
                ""presetType"": ""DeployConsumeSource"",
                ""lifetime"": ""Instant"",
                ""participatesInResponse"": true
              }
            ]";

            var pipeline = CreateEffectsPipeline(effectsJson);
            var loader = new EffectTemplateLoader(
                pipeline,
                new EffectTemplateRegistry(),
                entityTemplateKeys: new EntityTemplateKeyRegistry());

            var ex = Throws<InvalidOperationException>(() => loader.Load(ConfigCatalogLoader.Load(pipeline)));
            That(ex!.Message, Does.Contain("_ep.targetEntityTemplate"));
        }

        [Test]
        public void RuntimeEntityLifecycleSystem_TransfersTemplateAndConsumesSource()
        {
            string templatesJson = @"[
              {
                ""id"": ""lifecycle_target"",
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

            using var world = World.Create();
            var source = world.Create(
                WorldPositionCm.FromCm(9000, 8000),
                new PresentationStableId { Value = 77 },
                new PlayerOwner { PlayerId = 3 },
                new AttributeBuffer(),
                new AbilityExecInstance { HasTargetPos = 1, TargetPosCm = Fix64Vec2.FromInt(9000, 8000) });
            world.Get<AttributeBuffer>(source).SetBase(AttributeRegistry.GetId("Health"), 75f);

            var requests = new RuntimeEntityLifecycleQueue(capacity: 2);
            var system = new RuntimeEntityLifecycleSystem(
                world,
                requests,
                templates,
                new EntityTemplateKeyRegistry(),
                new PresentationStableIdAllocator());

            That(requests.TryEnqueue(new RuntimeEntityLifecycleRequest
            {
                Source = source,
                EffectContextSource = source,
                EffectContextTarget = source,
                TargetTemplateId = "lifecycle_target",
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
            That(world.Get<WorldPositionCm>(target).Value, Is.EqualTo(Fix64Vec2.FromInt(9000, 8000)));
            That(world.Get<AttributeBuffer>(target).GetBase(AttributeRegistry.GetId("Health")), Is.EqualTo(75f).Within(0.001f));
        }

        [Test]
        public void RuntimeEntityLifecycleSystem_AtTargetPoint_WithoutTargetPoint_FailsAndLeavesSourceIntact()
        {
            string templatesJson = @"[
              {
                ""id"": ""lifecycle_target_only"",
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

            using var world = World.Create();
            var source = world.Create(
                WorldPositionCm.FromCm(500, 600),
                new PresentationStableId { Value = 1 });

            var requests = new RuntimeEntityLifecycleQueue(capacity: 1);
            var system = new RuntimeEntityLifecycleSystem(
                world,
                requests,
                templates,
                new EntityTemplateKeyRegistry(),
                new PresentationStableIdAllocator());

            That(requests.TryEnqueue(new RuntimeEntityLifecycleRequest
            {
                Source = source,
                EffectContextSource = source,
                EffectContextTarget = source,
                TargetTemplateId = "lifecycle_target_only",
            }), Is.True);

            var ex = Throws<LifecycleExecutionException>(() => system.Update(0f));
            That(ex!.Message, Does.Contain("target point"));
            That(world.IsAlive(source), Is.True);
            That(world.Has<PresentationDestroyPending>(source), Is.False);
        }

        [Test]
        public void RuntimeEntityLifecycleSystem_AtTargetPoint_UsesAbilityTargetPosition()
        {
            string templatesJson = @"[
              {
                ""id"": ""lifecycle_target_only"",
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

            using var world = World.Create();
            var source = world.Create(
                WorldPositionCm.FromCm(100, 100),
                new PresentationStableId { Value = 1 },
                new AbilityExecInstance { HasTargetPos = 1, TargetPosCm = Fix64Vec2.FromInt(9000, 8000) });

            var requests = new RuntimeEntityLifecycleQueue(capacity: 1);
            var system = new RuntimeEntityLifecycleSystem(
                world,
                requests,
                templates,
                new EntityTemplateKeyRegistry(),
                new PresentationStableIdAllocator());

            That(requests.TryEnqueue(new RuntimeEntityLifecycleRequest
            {
                Source = source,
                EffectContextSource = source,
                EffectContextTarget = source,
                TargetTemplateId = "lifecycle_target_only",
            }), Is.True);

            system.Update(0f);

            Entity target = default;
            world.Query(new QueryDescription().WithAll<Name>(), (Entity entity, ref Name name) =>
            {
                if (name.Value == "Target")
                {
                    target = entity;
                }
            });

            That(world.IsAlive(target), Is.True);
            That(world.Get<WorldPositionCm>(target).Value, Is.EqualTo(Fix64Vec2.FromInt(9000, 8000)));
        }

        [Test]
        public void RuntimeEntityLifecycleSystem_RejectsSourceAlreadyPendingDestroy()
        {
            string templatesJson = @"[
              {
                ""id"": ""lifecycle_target_only"",
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

            using var world = World.Create();
            var source = world.Create(
                WorldPositionCm.FromCm(100, 100),
                new PresentationStableId { Value = 1 },
                new PresentationDestroyPending(),
                new AbilityExecInstance { HasTargetPos = 1, TargetPosCm = Fix64Vec2.FromInt(100, 100) });

            var requests = new RuntimeEntityLifecycleQueue(capacity: 1);
            var system = new RuntimeEntityLifecycleSystem(
                world,
                requests,
                templates,
                new EntityTemplateKeyRegistry(),
                new PresentationStableIdAllocator());

            That(requests.TryEnqueue(new RuntimeEntityLifecycleRequest
            {
                Source = source,
                EffectContextSource = source,
                EffectContextTarget = source,
                TargetTemplateId = "lifecycle_target_only",
            }), Is.True);

            var ex = Throws<LifecycleExecutionException>(() => system.Update(0f));
            That(ex!.Message, Does.Contain("pending destroy"));
        }

        [Test]
        public void RuntimeEntityLifecycleSystem_FailsWhenTargetTemplateMissingHealth()
        {
            string templatesJson = @"[
              {
                ""id"": ""lifecycle_target_no_health"",
                ""components"": {
                  ""Name"": { ""Value"": ""Target"" },
                  ""WorldPositionCm"": { ""Value"": { ""X"": 0, ""Y"": 0 } },
                  ""GameplayTagContainer"": {},
                  ""TagCountContainer"": {}
                }
              }
            ]";

            var pipeline = CreateTemplatesPipeline(templatesJson);
            var templates = new DataRegistry<EntityTemplate>(pipeline);
            templates.Load("Entities/templates.json", ConfigCatalogLoader.Load(pipeline));

            using var world = World.Create();
            var source = world.Create(
                WorldPositionCm.FromCm(100, 100),
                new PresentationStableId { Value = 1 },
                new AttributeBuffer(),
                new AbilityExecInstance { HasTargetPos = 1, TargetPosCm = Fix64Vec2.FromInt(5000, 5000) });
            world.Get<AttributeBuffer>(source).SetBase(AttributeRegistry.GetId("Health"), 50f);

            var requests = new RuntimeEntityLifecycleQueue(capacity: 1);
            var system = new RuntimeEntityLifecycleSystem(
                world,
                requests,
                templates,
                new EntityTemplateKeyRegistry(),
                new PresentationStableIdAllocator());

            That(requests.TryEnqueue(new RuntimeEntityLifecycleRequest
            {
                Source = source,
                EffectContextSource = source,
                EffectContextTarget = source,
                TargetTemplateId = "lifecycle_target_no_health",
            }), Is.True);

            var ex = Throws<LifecycleExecutionException>(() => system.Update(0f));
            That(ex!.Message, Does.Contain("AttributeBuffer"));
        }

        private static int RegisterDeployConsumeSourceGraph(GraphProgramRegistry programs)
        {
            var cfg = new GraphConfig
            {
                Id = "Graph.Lifecycle.DeployConsumeSource",
                Entry = "begin",
                Nodes =
                [
                    new GraphNodeConfig { Id = "begin", Op = "BeginLifecycleTransaction", Next = "materialize" },
                    new GraphNodeConfig { Id = "materialize", Op = "InvokeBuiltin", BuiltinHandler = "MaterializeTemplate", Next = "copyIdentity" },
                    new GraphNodeConfig { Id = "copyIdentity", Op = "InvokeBuiltin", BuiltinHandler = "CopyIdentityComponents", Next = "copyAttrs" },
                    new GraphNodeConfig { Id = "copyAttrs", Op = "InvokeBuiltin", BuiltinHandler = "CopyAttributeSlice", Next = "clearFx" },
                    new GraphNodeConfig { Id = "clearFx", Op = "InvokeBuiltin", BuiltinHandler = "ClearActiveEffects", Next = "transferId" },
                    new GraphNodeConfig { Id = "transferId", Op = "InvokeBuiltin", BuiltinHandler = "TransferStableId", Next = "rewire" },
                    new GraphNodeConfig { Id = "rewire", Op = "InvokeBuiltin", BuiltinHandler = "RewireSelection", Next = "consume" },
                    new GraphNodeConfig { Id = "consume", Op = "InvokeBuiltin", BuiltinHandler = "ConsumeEntity" },
                ],
            };

            var (package, _, diagnostics) = GraphCompiler.CompileWithOutputs(cfg);
            if (package == null)
            {
                throw new InvalidOperationException(diagnostics[0].Message);
            }

            GraphIdRegistry.Register(cfg.Id);
            int graphId = GraphIdRegistry.GetId(cfg.Id);
            var symbolResolver = new GasGraphSymbolResolver(
                new Ludots.Core.Gameplay.Relationships.RelationshipTypeRegistry(),
                new Ludots.Core.Gameplay.Relationships.RelationshipMetricRegistry(),
                new Ludots.Core.Gameplay.Relationships.RelationshipFlagRegistry(),
                new Ludots.Core.Gameplay.Relationships.RelationshipReasonRegistry(),
                new TargetDispatchPresetRegistry(),
                new EntityTemplateKeyRegistry());
            GraphProgramSymbolPatcher.Patch(package.Value.Symbols, package.Value.Program, symbolResolver);
            programs.Register(graphId, package.Value.Program);
            return graphId;
        }

        private static ConfigPipeline CreateEffectsPipeline(string effectsJson)
        {
            var root = Path.Combine(Path.GetTempPath(), $"LifecycleEffects_{Guid.NewGuid():N}");
            var gasDir = Path.Combine(root, "Configs", "GAS");
            Directory.CreateDirectory(gasDir);
            File.WriteAllText(Path.Combine(gasDir, "effects.json"), effectsJson);
            File.WriteAllText(
                Path.Combine(root, "Configs", "config_catalog.json"),
                @"[
  { ""Path"": ""GAS/effects.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }
]");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            return new ConfigPipeline(vfs, modLoader);
        }

        private static ConfigPipeline CreateTemplatesPipeline(string templatesJson)
        {
            var root = Path.Combine(Path.GetTempPath(), $"LifecycleTest_{Guid.NewGuid():N}");
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
