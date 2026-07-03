using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Placement;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using GasGraphExecutor = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class PlacementValidationTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EffectParamKeys.Initialize();
        }

        [Test]
        public void ClampToRange_InsideRange_DoesNotMoveTarget()
        {
            var origin = Fix64Vec2.FromInt(0, 0);
            var target = Fix64Vec2.FromInt(300, 400);
            bool inRange = PlacementValidation.ClampToRange(
                in origin,
                ref target,
                Fix64.FromInt(500),
                out bool clampedInRange);
            That(inRange, Is.True);
            That(clampedInRange, Is.True);
            That(target, Is.EqualTo(Fix64Vec2.FromInt(300, 400)));
        }

        [Test]
        public void ClampToRange_OutsideRange_ClampsToCircleEdge()
        {
            var origin = Fix64Vec2.Zero;
            var target = Fix64Vec2.FromInt(1000, 0);
            bool inRange = PlacementValidation.ClampToRange(
                in origin,
                ref target,
                Fix64.FromInt(500),
                out bool clampedInRange);
            That(inRange, Is.False);
            That(clampedInRange, Is.False);
            That(target.X.ToFloat(), Is.EqualTo(500f).Within(0.01f));
            That(target.Y, Is.EqualTo(Fix64.Zero));
        }

        [Test]
        public void IsPointInCircle_RespectsRadius()
        {
            var center = Fix64Vec2.FromInt(100, 100);
            var inside = Fix64Vec2.FromInt(120, 100);
            var outside = Fix64Vec2.FromInt(300, 100);
            That(PlacementValidation.IsPointInCircle(in inside, in center, Fix64.FromInt(50)), Is.True);
            That(PlacementValidation.IsPointInCircle(in outside, in center, Fix64.FromInt(50)), Is.False);
        }

        [Test]
        public void EffectTargetPointResolver_UsesCallerParamsFirst()
        {
            using var world = World.Create();
            var source = world.Create();
            var context = new EffectContext { Source = source };
            var merged = new EffectConfigParams();
            merged.TryAddFloat(EffectParamKeys.TargetPosX, 420f);
            merged.TryAddFloat(EffectParamKeys.TargetPosY, 180f);

            bool resolved = EffectTargetPointResolver.TryResolve(world, in context, in merged, out Fix64Vec2 point);
            That(resolved, Is.True);
            That(point, Is.EqualTo(Fix64Vec2.FromInt(420, 180)));
        }

        [Test]
        public void PlacementPhaseTargetPosResolver_RoundsResolvedPoint()
        {
            using var world = World.Create();
            var context = new EffectContext();
            var merged = new EffectConfigParams();
            merged.TryAddFloat(EffectParamKeys.TargetPosX, 420.6f);
            merged.TryAddFloat(EffectParamKeys.TargetPosY, 180.4f);

            IntVector2 targetPos = PlacementPhaseTargetPosResolver.Resolve(world, in context, in merged);
            That(targetPos, Is.EqualTo(new IntVector2(420, 180)));
        }

        [Test]
        public void GraphOps_ClampTargetToRange_UpdatesTargetPosAndBool()
        {
            using var world = World.Create();
            var caster = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var api = new GasGraphRuntimeApi(world, null, null, null);
            var program = new[]
            {
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstFloat,
                    Dst = 1,
                    ImmF = 500f,
                },
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ClampTargetToRange,
                    A = 0,
                    B = 1,
                    Dst = 0,
                },
            };

            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            e[0] = caster;
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = Entity.Null,
                TargetPos = new IntVector2(1000, 0),
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            That(state.TargetPos.X, Is.EqualTo(500));
            That(state.B[0], Is.EqualTo(0));
        }

        [Test]
        public void ExecuteValidation_ClampRangeGraph_RejectsOutOfRangeTarget()
        {
            using var world = World.Create();
            var caster = world.Create(new WorldPositionCm { Value = Fix64Vec2.Zero });
            var api = new GasGraphRuntimeApi(world, null, null, null);
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 500f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ClampTargetToRange, A = 0, B = 1, Dst = 0 },
            };

            bool passedNear = GasGraphExecutor.ExecuteValidation(
                world,
                caster,
                Entity.Null,
                new IntVector2(400, 0),
                program,
                api);
            bool passedFar = GasGraphExecutor.ExecuteValidation(
                world,
                caster,
                Entity.Null,
                new IntVector2(1000, 0),
                program,
                api);

            That(passedNear, Is.True);
            That(passedFar, Is.False);
        }
    }
}
