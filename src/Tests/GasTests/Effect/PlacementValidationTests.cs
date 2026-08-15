using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Placement;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphQuery;
using Ludots.Core.Navigation.GraphWorld;
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
                TargetPosCm = new IntVector2(1000, 0),
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
            CallStack = new int[Ludots.Core.NodeLibraries.GASGraph.GraphVmLimits.MaxCallStackDepth],
            CallStackCount = 0,
        };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            That(state.TargetPosCm.X, Is.EqualTo(500));
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
                api,
                GraphKind.Validation,
                new GasGraphOpHandlerTable());
            bool passedFar = GasGraphExecutor.ExecuteValidation(
                world,
                caster,
                Entity.Null,
                new IntVector2(1000, 0),
                program,
                api,
                GraphKind.Validation,
                new GasGraphOpHandlerTable());

            That(passedNear, Is.True);
            That(passedFar, Is.False);
        }

        [Test]
        public void TrySnapToNearestInCollection_ScansPastFirstCopyWindow()
        {
            using var world = World.Create();
            var keyRegistry = new Ludots.Core.Registry.StringIntRegistry();
            var collections = new EntityCollectionStore(keyRegistry);
            var owner = world.Create();
            const string collectionKey = "test.placement.snap.large";
            int collectionKeyId = keyRegistry.Register(collectionKey);
            var entities = new Entity[65];
            for (int i = 0; i < entities.Length; i++)
            {
                entities[i] = world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(10000 + i, 0) });
            }

            entities[64] = world.Create(new WorldPositionCm { Value = Fix64Vec2.FromInt(10, 0) });
            collections.Replace(
                owner,
                EntityCollectionDescriptor.Create(
                    collectionKey,
                    EntityCollectionSourceKind.Debug,
                    EntityCollectionRoleKind.Debug),
                entities);

            bool found = PlacementValidation.TrySnapToNearestInCollection(
                world,
                collections,
                owner,
                collectionKeyId,
                Fix64Vec2.Zero,
                Fix64.FromInt(100),
                out Fix64Vec2 snappedCm,
                out Entity snappedEntity);

            That(found, Is.True);
            That(snappedEntity, Is.EqualTo(entities[64]));
            That(snappedCm, Is.EqualTo(Fix64Vec2.FromInt(10, 0)));
        }

        [Test]
        public void TrySnapToNearestGraphEdge_ProjectsOntoNearestSegment()
        {
            var builder = new NodeGraphBuilder(3, 2);
            builder.AddNode(0, 0);
            builder.AddNode(100, 0);
            builder.AddNode(200, 0);
            builder.AddEdge(0, 1, 100f);
            builder.AddEdge(1, 2, 100f);
            NodeGraph graph = builder.Build();
            INodeGraphSpatialIndex index = LoadedGraphRuntime.CreateSpatialIndex(graph, preferredCellSizeCm: 100);
            Span<int> scratch = stackalloc int[8];
            Fix64Vec2 point = Fix64Vec2.FromInt(50, 25);

            bool found = PlacementValidation.TrySnapToNearestGraphEdge(
                graph,
                index,
                ref point,
                Fix64.FromInt(100),
                scratch,
                out GraphEdgeProjection projection);

            That(found, Is.True);
            That(point, Is.EqualTo(Fix64Vec2.FromInt(50, 0)));
            That(projection.FromNodeId, Is.EqualTo(0));
            That(projection.ToNodeId, Is.EqualTo(1));
        }

        [Test]
        public void GraphOps_SnapToNearestGraphEdge_UpdatesTargetPos()
        {
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm: 1000, loadedChunkCapacity: 1);
            var store = new ChunkedNodeGraphStore();
            store.SubscribeToLoadedChunks(loadedChunks);
            long chunkKey = GraphChunkKey.Pack(0, 0);
            var graphBuilder = new NodeGraphBuilder(3, 2);
            graphBuilder.AddNode(0, 0);
            graphBuilder.AddNode(100, 0);
            graphBuilder.AddNode(200, 0);
            graphBuilder.AddEdge(0, 1, 100f);
            graphBuilder.AddEdge(1, 2, 100f);
            store.AddOrReplace(chunkKey, new GraphChunkData(graphBuilder.Build(), Array.Empty<GraphCrossEdge>()));
            loadedChunks.SetLoaded(chunkKey, loaded: true);

            using var runtime = new LoadedGraphRuntime(store, loadedChunks, preferredProjectionCellSizeCm: 100);
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);
            api.BindLoadedGraphRuntime(runtime);
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 100f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.SnapToNearestGraphEdge, A = 1, Dst = 0 },
            };

            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            f[1] = 100f;
            var state = new GraphExecutionState
            {
                World = world,
                Caster = Entity.Null,
                ExplicitTarget = Entity.Null,
                TargetPosCm = new IntVector2(50, 25),
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
            CallStack = new int[Ludots.Core.NodeLibraries.GASGraph.GraphVmLimits.MaxCallStackDepth],
            CallStackCount = 0,
        };

            GasGraphOpHandlerTable.Execute(ref state, program, new GasGraphOpHandlerTable());
            That(state.B[0], Is.EqualTo(1));
            That(state.TargetPosCm, Is.EqualTo(new IntVector2(50, 0)));
        }
    }
}
