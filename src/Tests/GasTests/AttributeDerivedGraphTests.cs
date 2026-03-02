using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class AttributeDerivedGraphTests
    {
        [Test]
        public void DerivedGraph_NonLinearFormula_ComputesCDMultiplier()
        {
            // Arrange: entity with AbilityHaste=50 → CDMultiplier = 1/(1+50/100) = 0.6667
            using var world = World.Create();

            int abilityHasteAttrId = 1;
            int cdMultiplierAttrId = 2;

            var entity = world.Create(
                new AttributeBuffer(),
                new ActiveEffectContainer()
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
            var mockApi = new MinimalGraphApi(world);
            var system = new AttributeAggregatorSystem(world, registry, mockApi);
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
                new ActiveEffectContainer()
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

            var mockApi = new MinimalGraphApi(world);
            var system = new AttributeAggregatorSystem(world, registry, mockApi);
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
                new ActiveEffectContainer()
            );
            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetBase(1, 42f);

            var registry = new GraphProgramRegistry();
            var mockApi = new MinimalGraphApi(world);
            var system = new AttributeAggregatorSystem(world, registry, mockApi);
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
                new ActiveEffectContainer()
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

            var mockApi = new MinimalGraphApi(world);
            var system = new AttributeAggregatorSystem(world, registry, mockApi);
            system.Update(0f);

            // Entity should have DirtyFlags added (from WithoutDirtyJob path)
            That(world.Has<DirtyFlags>(entity), Is.True,
                "DirtyFlags should be added when derived attributes change");

            ref var dirty = ref world.Get<DirtyFlags>(entity);
            That(dirty.IsAttributeDirty(derivedAttr), Is.True,
                "Derived attribute should be marked dirty");
        }

        /// <summary>
        /// Minimal IGraphRuntimeApi for testing — only supports attribute reading.
        /// </summary>
        private class MinimalGraphApi : IGraphRuntimeApi
        {
            private readonly World _world;
            public MinimalGraphApi(World world) => _world = world;

            public bool TryGetAttributeCurrent(Arch.Core.Entity entity, int attributeId, out float value)
            {
                if (_world.IsAlive(entity) && _world.Has<AttributeBuffer>(entity))
                {
                    ref var buf = ref _world.Get<AttributeBuffer>(entity);
                    value = buf.GetCurrent(attributeId);
                    return true;
                }
                value = 0f;
                return false;
            }

            // Stubs for unused methods
            public bool TryGetGridPos(Arch.Core.Entity entity, out Ludots.Core.Mathematics.IntVector2 gridPos) { gridPos = default; return false; }
            public bool HasTag(Arch.Core.Entity entity, int tagId) => false;
            public int QueryRadius(Ludots.Core.Mathematics.IntVector2 center, float radius, System.Span<Arch.Core.Entity> buffer) => 0;
            public int QueryCone(Ludots.Core.Mathematics.IntVector2 origin, int directionDeg, int halfAngleDeg, float rangeCm, System.Span<Arch.Core.Entity> buffer) => 0;
            public int QueryRectangle(Ludots.Core.Mathematics.IntVector2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, System.Span<Arch.Core.Entity> buffer) => 0;
            public int QueryLine(Ludots.Core.Mathematics.IntVector2 origin, int directionDeg, int lengthCm, int halfWidthCm, System.Span<Arch.Core.Entity> buffer) => 0;
            public int QueryHexRange(Ludots.Core.Mathematics.IntVector2 center, int hexRadius, System.Span<Arch.Core.Entity> buffer) => 0;
            public int QueryHexRing(Ludots.Core.Mathematics.IntVector2 center, int hexRadius, System.Span<Arch.Core.Entity> buffer) => 0;
            public int QueryHexNeighbors(Ludots.Core.Mathematics.IntVector2 center, System.Span<Arch.Core.Entity> buffer) => 0;
            public int GetTeamId(Arch.Core.Entity entity) => 0;
            public uint GetEntityLayerCategory(Arch.Core.Entity entity) => 0;
            public int GetRelationship(int teamA, int teamB) => 0;
            public void ApplyEffectTemplate(Arch.Core.Entity caster, Arch.Core.Entity target, int templateId) { }
            public void ApplyEffectTemplate(Arch.Core.Entity caster, Arch.Core.Entity target, int templateId, in EffectArgs args) { }
            public void ModifyAttributeAdd(Arch.Core.Entity caster, Arch.Core.Entity target, int attributeId, float delta) { }
            public void SendEvent(Arch.Core.Entity caster, Arch.Core.Entity target, int eventTagId, float magnitude) { }
            public bool TryReadBlackboardFloat(Arch.Core.Entity entity, int keyId, out float value) { value = 0f; return false; }
            public bool TryReadBlackboardInt(Arch.Core.Entity entity, int keyId, out int value) { value = 0; return false; }
            public bool TryReadBlackboardEntity(Arch.Core.Entity entity, int keyId, out Arch.Core.Entity value) { value = default; return false; }
            public void WriteBlackboardFloat(Arch.Core.Entity entity, int keyId, float value) { }
            public void WriteBlackboardInt(Arch.Core.Entity entity, int keyId, int value) { }
            public void WriteBlackboardEntity(Arch.Core.Entity entity, int keyId, Arch.Core.Entity value) { }
            public bool TryLoadConfigFloat(int keyId, out float value) { value = 0f; return false; }
            public bool TryLoadConfigInt(int keyId, out int value) { value = 0; return false; }
        }
    }
}
