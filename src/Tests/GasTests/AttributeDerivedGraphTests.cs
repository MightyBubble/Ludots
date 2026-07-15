using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class AttributeDerivedGraphTests
    {
        [Test]
        public void DerivedGraph_SideEffectingAttributeAdd_HardFailsWithoutMutation()
        {
            using var world = World.Create();
            int attributeId = AttributeRegistry.Register("tests.derived-graph.side-effect.attribute");
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 2 },
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 5f },
                new() { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 2, B = 0, Imm = attributeId },
            };
            var programs = new GraphProgramRegistry();
            programs.Register(1, program);
            var binding = new AttributeDerivedGraphBinding();
            binding.Add(1);
            Entity entity = world.Create(
                CreateAttributes(attributeId, 10f),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags(),
                binding);
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            var graphApi = new GasGraphRuntimeApi(world, tagOps: tagOps);
            using var system = new AttributeAggregatorSystem(world, programs, graphApi, tagOps);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;

            Assert.That(ex.Message, Does.StartWith("GAS.GRAPH.ERR.DerivedAttributeSideEffectForbidden"));
            Assert.That(world.Get<AttributeBuffer>(entity).GetCurrent(attributeId), Is.EqualTo(10f));
            Assert.That(world.Get<DirtyFlags>(entity).IsAnyAttributeDirty(), Is.False);
            Assert.That(tagOps.DirtyEntities.Count, Is.Zero);
            Assert.That(world.Has<GameplayAttributeChangedBits>(entity), Is.False);
            Assert.That(world.Has<AttributeAggregateDirty>(entity), Is.True);
        }

        [Test]
        public void DerivedGraph_EffectRequest_HardFailsBeforePublish()
        {
            using var world = World.Create();
            int attributeId = AttributeRegistry.Register("tests.derived-graph.side-effect.effect-source");
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 2 },
                new() { Op = (ushort)GraphNodeOp.ApplyEffectTemplate, A = 2, Imm = 123 },
            };
            var programs = new GraphProgramRegistry();
            programs.Register(1, program);
            var binding = new AttributeDerivedGraphBinding();
            binding.Add(1);
            Entity entity = world.Create(
                CreateAttributes(attributeId, 10f),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags(),
                binding);
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            var effectRequests = new EffectRequestQueue();
            var graphApi = new GasGraphRuntimeApi(world, effectRequests: effectRequests, tagOps: tagOps);
            using var system = new AttributeAggregatorSystem(world, programs, graphApi, tagOps);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;

            Assert.That(ex.Message, Does.StartWith("GAS.GRAPH.ERR.DerivedAttributeSideEffectForbidden"));
            Assert.That(effectRequests.Count, Is.Zero);
            Assert.That(world.Get<AttributeBuffer>(entity).GetCurrent(attributeId), Is.EqualTo(10f));
            Assert.That(tagOps.DirtyEntities.Count, Is.Zero);
            Assert.That(world.Has<GameplayAttributeChangedBits>(entity), Is.False);
            Assert.That(world.Has<AttributeAggregateDirty>(entity), Is.True);
        }

        [Test]
        public void DerivedGraph_RemoveEffect_HardFailsBeforeCancellation()
        {
            using var world = World.Create();
            int attributeId = AttributeRegistry.Register("tests.derived-graph.side-effect.remove-effect-source");
            const int effectTemplateId = 123;
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 2 },
                new() { Op = (ushort)GraphNodeOp.RemoveEffectTemplate, A = 2, Imm = effectTemplateId },
            };
            var programs = new GraphProgramRegistry();
            programs.Register(1, program);
            var binding = new AttributeDerivedGraphBinding();
            binding.Add(1);
            Entity entity = world.Create(
                CreateAttributes(attributeId, 10f),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags(),
                binding);
            Entity effect = world.Create(
                new GameplayEffect(),
                new EffectTemplateRef { TemplateId = effectTemplateId });
            Assert.That(world.Get<ActiveEffectContainer>(entity).Add(effect), Is.True);
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            var graphApi = new GasGraphRuntimeApi(world, tagOps: tagOps);
            using var system = new AttributeAggregatorSystem(world, programs, graphApi, tagOps);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;

            Assert.That(ex.Message, Does.StartWith(IDerivedAttributeGraphRuntimeApi.SideEffectForbiddenError));
            Assert.That(world.Get<GameplayEffect>(effect).CancelRequested, Is.False);
            Assert.That(world.Get<AttributeBuffer>(entity).GetCurrent(attributeId), Is.EqualTo(10f));
            Assert.That(tagOps.DirtyEntities.Count, Is.Zero);
            Assert.That(world.Has<AttributeAggregateDirty>(entity), Is.True);
        }

        [Test]
        public void DerivedGraph_SendEvent_HardFailsBeforePublish()
        {
            using var world = World.Create();
            int attributeId = AttributeRegistry.Register("tests.derived-graph.side-effect.event-source");
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 2 },
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 7f },
                new() { Op = (ushort)GraphNodeOp.SendEvent, A = 2, B = 0, Imm = 123 },
            };
            var programs = new GraphProgramRegistry();
            programs.Register(1, program);
            var binding = new AttributeDerivedGraphBinding();
            binding.Add(1);
            Entity entity = world.Create(
                CreateAttributes(attributeId, 10f),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags(),
                binding);
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            var eventBus = new GameplayEventBus();
            var graphApi = new GasGraphRuntimeApi(world, eventBus: eventBus, tagOps: tagOps);
            using var system = new AttributeAggregatorSystem(world, programs, graphApi, tagOps);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;

            eventBus.Update();
            Assert.That(ex.Message, Does.StartWith(IDerivedAttributeGraphRuntimeApi.SideEffectForbiddenError));
            Assert.That(eventBus.Events.Count, Is.Zero);
            Assert.That(world.Get<AttributeBuffer>(entity).GetCurrent(attributeId), Is.EqualTo(10f));
            Assert.That(tagOps.DirtyEntities.Count, Is.Zero);
            Assert.That(world.Has<AttributeAggregateDirty>(entity), Is.True);
        }

        [Test]
        public void DerivedGraph_FanOutEffect_HardFailsBeforeDispatch()
        {
            using var world = World.Create();
            int attributeId = AttributeRegistry.Register("tests.derived-graph.side-effect.fan-out-source");
            var program = new GraphInstruction[]
            {
                new()
                {
                    Op = (ushort)GraphNodeOp.FanOutDispatchEffect,
                    Imm = 123,
                    Dst = 1,
                },
            };
            var programs = new GraphProgramRegistry();
            programs.Register(1, program);
            var binding = new AttributeDerivedGraphBinding();
            binding.Add(1);
            Entity entity = world.Create(
                CreateAttributes(attributeId, 10f),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags(),
                binding);
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            var effectRequests = new EffectRequestQueue();
            var graphApi = new GasGraphRuntimeApi(world, effectRequests: effectRequests, tagOps: tagOps);
            using var system = new AttributeAggregatorSystem(world, programs, graphApi, tagOps);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;

            Assert.That(ex.Message, Does.StartWith(IDerivedAttributeGraphRuntimeApi.SideEffectForbiddenError));
            Assert.That(effectRequests.Count, Is.Zero);
            Assert.That(world.Get<AttributeBuffer>(entity).GetCurrent(attributeId), Is.EqualTo(10f));
            Assert.That(tagOps.DirtyEntities.Count, Is.Zero);
            Assert.That(world.Has<AttributeAggregateDirty>(entity), Is.True);
        }

        [TestCase(GraphNodeOp.RelationshipEnsureLink)]
        [TestCase(GraphNodeOp.RelationshipRemoveLink)]
        [TestCase(GraphNodeOp.RelationshipSetMetric)]
        [TestCase(GraphNodeOp.RelationshipAddMetric)]
        [TestCase(GraphNodeOp.RelationshipSetFlag)]
        public void DerivedGraph_RelationshipMutation_HardFailsWithoutChangingGraph(GraphNodeOp operation)
        {
            using var world = World.Create();
            int attributeId = AttributeRegistry.Register("tests.derived-graph.side-effect.relationship-source");
            Entity source = world.Create(CreateAttributes(attributeId, 10f));
            Entity target = world.Create();
            var typeRegistry = new RelationshipTypeRegistry();
            var metricRegistry = new RelationshipMetricRegistry();
            var flagRegistry = new RelationshipFlagRegistry();
            int typeId = typeRegistry.Register("tests.derived-graph.relationship.type");
            int metricId = metricRegistry.Register("tests.derived-graph.relationship.metric", -100, 100, 0);
            int flagId = flagRegistry.Register("tests.derived-graph.relationship.flag");
            var runtime = new RelationshipRuntime(
                world,
                typeRegistry,
                metricRegistry,
                flagRegistry,
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(),
                new RelationshipReverseIndex(world));
            var graphApi = new GasGraphRuntimeApi(world, relationshipRuntime: runtime);

            if (operation != GraphNodeOp.RelationshipEnsureLink)
            {
                runtime.EnsureLink(source, target, typeId);
            }
            if (operation is GraphNodeOp.RelationshipSetMetric or GraphNodeOp.RelationshipAddMetric)
            {
                runtime.SetMetric(source, target, typeId, metricId, 11, reasonId: 0);
            }

            AttributeBuffer staged = world.Get<AttributeBuffer>(source);
            graphApi.BeginDerivedAttributeWrites(source, in staged);
            try
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                {
                    switch (operation)
                    {
                        case GraphNodeOp.RelationshipEnsureLink:
                            graphApi.EnsureRelationshipLink(source, target, typeId);
                            break;
                        case GraphNodeOp.RelationshipRemoveLink:
                            graphApi.RemoveRelationshipLink(source, target, typeId);
                            break;
                        case GraphNodeOp.RelationshipSetMetric:
                            graphApi.SetRelationshipMetric(source, target, metricId, 42, reasonId: 0, typeId);
                            break;
                        case GraphNodeOp.RelationshipAddMetric:
                            graphApi.AddRelationshipMetric(source, target, metricId, 4, reasonId: 0, typeId);
                            break;
                        case GraphNodeOp.RelationshipSetFlag:
                            graphApi.SetRelationshipFlag(source, target, flagId, enabled: true, reasonId: 0, typeId);
                            break;
                        default:
                            throw new InvalidOperationException($"Unsupported relationship mutation test operation {operation}.");
                    }
                })!;

                Assert.That(ex.Message, Does.StartWith(IDerivedAttributeGraphRuntimeApi.SideEffectForbiddenError));
            }
            finally
            {
                graphApi.EndDerivedAttributeWrites(source, ref staged, commit: false);
            }

            Assert.That(
                runtime.HasLink(source, target, typeId),
                Is.EqualTo(operation != GraphNodeOp.RelationshipEnsureLink));
            Assert.That(runtime.GetMetric(source, target, typeId, metricId),
                Is.EqualTo(operation is GraphNodeOp.RelationshipSetMetric or GraphNodeOp.RelationshipAddMetric ? 11 : 0));
            Assert.That(runtime.HasFlag(source, target, typeId, flagId), Is.False);
        }

        [TestCase(GraphNodeOp.BeginLifecycleTransaction)]
        [TestCase(GraphNodeOp.InvokeBuiltin)]
        public void DerivedGraph_LifecycleMutation_HardFailsBeforeRuntimeDispatch(GraphNodeOp operation)
        {
            using var world = World.Create();
            int attributeId = AttributeRegistry.Register("tests.derived-graph.side-effect.lifecycle-source");
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)operation, Imm = 1 },
            };
            var programs = new GraphProgramRegistry();
            programs.Register(1, program);
            var binding = new AttributeDerivedGraphBinding();
            binding.Add(1);
            Entity entity = world.Create(
                CreateAttributes(attributeId, 10f),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags(),
                binding);
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            var graphApi = new GasGraphRuntimeApi(world, tagOps: tagOps);
            using var system = new AttributeAggregatorSystem(world, programs, graphApi, tagOps);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;

            Assert.That(ex.Message, Does.StartWith(IDerivedAttributeGraphRuntimeApi.SideEffectForbiddenError));
            Assert.That(world.Get<AttributeBuffer>(entity).GetCurrent(attributeId), Is.EqualTo(10f));
            Assert.That(tagOps.DirtyEntities.Count, Is.Zero);
            Assert.That(world.Has<AttributeAggregateDirty>(entity), Is.True);
        }

        [Test]
        public void DerivedGraph_WhenLaterSideEffectFails_DiscardsEarlierDerivedWrites()
        {
            using var world = World.Create();
            int sourceAttributeId = AttributeRegistry.Register("tests.derived-graph.rollback.source");
            int derivedAttributeId = AttributeRegistry.Register("tests.derived-graph.rollback.result");
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadSelfAttribute, Dst = 0, Imm = sourceAttributeId },
                new() { Op = (ushort)GraphNodeOp.WriteSelfAttribute, A = 0, Imm = derivedAttributeId },
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 2 },
                new() { Op = (ushort)GraphNodeOp.ApplyEffectTemplate, A = 2, Imm = 123 },
            };
            var programs = new GraphProgramRegistry();
            programs.Register(1, program);
            var binding = new AttributeDerivedGraphBinding();
            binding.Add(1);
            Entity entity = world.Create(
                CreateAttributes(sourceAttributeId, 10f),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags(),
                binding);
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            var effectRequests = new EffectRequestQueue();
            var graphApi = new GasGraphRuntimeApi(world, effectRequests: effectRequests, tagOps: tagOps);
            using var system = new AttributeAggregatorSystem(world, programs, graphApi, tagOps);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;

            Assert.That(ex.Message, Does.StartWith("GAS.GRAPH.ERR.DerivedAttributeSideEffectForbidden"));
            Assert.That(world.Get<AttributeBuffer>(entity).HasAttribute(derivedAttributeId), Is.False);
            Assert.That(world.Get<AttributeBuffer>(entity).GetCurrent(sourceAttributeId), Is.EqualTo(10f));
            Assert.That(effectRequests.Count, Is.Zero);
            Assert.That(tagOps.DirtyEntities.Count, Is.Zero);
            Assert.That(world.Has<GameplayAttributeChangedBits>(entity), Is.False);
            Assert.That(world.Has<AttributeAggregateDirty>(entity), Is.True);
        }

        [Test]
        public void DerivedGraph_BlackboardWrite_HardFailsBeforeMutation()
        {
            using var world = World.Create();
            int sourceAttributeId = AttributeRegistry.Register("tests.derived-graph.side-effect.blackboard-source");
            const int blackboardKeyId = 1;
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 2 },
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 7f },
                new() { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 2, B = 0, Imm = blackboardKeyId },
            };
            var programs = new GraphProgramRegistry();
            programs.Register(1, program);
            var binding = new AttributeDerivedGraphBinding();
            binding.Add(1);
            Entity entity = world.Create(
                CreateAttributes(sourceAttributeId, 10f),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags(),
                new BlackboardFloatBuffer(),
                binding);
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            var graphApi = new GasGraphRuntimeApi(world, tagOps: tagOps);
            using var system = new AttributeAggregatorSystem(world, programs, graphApi, tagOps);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;

            Assert.That(ex.Message, Does.StartWith("GAS.GRAPH.ERR.DerivedAttributeSideEffectForbidden"));
            Assert.That(world.Get<BlackboardFloatBuffer>(entity).TryGet(blackboardKeyId, out _), Is.False);
            Assert.That(world.Get<AttributeBuffer>(entity).GetCurrent(sourceAttributeId), Is.EqualTo(10f));
            Assert.That(tagOps.DirtyEntities.Count, Is.Zero);
            Assert.That(world.Has<GameplayAttributeChangedBits>(entity), Is.False);
            Assert.That(world.Has<AttributeAggregateDirty>(entity), Is.True);
        }

        [Test]
        public void DerivedGraphs_InOneAggregation_ReadPriorStagedWritesAndCommitOnce()
        {
            using var world = World.Create();
            int sourceAttributeId = AttributeRegistry.Register("tests.derived-graph.chained.source");
            int intermediateAttributeId = AttributeRegistry.Register("tests.derived-graph.chained.intermediate");
            int resultAttributeId = AttributeRegistry.Register("tests.derived-graph.chained.result");
            var programs = new GraphProgramRegistry();
            programs.Register(1, new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadSelfAttribute, Dst = 0, Imm = sourceAttributeId },
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 2f },
                new() { Op = (ushort)GraphNodeOp.MulFloat, Dst = 2, A = 0, B = 1 },
                new() { Op = (ushort)GraphNodeOp.WriteSelfAttribute, A = 2, Imm = intermediateAttributeId },
            });
            programs.Register(2, new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadSelfAttribute, Dst = 0, Imm = intermediateAttributeId },
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 3f },
                new() { Op = (ushort)GraphNodeOp.AddFloat, Dst = 2, A = 0, B = 1 },
                new() { Op = (ushort)GraphNodeOp.WriteSelfAttribute, A = 2, Imm = resultAttributeId },
            });
            var binding = new AttributeDerivedGraphBinding();
            binding.Add(1);
            binding.Add(2);
            Entity entity = world.Create(
                CreateAttributes(sourceAttributeId, 10f),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags(),
                binding);
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            using var system = new AttributeAggregatorSystem(
                world,
                programs,
                new GasGraphRuntimeApi(world, tagOps: tagOps),
                tagOps);

            system.Update(0f);

            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(entity);
            Assert.That(attributes.GetCurrent(intermediateAttributeId), Is.EqualTo(20f));
            Assert.That(attributes.GetCurrent(resultAttributeId), Is.EqualTo(23f));
            ref DirtyFlags dirty = ref world.Get<DirtyFlags>(entity);
            Assert.That(dirty.IsAttributeDirty(intermediateAttributeId), Is.True);
            Assert.That(dirty.IsAttributeDirty(resultAttributeId), Is.True);
            Assert.That(tagOps.DirtyEntities.Count, Is.EqualTo(1));
            Assert.That(world.Has<GameplayAttributeChangedBits>(entity), Is.True);
            ref GameplayAttributeChangedBits presentation = ref world.Get<GameplayAttributeChangedBits>(entity);
            Assert.That(presentation.IsSet(intermediateAttributeId), Is.True);
            Assert.That(presentation.IsSet(resultAttributeId), Is.True);
            Assert.That(world.Has<AttributeAggregateDirty>(entity), Is.False);
        }

        [Test]
        public void DerivedGraph_NonLinearFormula_ComputesCDMultiplier()
        {
            // Arrange: entity with AbilityHaste=50 → CDMultiplier = 1/(1+50/100) = 0.6667
            using var world = World.Create();

            int abilityHasteAttrId = 1;
            int cdMultiplierAttrId = 2;

            var entity = world.Create(
                new AttributeBuffer(),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags()
            );
            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetBase(abilityHasteAttrId, 50f);
            buf.SetCurrent(abilityHasteAttrId, 50f);

            // Build derived graph program:
            // F[0] = LoadSelfAttribute(Imm=abilityHasteAttrId)  → 50
            // F[1] = ConstFloat(100)
            // F[2] = DivFloat(F[0], F[1])                       → 0.5
            // F[3] = ConstFloat(1)
            // F[4] = AddFloat(F[3], F[2])                        → 1.5
            // F[5] = DivFloat(F[3], F[4])                        → 0.6667
            // WriteSelfAttribute(Imm=cdMultiplierAttrId, A=F[5])
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadSelfAttribute, Dst = 0, Imm = abilityHasteAttrId },
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 100f },
                new() { Op = (ushort)GraphNodeOp.DivFloat, Dst = 2, A = 0, B = 1 },
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 3, ImmF = 1f },
                new() { Op = (ushort)GraphNodeOp.AddFloat, Dst = 4, A = 3, B = 2 },
                new() { Op = (ushort)GraphNodeOp.DivFloat, Dst = 5, A = 3, B = 4 },
                new() { Op = (ushort)GraphNodeOp.WriteSelfAttribute, A = 5, Imm = cdMultiplierAttrId },
            };

            var registry = new GraphProgramRegistry();
            registry.Register(1, program);

            var binding = new AttributeDerivedGraphBinding();
            binding.Add(1);
            world.Add(entity, binding);

            // Act: run aggregator
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            using var system = new AttributeAggregatorSystem(
                world,
                registry,
                new GasGraphRuntimeApi(world, tagOps: tagOps),
                tagOps);
            system.Update(0f);

            // Assert
            ref var result = ref world.Get<AttributeBuffer>(entity);
            float cdMul = result.GetCurrent(cdMultiplierAttrId);
            That(cdMul, Is.EqualTo(1f / 1.5f).Within(0.001f),
                "CDMultiplier should be 1/(1+AH/100)");
        }

        [Test]
        public void DerivedGraph_ArmorToEHP_ComputesCorrectly()
        {
            // Arrange: HP=1000, Armor=100 → PhysicalEHP = 1000 * (1 + 100/100) = 2000
            using var world = World.Create();

            int hpAttrId = 1;
            int armorAttrId = 2;
            int physEhpAttrId = 3;

            var entity = world.Create(
                new AttributeBuffer(),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags()
            );
            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetBase(hpAttrId, 1000f);
            buf.SetCurrent(hpAttrId, 1000f);
            buf.SetBase(armorAttrId, 100f);
            buf.SetCurrent(armorAttrId, 100f);

            // Graph: PhysEHP = HP * (1 + Armor/100)
            // F[0] = LoadSelfAttribute(HP)        → 1000
            // F[1] = LoadSelfAttribute(Armor)     → 100
            // F[2] = ConstFloat(100)
            // F[3] = DivFloat(F[1], F[2])          → 1.0
            // F[4] = ConstFloat(1)
            // F[5] = AddFloat(F[4], F[3])           → 2.0
            // F[6] = MulFloat(F[0], F[5])           → 2000
            // WriteSelfAttribute(PhysEHP, F[6])
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadSelfAttribute, Dst = 0, Imm = hpAttrId },
                new() { Op = (ushort)GraphNodeOp.LoadSelfAttribute, Dst = 1, Imm = armorAttrId },
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 2, ImmF = 100f },
                new() { Op = (ushort)GraphNodeOp.DivFloat, Dst = 3, A = 1, B = 2 },
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 4, ImmF = 1f },
                new() { Op = (ushort)GraphNodeOp.AddFloat, Dst = 5, A = 4, B = 3 },
                new() { Op = (ushort)GraphNodeOp.MulFloat, Dst = 6, A = 0, B = 5 },
                new() { Op = (ushort)GraphNodeOp.WriteSelfAttribute, A = 6, Imm = physEhpAttrId },
            };

            var registry = new GraphProgramRegistry();
            registry.Register(1, program);

            var binding = new AttributeDerivedGraphBinding();
            binding.Add(1);
            world.Add(entity, binding);

            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            using var system = new AttributeAggregatorSystem(
                world,
                registry,
                new GasGraphRuntimeApi(world, tagOps: tagOps),
                tagOps);
            system.Update(0f);

            ref var result = ref world.Get<AttributeBuffer>(entity);
            That(result.GetCurrent(physEhpAttrId), Is.EqualTo(2000f).Within(0.01f),
                "PhysicalEHP should be HP * (1 + Armor/100)");
        }

        [Test]
        public void DerivedGraph_NoBinding_AggregatesNormally()
        {
            // Entity without AttributeDerivedGraphBinding should aggregate normally
            using var world = World.Create();

            var entity = world.Create(
                new AttributeBuffer(),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags()
            );
            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetBase(1, 42f);

            var registry = new GraphProgramRegistry();
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            using var system = new AttributeAggregatorSystem(
                world,
                registry,
                new GasGraphRuntimeApi(world, tagOps: tagOps),
                tagOps);
            system.Update(0f);

            ref var result = ref world.Get<AttributeBuffer>(entity);
            That(result.GetCurrent(1), Is.EqualTo(42f),
                "Without binding, base value should pass through unchanged");
        }

        [Test]
        public void DerivedGraph_DirtyFlags_IncludeDerivedChanges()
        {
            // Derived graph writes should be reflected in dirty flags
            using var world = World.Create();

            int sourceAttr = 1;
            int derivedAttr = 2;

            var entity = world.Create(
                new AttributeBuffer(),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags()
            );
            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetBase(sourceAttr, 10f);
            buf.SetCurrent(sourceAttr, 10f);
            // derivedAttr starts at 0

            // Graph: derivedAttr = sourceAttr * 2
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadSelfAttribute, Dst = 0, Imm = sourceAttr },
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 2f },
                new() { Op = (ushort)GraphNodeOp.MulFloat, Dst = 2, A = 0, B = 1 },
                new() { Op = (ushort)GraphNodeOp.WriteSelfAttribute, A = 2, Imm = derivedAttr },
            };

            var registry = new GraphProgramRegistry();
            registry.Register(1, program);

            var binding = new AttributeDerivedGraphBinding();
            binding.Add(1);
            world.Add(entity, binding);

            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            using var system = new AttributeAggregatorSystem(
                world,
                registry,
                new GasGraphRuntimeApi(world, tagOps: tagOps),
                tagOps);
            system.Update(0f);

            // Entity keeps its preinstalled DirtyFlags and records the derived change.
            That(world.Has<DirtyFlags>(entity), Is.True,
                "DirtyFlags should be added when derived attributes change");

            ref var dirty = ref world.Get<DirtyFlags>(entity);
            That(dirty.IsAttributeDirty(derivedAttr), Is.True,
                "Derived attribute should be marked dirty");
        }

        private static AttributeBuffer CreateAttributes(int attributeId, float value)
        {
            var attributes = new AttributeBuffer();
            attributes.SetBase(attributeId, value);
            attributes.SetCurrent(attributeId, value);
            return attributes;
        }

    }
}
