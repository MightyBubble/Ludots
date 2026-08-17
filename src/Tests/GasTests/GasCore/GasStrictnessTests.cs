using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Bindings;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class GasStrictnessTests
    {
        private readonly TagOps _tagOps = new(
            new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
            new TagRuleRegistry());

        [Test]
        public void RelationshipFilter_Parse_RejectsAliasesCasingWhitespaceAndNumericValues()
        {
            That(RelationshipFilterUtil.Parse("Hostile"), Is.EqualTo(RelationshipFilter.Hostile));

            Throws<InvalidOperationException>(() => RelationshipFilterUtil.Parse("hostile"));
            Throws<InvalidOperationException>(() => RelationshipFilterUtil.Parse(" Hostile"));
            Throws<InvalidOperationException>(() => RelationshipFilterUtil.Parse("1"));
            Throws<ArgumentException>(() => RelationshipFilterUtil.Parse(null!));
        }

        [Test]
        public void RelationshipFilter_Passes_RejectsUnsupportedEnumValue()
        {
            Throws<ArgumentOutOfRangeException>(() =>
                RelationshipFilterUtil.Passes((RelationshipFilter)255, sourceTeamId: 1, targetTeamId: 2));
        }

        [Test]
        public void GasConditionEvaluator_RejectsNoneAndUnsupportedKind()
        {
            using var world = World.Create();
            var target = world.Create(new GameplayTagContainer());

            var none = new GasCondition(GasConditionKind.None, tagId: 1, TagSense.Present);
            Throws<InvalidOperationException>(() =>
                GasConditionEvaluator.ShouldExpire(world, target, in none, _tagOps));

            var unsupported = new GasCondition((GasConditionKind)255, tagId: 1, TagSense.Present);
            Throws<ArgumentOutOfRangeException>(() =>
                GasConditionEvaluator.ShouldExpire(world, target, in unsupported, _tagOps));
        }

        [Test]
        public void EffectPipelineSystems_RejectNonPositiveStepRateAtConstruction()
        {
            using var world = World.Create();

            Throws<ArgumentOutOfRangeException>(() => new EffectProposalProcessingSystem(
                world,
                new EffectRequestQueue(),
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                new DiscreteClock(),
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                stepRateHz: 0));
            Throws<ArgumentOutOfRangeException>(() => new EffectApplicationSystem(
                world,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                new DiscreteClock(),
                stepRateHz: 0));
            Throws<ArgumentOutOfRangeException>(() => new EffectLifetimeSystem(
                world,
                new DiscreteClock(),
                new GasConditionRegistry(),
                snapshotCapacity: 1,
                fanOutCommandCapacity: GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                stepRateHz: 0));
        }

        [Test]
        public void EffectPipelineSystems_RequireGameplayClockAtConstruction()
        {
            using var world = World.Create();

            Throws<ArgumentNullException>(() => new EffectProposalProcessingSystem(
                world,
                new EffectRequestQueue(),
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                clock: null!,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types));
            Throws<ArgumentNullException>(() => new EffectApplicationSystem(
                world,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                clock: null!));
            Throws<ArgumentNullException>(() => new EffectLifetimeSystem(
                world,
                null!,
                new GasConditionRegistry(),
                snapshotCapacity: 1,
                fanOutCommandCapacity: GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
        }

        [Test]
        public void GraphTargetList_FilterRelationship_RejectsUnsupportedModeBeforeApiUse()
        {
            using var world = World.Create();

            Throws<ArgumentOutOfRangeException>(() => ExecuteInvalidRelationshipFilterMode(world));
        }

        [TestCase(
            """
            [
              {
                "Id": "Bind.Physics.ForceInput2D.X",
                "attribute": "Physics.ForceRequestX",
                "sink": "Physics.ForceInput2D",
                "channel": 0,
                "mode": "Override",
                "scale": 1.0,
                "resetPolicy": "None"
              }
            ]
            """,
            "exact string field 'id'")]
        [TestCase(
            """
            [
              {
                "id": "Bind.Physics.ForceInput2D.X",
                "attribute": "Physics.ForceRequestX",
                "sink": "Physics.ForceInput2D",
                "channel": 0,
                "mode": "override",
                "scale": 1.0,
                "resetPolicy": "None"
              }
            ]
            """,
            "unsupported mode 'override'")]
        [TestCase(
            """
            [
              {
                "id": "Bind.Physics.ForceInput2D.X",
                "attribute": "Physics.ForceRequestX",
                "sink": "Physics.ForceInput2D",
                "channel": 0,
                "mode": "Override",
                "resetPolicy": "None"
              }
            ]
            """,
            "scale requires an explicit finite number")]
        [TestCase(
            """
            [
              {
                "id": "Bind.Physics.ForceInput2D.X",
                "attribute": " Physics.ForceRequestX",
                "sink": "Physics.ForceInput2D",
                "channel": 0,
                "mode": "Override",
                "scale": 1.0,
                "resetPolicy": "None"
              }
            ]
            """,
            "attribute must not include")]
        public void AttributeBindingLoader_RejectsAliasesImplicitFieldsAndNonCanonicalStrings(
            string bindingsJson,
            string expectedMessage)
        {
            string root = CreateTempRoot("Ludots_GasStrictness_AttributeBindings");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "GAS"));
                File.WriteAllText(Path.Combine(root, "GAS", "attribute_bindings.json"), bindingsJson);

                var pipeline = BuildCorePipeline(root);
                var catalog = BuildCatalog("GAS/attribute_bindings.json");
                var sinks = new AttributeSinkRegistry();
                GasAttributeSinks.RegisterBuiltins(sinks);
                var registry = new AttributeBindingRegistry();
                var loader = new AttributeBindingLoader(pipeline, sinks, registry);

                InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                    loader.Load(catalog, relativePath: "GAS/attribute_bindings.json"))!;
                That(ex.Message, Does.Contain(expectedMessage));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [TestCase(
            """
            [
              {
                "Id": "SourceToResolved",
                "payloadSource": "OriginalSource",
                "payloadTarget": "ResolvedEntity",
                "payloadTargetContext": "OriginalTarget"
              }
            ]
            """,
            "exact string field 'id'")]
        [TestCase(
            """
            [
              {
                "id": "SourceToResolved",
                "payloadSource": "originalsource",
                "payloadTarget": "ResolvedEntity",
                "payloadTargetContext": "OriginalTarget"
              }
            ]
            """,
            "unsupported payloadSource 'originalsource'")]
        [TestCase(
            """
            [
              {
                "id": "SourceToResolved",
                "payloadSource": "OriginalSource",
                "payloadTarget": "ResolvedEntity",
                "payloadTargetContext": "OriginalTarget "
              }
            ]
            """,
            "payloadTargetContext must not include")]
        public void TargetDispatchPresetLoader_RejectsAliasesAndNonCanonicalStrings(
            string presetsJson,
            string expectedMessage)
        {
            string root = CreateTempRoot("Ludots_GasStrictness_TargetDispatch");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "GAS"));
                File.WriteAllText(Path.Combine(root, "GAS", "target_dispatch_presets.json"), presetsJson);

                var pipeline = BuildCorePipeline(root);
                var catalog = BuildCatalog("GAS/target_dispatch_presets.json");
                var registry = new TargetDispatchPresetRegistry();
                var loader = new TargetDispatchPresetLoader(pipeline, registry);

                InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                    loader.Load(catalog, relativePath: "GAS/target_dispatch_presets.json"))!;
                That(ex.Message, Does.Contain(expectedMessage));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [TestCase(
            """{ "Mode": "Auto", "stepEveryFixedTicks": 1 }""",
            "mode requires an explicit semantic string")]
        [TestCase(
            """{ "mode": "auto", "stepEveryFixedTicks": 1 }""",
            "mode 'auto' is invalid")]
        [TestCase(
            """{ "mode": "Auto" }""",
            "stepEveryFixedTicks requires an explicit integer field")]
        public void GasClockConfigLoader_RejectsImplicitDefaultsAndLooseCasing(
            string clockJson,
            string expectedMessage)
        {
            string root = CreateTempRoot("Ludots_GasStrictness_Clock");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "GAS"));
                File.WriteAllText(Path.Combine(root, "GAS", "clock.json"), clockJson);

                var pipeline = BuildCorePipeline(root);
                var catalog = new ConfigCatalog();
                catalog.Add(new ConfigCatalogEntry("GAS/clock.json", ConfigMergePolicy.DeepObject));
                var loader = new GasClockConfigLoader(pipeline);

                InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                    loader.Load(catalog, relativePath: "GAS/clock.json"))!;
                That(ex.Message, Does.Contain(expectedMessage));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void GasSemanticRegistries_DoNotProvideCaseInsensitiveAliases()
        {
            var sinks = new AttributeSinkRegistry();
            GasAttributeSinks.RegisterBuiltins(sinks);
            That(sinks.GetId("Physics.ForceInput2D"), Is.GreaterThanOrEqualTo(0));
            That(sinks.GetId("physics.forceinput2d"), Is.EqualTo(-1));

            var presets = new TargetDispatchPresetRegistry();
            presets.Register("SourceToResolved", new TargetResolverContextMapping
            {
                PayloadSource = ContextSlot.OriginalSource,
                PayloadTarget = ContextSlot.ResolvedEntity,
                PayloadTargetContext = ContextSlot.OriginalTarget,
            });

            That(presets.GetId("SourceToResolved"), Is.GreaterThan(0));
            That(presets.TryGetId("sourcetoresolved", out _), Is.False);
            Throws<InvalidOperationException>(() => presets.GetId("sourcetoresolved"));
        }

        private static void ExecuteInvalidRelationshipFilterMode(World world)
        {
            var typeRegistry = new RelationshipTypeRegistry();
            var metricRegistry = new RelationshipMetricRegistry();
            var flagRegistry = new RelationshipFlagRegistry();
            var bandRegistry = new RelationshipBandRegistry();
            var changeBuffer = new RelationshipChangeBuffer();
            var relationships = new RelationshipRuntime(world, typeRegistry, metricRegistry, flagRegistry, bandRegistry, changeBuffer, new RelationshipReverseIndex(world));
            var api = new GasGraphRuntimeApi(world, tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()), relationshipRuntime: relationships);

            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<GraphInstruction> program = stackalloc GraphInstruction[1];
            program[0] = new GraphInstruction
            {
                Op = (ushort)GraphNodeOp.QueryFilterRelationship,
                A = 0,
                Imm = 255,
            };

            var targetList = new GraphTargetList(targets);
            var state = new GraphExecutionState
            {
                World = world,
                Api = api,
                F = floats,
                I = ints,
                B = bools,
                E = entities,
                Targets = targets,
                TargetList = targetList,
            CallStack = new int[Ludots.Core.NodeLibraries.GASGraph.GraphVmLimits.MaxCallStackDepth],
            CallStackCount = 0,
        };

            GasGraphOpHandlerTable.Execute(ref state, program, new GasGraphOpHandlerTable());
        }

        private static string CreateTempRoot(string prefix)
        {
            string root = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static ConfigPipeline BuildCorePipeline(string coreRoot)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreRoot);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            return new ConfigPipeline(vfs, modLoader);
        }

        private static ConfigCatalog BuildCatalog(string path)
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry(path, ConfigMergePolicy.ArrayById, "id"));
            return catalog;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
