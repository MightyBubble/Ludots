using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Systems;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Features.GraphRuntime
{
    [TestFixture]
    public sealed class GraphScoreRuntimeTests
    {
        [Test]
        public void GraphScoreRuntime_BudgetCountsExecutedInstructions_NotRegisteredProgramLength()
        {
            using var world = World.Create();
            Entity actor = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity target = world.Create(WorldPositionCm.FromCm(100, 0), new GameplayTagContainer());

            var programs = new GraphProgramRegistry();
            const int graphId = 77;
            programs.Register(graphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.JumpIfFalse, A = 0, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 99f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 4f },
            }, GraphKind.Score);

            IReadOnlyGraphScorer scorer = CompiledGraphScoreRuntime.Compile(world, new StubGraphApi(world), programs);
            var budget = GraphInstructionBudget.Create(maxInstructions: 3);

            bool ok = scorer.TryEvaluateScore(actor, target, default, graphId, ref budget, out float score, out var failure);

            Assert.That(ok, Is.True);
            Assert.That(failure, Is.EqualTo(GraphScoreFailureReason.None));
            Assert.That(score, Is.EqualTo(4f));
            Assert.That(budget.ConsumedInstructions, Is.EqualTo(3));
        }

        [Test]
        public void GraphScoreRuntime_BudgetExhaustionDoesNotReturnPartialScore()
        {
            using var world = World.Create();
            Entity actor = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity target = world.Create(WorldPositionCm.FromCm(100, 0), new GameplayTagContainer());

            var programs = new GraphProgramRegistry();
            const int graphId = 78;
            programs.Register(graphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 1f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 5f },
            }, GraphKind.Score);

            IReadOnlyGraphScorer scorer = CompiledGraphScoreRuntime.Compile(world, new StubGraphApi(world), programs);
            var budget = GraphInstructionBudget.Create(maxInstructions: 1);

            bool ok = scorer.TryEvaluateScore(actor, target, default, graphId, ref budget, out float score, out var failure);

            Assert.That(ok, Is.False);
            Assert.That(failure, Is.EqualTo(GraphScoreFailureReason.BudgetExhausted));
            Assert.That(score, Is.EqualTo(0f));
            Assert.That(budget.ConsumedInstructions, Is.EqualTo(1));
        }

        [Test]
        public void UtilityAiDecisionSystem_GraphScoreBudgetExhaustionDoesNotSubmitOrder()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var admissionResults = new OrderAdmissionResultBuffer(16, 16);
            var orders = new OrderQueue(16, admissionResults);

            AbilityIdRegistry.Clear();
            int attackAbilityId = AbilityIdRegistry.Register("Ability.Test.Attack");
            var abilities = new AbilityDefinitionRegistry();
            abilities.Register(attackAbilityId, new AbilityDefinition());

            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            GameplayTagContainer noTags = default;

            const int scoreGraphId = 91;
            var programs = new GraphProgramRegistry();
            programs.Register(scoreGraphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 1f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 10f },
            }, GraphKind.Score);
            IReadOnlyGraphScorer scorer = CompiledGraphScoreRuntime.Compile(world, new StubGraphApi(world), programs);

            var runtime = new UtilityAiCompiledRuntime(
                new[] { new UtilityAiProfileDefinition(0, 1, 1, 16, -1) },
                new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
                new[]
                {
                    new UtilityAiDecisionDefinition(0, 0, 1, 0, 1, 10, 1f, 1f, 0f, 0, 0, attackAbilityId, 0, 0, UtilityAiDecisionFlags.Autocast | UtilityAiDecisionFlags.RequiresTarget)
                },
                new[] { new UtilityAiConsiderationDefinition(0, 0, 0, 1f, UtilityAiAggregateMode.Multiply) },
                new[] { new UtilityAiTargetFilterDefinition(0, 2, 16) },
                new[]
                {
                    new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.SpatialRadius, 1500, 0, RelationshipFilter.All, in noTags),
                    new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.Relationship, 0, 0, RelationshipFilter.Hostile, in noTags)
                },
                new[] { new UtilityAiInputDefinition(UtilityAiInputKind.GraphScore, 0, scoreGraphId) },
                new[] { new UtilityAiNormalizationDefinition(UtilityAiNormalizationKind.Identity, 0f, 1f) },
                new[] { new UtilityAiCurveDefinition(UtilityAiCurveKind.Linear, 1f) },
                new[] { new UtilityAiTaskDefinition(UtilityAiTaskKind.SubmitOrder, 102, attackAbilityId, 0, (int)OrderSubmitMode.Immediate, 0, -1, 0) },
                System.Array.Empty<UtilityAiStanceDefinition>(),
                System.Array.Empty<UtilityAiActuatorDefinition>());

            var partition = new ChunkedGridSpatialPartitionWorld(64);
            var spec = new WorldSizeSpec(new WorldAabbCm(-5000, -5000, 10000, 10000), 100);
            var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
            spatial.SetPositionProvider(entity => world.Get<WorldPositionCm>(entity).ToWorldCmInt2());

            Entity actor = world.Create(
                new UtilityAiAgent { ProfileId = 0 },
                new UtilityAiState { CurrentDecisionId = -1, NextThinkStep = 0 },
                new UtilityAiDecisionTrace(),
                new UtilityAiCombatMemory(),
                new OrderBuffer { ActiveIndex = -1 },
                new AbilityStateBuffer(),
                new Team { Id = 1 },
                WorldPositionCm.FromCm(0, 0));
            ref var actorAbilities = ref world.Get<AbilityStateBuffer>(actor);
            actorAbilities.AddAbility(attackAbilityId);
            partition.Add(actor, 0, 0);

            Entity hostile = world.Create(new Team { Id = 2 }, WorldPositionCm.FromCm(500, 0), new OrderBuffer { ActiveIndex = -1 });
            partition.Add(hostile, 5, 0);

            var decision = new UtilityAiDecisionSystem(
                world,
                clock,
                runtime,
                spatial,
                abilities,
                scorer,
                graphScoreInstructionBudgetPerThink: 1,
                orders: orders);

            decision.Update(1f / 60f);

            Assert.That(orders.Count, Is.EqualTo(0));
            Assert.That(
                world.Get<UtilityAiDecisionTrace>(actor).LastFilterRejectReason,
                Is.EqualTo((int)UtilityAiFilterRejectReason.BudgetExhausted));
        }

        private sealed class StubGraphApi : IGraphRuntimeApi
        {
            private readonly World _world;

            public StubGraphApi(World world)
            {
                _world = world;
            }

            public bool TryGetGridPos(Entity entity, out IntVector2 gridPos)
            {
                if (_world.TryGet(entity, out WorldPositionCm position))
                {
                    var worldCm = position.Value.ToWorldCmInt2();
                    gridPos = new IntVector2(worldCm.X, worldCm.Y);
                    return true;
                }

                gridPos = default;
                return false;
            }

            public bool HasTag(Entity entity, int tagId)
                => _world.TryGet(entity, out GameplayTagContainer tags) && tags.HasTag(tagId);

            public bool TryGetAttributeCurrent(Entity entity, int attributeId, out float value)
            {
                value = 0f;
                return false;
            }

            public SpatialQueryResult QueryRadius(IntVector2 center, float radius, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryCone(IntVector2 origin, int directionDeg, int halfAngleDeg, float rangeCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryRectangle(IntVector2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryLine(IntVector2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexRange(IntVector2 center, int hexRadius, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexRing(IntVector2 center, int hexRadius, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexNeighbors(IntVector2 center, Span<Entity> buffer) => default;
            public int GetTeamId(Entity entity) => 0;
            public uint GetEntityLayerCategory(Entity entity) => 0;
            public int GetRelationship(int teamA, int teamB) => 0;
            public void ApplyEffectTemplate(Entity caster, Entity target, int templateId) { }
            public void ApplyEffectTemplate(Entity caster, Entity target, int templateId, in EffectArgs args) { }
            public void RemoveEffectTemplate(Entity target, int templateId) { }
            public void ModifyAttributeAdd(Entity caster, Entity target, int attributeId, float delta) { }
            public void ModifyAttributeSet(Entity caster, Entity target, int attributeId, float value) { }
            public void SendEvent(Entity caster, Entity target, int eventTagId, float magnitude) { }
            public bool TryReadBlackboardFloat(Entity entity, int keyId, out float value) { value = 0f; return false; }
            public bool TryReadBlackboardInt(Entity entity, int keyId, out int value) { value = 0; return false; }
            public bool TryReadBlackboardEntity(Entity entity, int keyId, out Entity value) { value = default; return false; }
            public void WriteBlackboardFloat(Entity entity, int keyId, float value) { }
            public void WriteBlackboardInt(Entity entity, int keyId, int value) { }
            public void WriteBlackboardEntity(Entity entity, int keyId, Entity value) { }
            public bool TryLoadConfigFloat(int keyId, out float value) { value = 0f; return false; }
            public bool TryLoadConfigInt(int keyId, out int value) { value = 0; return false; }
        }
    }
}
