using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Spatial;
using NUnit.Framework;
using GraphInstruction = Ludots.Core.GraphRuntime.GraphInstruction;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ContextScoredResolverTests
    {
        [Test]
        public void ContextScoredMode_ResolvesBestCandidateAndSubmitsConcreteCastOrder()
        {
            using var world = World.Create();

            const int rootAbilityId = 1000;
            const int lightAbilityId = 1001;
            const int finisherAbilityId = 1002;
            const int downedTagId = 17;
            const int scoreGraphId = 200;

            var actor = world.Create();
            var abilities = new AbilityStateBuffer();
            abilities.AddAbility(rootAbilityId);
            abilities.AddAbility(lightAbilityId);
            abilities.AddAbility(finisherAbilityId);
            world.Add(actor, abilities);
            world.Add(actor, WorldPositionCm.FromCm(0, 0));
            world.Add(actor, new FacingDirection { AngleRad = 0f });

            var targetNormal = world.Create();
            world.Add(targetNormal, WorldPositionCm.FromCm(90, 0));
            world.Add(targetNormal, new GameplayTagContainer());

            var targetDowned = world.Create();
            world.Add(targetDowned, WorldPositionCm.FromCm(180, 10));
            world.Add(targetDowned, new GameplayTagContainer());
            ref var downedTags = ref world.Get<GameplayTagContainer>(targetDowned);
            downedTags.AddTag(downedTagId);

            var contextGroups = new ContextGroupRegistry();
            contextGroups.Register(
                groupId: 1,
                rootAbilityId: rootAbilityId,
                new ContextGroupDefinition(
                    searchRadiusCm: 300,
                    new[]
                    {
                        new ContextGroupCandidate(
                            abilityId: lightAbilityId,
                            preconditionGraphId: 0,
                            scoreGraphId: 0,
                            basePriority: 10f,
                            maxDistanceCm: 250,
                            distanceWeight: 0f,
                            maxAngleDeg: 120,
                            angleWeight: 0f,
                            hoveredBiasScore: 0f,
                            requiresTarget: true),
                        new ContextGroupCandidate(
                            abilityId: finisherAbilityId,
                            preconditionGraphId: 0,
                            scoreGraphId: scoreGraphId,
                            basePriority: 0f,
                            maxDistanceCm: 250,
                            distanceWeight: 0f,
                            maxAngleDeg: 120,
                            angleWeight: 0f,
                            hoveredBiasScore: 0f,
                            requiresTarget: true),
                    }));

            var graphPrograms = new GraphProgramRegistry();
            graphPrograms.Register(scoreGraphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 2 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HasTag, Dst = 0, A = 2, Imm = downedTagId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.JumpIfFalse, A = 0, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 25f },
            });

            var resolver = new ContextScoredOrderResolver(
                world,
                contextGroups,
                graphPrograms,
                new StubSpatialQueryService(targetNormal, targetDowned),
                new StubGraphApi(world));

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var mapping = new InputOrderMappingSystem(input, new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.ContextScored,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Attack",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 0 },
                        RequireSelection = false,
                        SelectionType = OrderSelectionType.Entity,
                        IsSkillMapping = true,
                    }
                }
            });

            var orders = new List<Ludots.Core.Gameplay.GAS.Orders.Order>();
            mapping.SetLocalPlayer(actor, 1);
            mapping.SetOrderTypeKeyResolver(key => key == "castAbility" ? 100 : 0);
            mapping.SetHoveredEntityProvider((out Entity entity) =>
            {
                entity = targetNormal;
                return true;
            });
            mapping.SetContextScoredProvider(resolver.TryResolve);
            mapping.SetOrderSubmitHandler((in Ludots.Core.Gameplay.GAS.Orders.Order order) => orders.Add(order));

            input.InjectButtonPress("Attack");
            input.Update();
            mapping.Update(0f);

            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].Args.I0, Is.EqualTo(2), "ContextScored should resolve to the finisher slot.");
            Assert.That(orders[0].Target, Is.EqualTo(targetDowned));
        }

        private static InputConfigRoot CreateInputConfig()
        {
            return new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Attack", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 1,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Attack", Path = "<Keyboard>/a", Processors = new() },
                        }
                    }
                }
            };
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class StubSpatialQueryService : ISpatialQueryService
        {
            private readonly Entity[] _entities;

            public StubSpatialQueryService(params Entity[] entities)
            {
                _entities = entities;
            }

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryHexRange(Ludots.Core.Map.Hex.HexCoordinates center, int hexRadius, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryHexRing(Ludots.Core.Map.Hex.HexCoordinates center, int hexRadius, Span<Entity> buffer) => Write(buffer);

            private SpatialQueryResult Write(Span<Entity> buffer)
            {
                int count = 0;
                for (int i = 0; i < _entities.Length && i < buffer.Length; i++)
                {
                    buffer[count++] = _entities[i];
                }

                return new SpatialQueryResult(count, 0);
            }
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
            {
                return _world.TryGet(entity, out GameplayTagContainer tags) && tags.HasTag(tagId);
            }

            public bool TryGetAttributeCurrent(Entity entity, int attributeId, out float value)
            {
                if (_world.TryGet(entity, out AttributeBuffer buffer))
                {
                    value = buffer.GetCurrent(attributeId);
                    return true;
                }

                value = 0f;
                return false;
            }

            public int QueryRadius(IntVector2 center, float radius, Span<Entity> buffer) => 0;
            public int QueryCone(IntVector2 origin, int directionDeg, int halfAngleDeg, float rangeCm, Span<Entity> buffer) => 0;
            public int QueryRectangle(IntVector2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => 0;
            public int QueryLine(IntVector2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => 0;
            public int QueryHexRange(IntVector2 center, int hexRadius, Span<Entity> buffer) => 0;
            public int QueryHexRing(IntVector2 center, int hexRadius, Span<Entity> buffer) => 0;
            public int QueryHexNeighbors(IntVector2 center, Span<Entity> buffer) => 0;
            public int GetTeamId(Entity entity) => 0;
            public uint GetEntityLayerCategory(Entity entity) => 0;
            public int GetRelationship(int teamA, int teamB) => 0;
            public void ApplyEffectTemplate(Entity caster, Entity target, int templateId) { }
            public void ApplyEffectTemplate(Entity caster, Entity target, int templateId, in EffectArgs args) { }
            public void ModifyAttributeAdd(Entity caster, Entity target, int attributeId, float delta) { }
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
