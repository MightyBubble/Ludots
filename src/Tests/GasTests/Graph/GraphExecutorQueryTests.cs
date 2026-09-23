using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphExecutorQueryTests
    {
        [Test]
        public void Execute_UsesExplicitSubjectAndCallerOwnedTargetBuffer()
        {
            using World world = World.Create();
            Entity subject = world.Create();
            var programs = new GraphProgramRegistry();
            programs.Register(1, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadCaster },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt }
            }, GraphKind.Query);

            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];

            int count = GraphExecutor.ExecuteQuery(
                programs,
                1,
                world,
                subject,
                targets,
                new MinimalGraphRuntimeApi(),
                floats,
                ints,
                bools,
                entities,
                callStack,
                targetPosCm: IntVector2.Zero);

            Assert.That(count, Is.EqualTo(0));
            Assert.That(entities[0], Is.EqualTo(subject));
        }

        [Test]
        public void Execute_ReturnsQueryTargetsInCallerOwnedBuffer()
        {
            using World world = World.Create();
            Entity subject = world.Create();
            Entity first = world.Create();
            Entity second = world.Create();
            var programs = new GraphProgramRegistry();
            programs.Register(1, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.QueryRadius, ImmF = 500f, Flags = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt }
            }, GraphKind.Query);

            var api = new MinimalGraphRuntimeApi
            {
                RadiusTargets = new[] { first, second }
            };
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];

            int count = GraphExecutor.ExecuteQuery(
                programs,
                1,
                world,
                subject,
                targets,
                api,
                floats,
                ints,
                bools,
                entities,
                callStack);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(targets[0], Is.EqualTo(first));
            Assert.That(targets[1], Is.EqualTo(second));
        }

        [Test]
        public void QueryAuthorableOperationsArePure()
        {
            GasGraphOpHandlerTable handlers = GasGraphOpHandlerTable.Instance;
            foreach (GraphNodeOp op in GraphOpDescriptorTable.EnumerateAuthorable(GraphKind.Query))
            {
                Assert.That(
                    handlers.TryGetOperationMetadata(op, out EffectOperationMetadata metadata),
                    Is.True,
                    $"Query opcode {op} must have operation metadata.");
                Assert.That(
                    metadata.Kind,
                    Is.EqualTo(EffectOperationKind.Pure),
                    $"Query opcode {op} is authorable but classified as {metadata.Kind}.");
            }
        }

        [Test]
        public void Execute_RejectsNullSubjectBeforeRunning()
        {
            using World world = World.Create();
            var programs = new GraphProgramRegistry();
            programs.Register(1, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt }
            }, GraphKind.Query);

            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];

            Assert.That(
                () => GraphExecutor.ExecuteQuery(
                    programs,
                    1,
                    world,
                    Entity.Null,
                    targets,
                    new MinimalGraphRuntimeApi(),
                    floats,
                    ints,
                    bools,
                    entities,
                    callStack),
                Throws.ArgumentException);
        }

        [Test]
        public void Execute_RejectsNonQueryGraphWithoutRunning()
        {
            using World world = World.Create();
            Entity subject = world.Create();
            var programs = new GraphProgramRegistry();
            programs.Register(1, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt }
            }, GraphKind.Script);

            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];

            Assert.That(
                () => GraphExecutor.ExecuteQuery(
                    programs,
                    1,
                    world,
                    subject,
                    targets,
                    new MinimalGraphRuntimeApi(),
                    floats,
                    ints,
                    bools,
                    entities,
                    callStack),
                Throws.InvalidOperationException);
        }

        [Test]
        public void Execute_RejectsStaleSubjectBeforeRunning()
        {
            using World world = World.Create();
            Entity subject = world.Create();
            world.Destroy(subject);
            var programs = new GraphProgramRegistry();
            programs.Register(1, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt }
            }, GraphKind.Query);

            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];

            Assert.That(
                () => GraphExecutor.ExecuteQuery(
                    programs,
                    1,
                    world,
                    subject,
                    targets,
                    new MinimalGraphRuntimeApi(),
                    floats,
                    ints,
                    bools,
                    entities,
                    callStack),
                Throws.InvalidOperationException);
        }

        [Test]
        public void QueryRegistrationRejectsKnownSideEffectOpcode()
        {
            var programs = new GraphProgramRegistry();
            Assert.That(
                () => programs.Register(1, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ShowPanel },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt }
                }, GraphKind.Query),
                Throws.InvalidOperationException);
        }

        private sealed class MinimalGraphRuntimeApi : IGraphRuntimeApi
        {
            public Entity[] RadiusTargets { get; init; } = Array.Empty<Entity>();

            public bool TryGetGridPos(Entity entity, out IntVector2 gridPos) { gridPos = default; return false; }
            public bool HasTag(Entity entity, int tagId) => false;
            public bool TryGetAttributeCurrent(Entity entity, int attributeId, out float value) { value = 0f; return false; }
            public SpatialQueryResult QueryRadius(IntVector2 centerCm, float radiusCm, Span<Entity> buffer)
            {
                RadiusTargets.AsSpan().CopyTo(buffer);
                return new SpatialQueryResult(RadiusTargets.Length, 0);
            }
            public SpatialQueryResult QueryCone(IntVector2 originCm, int directionDeg, int halfAngleDeg, float rangeCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryRectangle(IntVector2 centerCm, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryLine(IntVector2 originCm, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => default;
            public void SetWorldPosition(Entity target, int xCm, int yCm) { }
            public void SpawnTemplate(int templateKeyId, Entity source, float xCm, float yCm, bool hasPosition) { }
            public SpatialQueryResult QueryHexRange(IntVector2 centerCm, int hexRadius, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexRing(IntVector2 centerCm, int hexRadius, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexNeighbors(IntVector2 centerCm, Span<Entity> buffer) => default;
            public int GetTeamId(Entity entity) => 0;
            public uint GetEntityLayerCategory(Entity entity) => 0;
            public int GetRelationship(int teamA, int teamB) => GraphRelationship.Neutral;
            public void ApplyEffectTemplate(Entity caster, Entity target, int templateId) { }
            public void ApplyEffectTemplate(Entity caster, Entity target, int templateId, in EffectArgs args) { }
            public void RemoveEffectTemplate(Entity target, int templateId) { }
            public void ModifyAttributeAdd(Entity caster, Entity target, int attributeId, float delta) { }
            public void ModifyAttributeSet(Entity caster, Entity target, int attributeId, float value) { }
            public void SendEvent(Entity caster, Entity target, int eventTagId, float magnitude) { }
            public bool TryReadBlackboardFloat(Entity entity, int keyId, out float value) { value = 0f; return false; }
            public bool TryReadBlackboardInt(Entity entity, int keyId, out int value) { value = 0; return false; }
            public bool TryReadBlackboardEntity(Entity entity, int keyId, out Entity value) { value = Entity.Null; return false; }
            public void WriteBlackboardFloat(Entity entity, int keyId, float value) { }
            public void WriteBlackboardInt(Entity entity, int keyId, int value) { }
            public void WriteBlackboardEntity(Entity entity, int keyId, Entity value) { }
            public bool TryLoadConfigFloat(int keyId, out float value) { value = 0f; return false; }
            public bool TryLoadConfigInt(int keyId, out int value) { value = 0; return false; }
        }
    }
}
