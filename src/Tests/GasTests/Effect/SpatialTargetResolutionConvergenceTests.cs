using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Spatial;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class SpatialTargetResolutionConvergenceTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EffectParamKeys.Initialize();
        }

        [Test]
        public void TargetResolver_CircleSearch_UsesExplicitTargetPoint_WhenTargetEntityIsMissing()
        {
            using var world = World.Create();
            var actor = world.Create(
                WorldPositionCm.FromCm(120, 180),
                new AbilityExecInstance
                {
                    TargetPosCm = Fix64Vec2.FromInt(960, 720),
                    HasTargetPos = 1
                });

            var spatial = new RecordingSpatialQueryService();
            var query = new TargetQueryDescriptor
            {
                Kind = TargetResolverKind.BuiltinSpatial,
                Spatial = new BuiltinSpatialDescriptor
                {
                    Shape = SpatialShape.Circle,
                    RadiusCm = 220
                }
            };

            Span<Entity> buffer = stackalloc Entity[8];
            int count = TargetResolverFanOutHelper.ResolveTargets(
                world,
                new EffectContext { Source = actor },
                in query,
                spatial,
                buffer.ToArray());

            That(count, Is.EqualTo(0));
            That(spatial.LastRadiusCenter, Is.EqualTo(new WorldCmInt2(960, 720)));
            That(spatial.LastRadiusCm, Is.EqualTo(220));
        }

        [Test]
        public void TargetResolver_LineSearch_UsesExplicitTargetPoint_ForDirection()
        {
            using var world = World.Create();
            var actor = world.Create(
                WorldPositionCm.FromCm(300, 300),
                new AbilityExecInstance
                {
                    TargetPosCm = Fix64Vec2.FromInt(300, 930),
                    HasTargetPos = 1
                });

            var spatial = new RecordingSpatialQueryService();
            var query = new TargetQueryDescriptor
            {
                Kind = TargetResolverKind.BuiltinSpatial,
                Spatial = new BuiltinSpatialDescriptor
                {
                    Shape = SpatialShape.Line,
                    LengthCm = 640,
                    HalfWidthCm = 80
                }
            };

            Span<Entity> buffer = stackalloc Entity[8];
            int count = TargetResolverFanOutHelper.ResolveTargets(
                world,
                new EffectContext { Source = actor },
                in query,
                spatial,
                buffer.ToArray());

            That(count, Is.EqualTo(0));
            That(spatial.LastLineOrigin, Is.EqualTo(new WorldCmInt2(300, 300)));
            That(spatial.LastLineDirectionDeg, Is.EqualTo(90));
            That(spatial.LastLineLengthCm, Is.EqualTo(640));
        }

        [Test]
        public void TargetResolver_LineSearch_ExplicitTargetPointOverridesActorFacing()
        {
            using var world = World.Create();
            var actor = world.Create(
                WorldPositionCm.FromCm(300, 300),
                new FacingDirection { AngleRad = 0f },
                new AbilityExecInstance
                {
                    TargetPosCm = Fix64Vec2.FromInt(300, 930),
                    HasTargetPos = 1
                });

            var spatial = new RecordingSpatialQueryService();
            var query = new TargetQueryDescriptor
            {
                Kind = TargetResolverKind.BuiltinSpatial,
                Spatial = new BuiltinSpatialDescriptor
                {
                    Shape = SpatialShape.Line,
                    LengthCm = 640,
                    HalfWidthCm = 80
                }
            };

            Span<Entity> buffer = stackalloc Entity[8];
            int count = TargetResolverFanOutHelper.ResolveTargets(
                world,
                new EffectContext { Source = actor },
                in query,
                spatial,
                buffer.ToArray());

            That(count, Is.EqualTo(0));
            That(spatial.LastLineOrigin, Is.EqualTo(new WorldCmInt2(300, 300)));
            That(spatial.LastLineDirectionDeg, Is.EqualTo(90));
            That(spatial.LastLineLengthCm, Is.EqualTo(640));
        }

        [Test]
        public void TargetResolver_LineSearch_UsesVectorOrigin_WhenCastStoresTwoPoints()
        {
            using var world = World.Create();
            var actor = world.Create(
                WorldPositionCm.FromCm(300, 300),
                new AbilityExecInstance
                {
                    TargetOriginPosCm = Fix64Vec2.FromInt(640, 480),
                    HasTargetOriginPos = 1,
                    TargetPosCm = Fix64Vec2.FromInt(1120, 480),
                    HasTargetPos = 1
                });

            var spatial = new RecordingSpatialQueryService();
            var query = new TargetQueryDescriptor
            {
                Kind = TargetResolverKind.BuiltinSpatial,
                Spatial = new BuiltinSpatialDescriptor
                {
                    Shape = SpatialShape.Line,
                    LengthCm = 540,
                    HalfWidthCm = 70
                }
            };

            Span<Entity> buffer = stackalloc Entity[8];
            int count = TargetResolverFanOutHelper.ResolveTargets(
                world,
                new EffectContext { Source = actor },
                in query,
                spatial,
                buffer.ToArray());

            That(count, Is.EqualTo(0));
            That(spatial.LastLineOrigin, Is.EqualTo(new WorldCmInt2(640, 480)));
            That(spatial.LastLineDirectionDeg, Is.EqualTo(0));
            That(spatial.LastLineLengthCm, Is.EqualTo(540));
        }

        [Test]
        public void TargetResolver_LineSearch_UsesPreservedCallerParams_WhenExecIsMissing()
        {
            using var world = World.Create();
            var actor = world.Create(WorldPositionCm.FromCm(1950, 900));

            var spatial = new RecordingSpatialQueryService();
            var query = new TargetQueryDescriptor
            {
                Kind = TargetResolverKind.BuiltinSpatial,
                Spatial = new BuiltinSpatialDescriptor
                {
                    Shape = SpatialShape.Line,
                    LengthCm = 780,
                    HalfWidthCm = 95
                }
            };

            var mergedParams = default(EffectConfigParams);
            mergedParams.TryAddFloat(EffectParamKeys.TargetOriginX, 1950f);
            mergedParams.TryAddFloat(EffectParamKeys.TargetOriginY, 900f);
            mergedParams.TryAddFloat(EffectParamKeys.TargetPosX, 1730f);
            mergedParams.TryAddFloat(EffectParamKeys.TargetPosY, 900f);

            Span<Entity> buffer = stackalloc Entity[8];
            int count = TargetResolverFanOutHelper.ResolveTargets(
                world,
                new EffectContext { Source = actor },
                in query,
                in mergedParams,
                spatial,
                buffer.ToArray());

            That(count, Is.EqualTo(0));
            That(spatial.LastLineOrigin, Is.EqualTo(new WorldCmInt2(1950, 900)));
            That(spatial.LastLineDirectionDeg, Is.EqualTo(180));
            That(spatial.LastLineLengthCm, Is.EqualTo(780));
        }

        [Test]
        public void AbilityExecSystem_SpatialListCast_UsesFirstPointAsVectorOrigin_AndLastPointAsTargetPosition()
        {
            using var world = World.Create();
            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardSpatialBuffer(),
                new BlackboardEntityBuffer(),
                new BlackboardIntBuffer(),
                new AbilityStateBuffer());

            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(9001);

            var order = new Order
            {
                OrderId = 7,
                Actor = actor,
                OrderTypeId = 100,
                Args = new OrderArgs { I0 = 0 }
            };

            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            orderBuffer.SetActiveDirect(in order, priority: 100);

            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            bbI.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            ref var bbSpatial = ref world.Get<BlackboardSpatialBuffer>(actor);
            bbSpatial.AppendPoint(OrderBlackboardKeys.Cast_TargetPosition, new Vector3(240f, 0f, 360f));
            bbSpatial.AppendPoint(OrderBlackboardKeys.Cast_TargetPosition, new Vector3(840f, 0f, 1260f));

            var defs = new AbilityDefinitionRegistry();
            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.EventGate, tick: 1, tagId: 5001);
            spec.SetItem(1, ExecItemKind.End, tick: 2);
            defs.Register(9001, new AbilityDefinition { ExecSpec = spec });

            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new EffectRequestQueue(),
                4096,
                defs,
                castAbilityOrderTypeId: 100,
                orderTypeRegistry: new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity)),
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

            system.Update(0f);

            That(world.Has<AbilityExecInstance>(actor), Is.True);
            ref var exec = ref world.Get<AbilityExecInstance>(actor);
            That(exec.HasTargetOriginPos, Is.EqualTo(1));
            That(exec.TargetOriginPosCm, Is.EqualTo(Fix64Vec2.FromInt(240, 360)));
            That(exec.HasTargetPos, Is.EqualTo(1));
            That(exec.TargetPosCm, Is.EqualTo(Fix64Vec2.FromInt(840, 1260)));
        }

        private sealed class RecordingSpatialQueryService : ISpatialQueryService
        {
            public WorldCmInt2 LastRadiusCenter { get; private set; }
            public int LastRadiusCm { get; private set; }
            public WorldCmInt2 LastLineOrigin { get; private set; }
            public int LastLineDirectionDeg { get; private set; }
            public int LastLineLengthCm { get; private set; }

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer) => default;

            public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer)
            {
                LastRadiusCenter = center;
                LastRadiusCm = radiusCm;
                return default;
            }

            public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer) => default;

            public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => default;

            public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer)
            {
                LastLineOrigin = origin;
                LastLineDirectionDeg = directionDeg;
                LastLineLengthCm = lengthCm;
                return default;
            }

            public SpatialQueryResult QueryHexRange(Ludots.Core.Map.Hex.HexCoordinates center, int hexRadius, Span<Entity> buffer) => default;

            public SpatialQueryResult QueryHexRing(Ludots.Core.Map.Hex.HexCoordinates center, int hexRadius, Span<Entity> buffer) => default;
        }
    }
}
