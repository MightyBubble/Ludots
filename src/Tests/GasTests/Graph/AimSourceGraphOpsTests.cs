using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.AimSource;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Graph
{
    /// <summary>
    /// Aimsource op family end to end: authored Query graphs compile to the new opcodes
    /// and execute against the production aimsource runtime bound over stub engine
    /// globals (screen px ↔ world cm 1:1 projector, flat heightmap ground). Screen point
    /// to ground, knowledge-gated candidate pick, rect region filter, and the two
    /// direction helpers.
    /// </summary>
    [TestFixture]
    public sealed class AimSourceGraphOpsTests
    {
        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
            ConfigKeyRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GraphIdRegistry.Clear();
            ConfigKeyRegistry.Clear();
        }

        [Test]
        public void StickToDirection_ComputesAngleDegreesFromStickVector()
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "aim.stick",
                Kind = "Query",
                Entry = "x",
                Nodes =
                {
                    ConstFloat("x", 1f),
                    ConstFloat("y", 1f),
                    new GraphControlFlowNode { Id = "stick", Op = "StickToDirection", ValidOutput = "hasDir" },
                },
                ControlEdges =
                {
                    ControlEdge("x", "y"),
                    ControlEdge("y", "stick"),
                },
                ValueEdges =
                {
                    ValueEdge("x", "stick", "a"),
                    ValueEdge("y", "stick", "b"),
                }
            };

            using var world = World.Create();
            RunResult run = CompileAndRun(doc, world, featuredNodeId: "stick");
            Assert.That(run.Ops, Does.Contain(GraphNodeOp.StickToDirection));
            Assert.That(run.Floats[run.FeaturedDst], Is.EqualTo(45f).Within(0.0001f),
                "stick (1,1) aims at 45 degrees (0 = +X)");
        }

        [Test]
        public void StickToDirection_DeadzoneVector_ReportsInvalidAndZero()
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "aim.stick.dead",
                Kind = "Query",
                Entry = "x",
                Nodes =
                {
                    ConstFloat("x", 0.0005f),
                    ConstFloat("y", 0f),
                    new GraphControlFlowNode { Id = "stick", Op = "StickToDirection", ValidOutput = "hasDir" },
                },
                ControlEdges =
                {
                    ControlEdge("x", "y"),
                    ControlEdge("y", "stick"),
                },
                ValueEdges =
                {
                    ValueEdge("x", "stick", "a"),
                    ValueEdge("y", "stick", "b"),
                }
            };

            using var world = World.Create();
            RunResult run = CompileAndRun(doc, world, featuredNodeId: "stick");
            Assert.That(run.Floats[run.FeaturedDst], Is.EqualTo(0f),
                "a sub-deadzone stick has no direction");
        }

        [Test]
        public void PointToDirection_AimsFromRepPositionToTargetPoint()
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "aim.point",
                Kind = "Query",
                Entry = "load",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "load", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "dir", Op = "PointToDirection", ValidOutput = "hasDir" },
                },
                ControlEdges = { ControlEdge("load", "dir") },
                ValueEdges = { ValueEdge("load", "dir", "source") },
            };

            using var world = World.Create();
            Entity rep = world.Create(WorldPositionCm.FromCm(1000, 2000));
            RunResult run = CompileAndRun(
                doc,
                world,
                featuredNodeId: "dir",
                caster: rep,
                targetPosCm: new IntVector2(2000, 2000));

            Assert.That(run.Ops, Does.Contain(GraphNodeOp.PointToDirection));
            Assert.That(run.Floats[run.FeaturedDst], Is.EqualTo(0f).Within(0.0001f),
                "rep (1000,2000) → point (2000,2000) aims along +X (0 degrees)");
        }

        [Test]
        public void PointToDirection_RepWithoutPosition_ReportsInvalid()
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "aim.point.missing",
                Kind = "Query",
                Entry = "load",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "load", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "dir", Op = "PointToDirection", ValidOutput = "hasDir" },
                },
                ControlEdges = { ControlEdge("load", "dir") },
                ValueEdges = { ValueEdge("load", "dir", "source") },
            };

            using var world = World.Create();
            Entity rep = world.Create();
            RunResult run = CompileAndRun(doc, world, featuredNodeId: "dir", caster: rep);
            Assert.That(run.Floats[run.FeaturedDst], Is.EqualTo(0f),
                "a rep without a world position yields no direction");
        }

        [Test]
        public void ScreenPointToGround_ResolvesThroughAuthoritativeKernelAndFeedsTargetPos()
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "aim.ground",
                Kind = "Query",
                Entry = "load",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "load", Op = "LoadCaster" },
                    ConstFloat("sx", 8300f),
                    ConstFloat("sy", 4500f),
                    new GraphControlFlowNode { Id = "ground", Op = "ScreenPointToGround" },
                    new GraphControlFlowNode { Id = "dir", Op = "PointToDirection", ValidOutput = "hasDir" },
                },
                ControlEdges =
                {
                    ControlEdge("load", "sx"),
                    ControlEdge("sx", "sy"),
                    ControlEdge("sy", "ground"),
                    ControlEdge("ground", "dir"),
                },
                ValueEdges =
                {
                    ValueEdge("sx", "ground", "a"),
                    ValueEdge("sy", "ground", "b"),
                    ValueEdge("load", "dir", "source"),
                },
            };

            using var world = World.Create();
            Entity rep = world.Create(WorldPositionCm.FromCm(0, 0));
            RunResult run = CompileAndRun(doc, world, featuredNodeId: "dir", caster: rep);
            Assert.That(run.Api.TryScreenPointToGround(8300f, 4500f, out IntVector2 kernelCm), Is.True,
                "kernel sanity: the stub ground chain resolves the screen point");
            Assert.That(kernelCm.X, Is.EqualTo(8300));
            Assert.That(kernelCm.Y, Is.EqualTo(4500));
            Assert.That(run.Ops, Does.Contain(GraphNodeOp.ScreenPointToGround));
            Assert.That(run.Ops, Does.Contain(GraphNodeOp.PointToDirection));
            Assert.That(run.Floats[run.FeaturedDst], Is.EqualTo(MathF.Atan2(4500f, 8300f) * (180f / MathF.PI)).Within(0.01f),
                "the screen point resolves on the ground through the camera ray + heightmap chain and feeds the aim direction from the rep position");
        }

        [Test]
        public void ScreenPointToEntity_PicksNearestKnowledgeGatedCandidate()
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "aim.pick",
                Kind = "Query",
                Entry = "load",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "load", Op = "LoadCaster" },
                    ConstFloat("sx", 1610f),
                    ConstFloat("sy", 1200f),
                    new GraphControlFlowNode { Id = "pick", Op = "ScreenPointToEntity", PickRadiusPx = 20f },
                },
                ControlEdges =
                {
                    ControlEdge("load", "sx"),
                    ControlEdge("sx", "sy"),
                    ControlEdge("sy", "pick"),
                },
                ValueEdges =
                {
                    ValueEdge("load", "pick", "source"),
                    ValueEdge("sx", "pick", "a"),
                    ValueEdge("sy", "pick", "b"),
                },
            };

            using var world = World.Create();
            Entity owner = world.Create();
            Entity near = world.Create(WorldPositionCm.FromCm(1600, 1200), new CommandSourceSelectableTag());
            Entity far = world.Create(WorldPositionCm.FromCm(2600, 1600), new CommandSourceSelectableTag());
            Entity unselectable = world.Create(
                WorldPositionCm.FromCm(1610, 1200),
                new CommandSourceSelectableTag(),
                new CommandSourceSelectableState { IsEnabled = 0 });

            RunResult run = CompileAndRun(
                doc,
                world,
                featuredNodeId: "pick",
                caster: owner,
                candidates: new[] { far, unselectable, near });

            Assert.That(run.Ops, Does.Contain(GraphNodeOp.ScreenPointToEntity));
            Assert.That(run.Entities[run.FeaturedDst], Is.EqualTo(near),
                "the pointer picks the nearest inspectable candidate from the explicit set; disabled candidates never win");
        }

        [Test]
        public void ScreenRegionToEntities_FiltersCandidatesToRectInOrder()
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "aim.region",
                Kind = "Query",
                Entry = "minX",
                Nodes =
                {
                    ConstFloat("minX", 1500f),
                    ConstFloat("minY", 1100f),
                    ConstFloat("maxX", 3500f),
                    ConstFloat("maxY", 2300f),
                    new GraphControlFlowNode { Id = "region", Op = "ScreenRegionToEntities" },
                },
                ControlEdges =
                {
                    ControlEdge("minX", "minY"),
                    ControlEdge("minY", "maxX"),
                    ControlEdge("maxX", "maxY"),
                    ControlEdge("maxY", "region"),
                },
                ValueEdges =
                {
                    ValueEdge("minX", "region", "a"),
                    ValueEdge("minY", "region", "b"),
                    ValueEdge("maxX", "region", "c"),
                    ValueEdge("maxY", "region", "max"),
                },
            };

            using var world = World.Create();
            Entity owner = world.Create();
            Entity insideFirst = world.Create(WorldPositionCm.FromCm(1600, 1200), new CommandSourceSelectableTag());
            Entity outside = world.Create(WorldPositionCm.FromCm(5000, 5000), new CommandSourceSelectableTag());
            Entity insideSecond = world.Create(WorldPositionCm.FromCm(2600, 1600), new CommandSourceSelectableTag());

            RunResult run = CompileAndRun(
                doc,
                world,
                featuredNodeId: "region",
                caster: owner,
                candidates: new[] { insideFirst, outside, insideSecond });

            Assert.That(run.Ops, Does.Contain(GraphNodeOp.ScreenRegionToEntities));
            Assert.That(run.TargetCount, Is.EqualTo(2));
            Assert.That(run.Targets[0], Is.EqualTo(insideFirst));
            Assert.That(run.Targets[1], Is.EqualTo(insideSecond),
                "the rect filter preserves candidate order — the result order stays deterministic");
        }

        [Test]
        public void AimSourceApi_WithoutBoundRuntime_FailsClosed()
        {
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world);
            Entity[] buffer = new Entity[1];
            Assert.That(() => api.TryScreenPointToGround(0f, 0f, out _), Throws.InvalidOperationException);
            Assert.That(() => api.PickScreenPointEntity(buffer, 1, Entity.Null, null, 0f, 0f, 10f), Throws.InvalidOperationException);
            Assert.That(() => api.FilterScreenRegionEntities(buffer, 1, new ScreenRect(0f, 0f, 1f, 1f)), Throws.InvalidOperationException);
        }

        private static GraphControlFlowNode ConstFloat(string id, float value) =>
            new() { Id = id, Op = "ConstFloat", FloatValue = value };

        private static GraphControlFlowEdge ControlEdge(string from, string to) =>
            new(from, GraphControlFlowPorts.Next, to);

        private static GraphControlFlowValueEdge ValueEdge(string from, string to, string toPort) =>
            new(from, GraphControlFlowPorts.Value, to, toPort);

        /// <summary>Copied-out execution results: the frame is a ref struct, so assertions read these.</summary>
        private sealed class RunResult
        {
            public RunResult(GraphNodeOp[] ops, byte featuredDst, float[] floats, int[] ints, Entity[] entities, Entity[] targets, int targetCount, GasGraphRuntimeApi api, byte[] bools, byte groundDst)
            {
                Ops = ops;
                FeaturedDst = featuredDst;
                Floats = floats;
                Ints = ints;
                Entities = entities;
                Targets = targets;
                TargetCount = targetCount;
                Api = api;
                Bools = bools;
                GroundDst = groundDst;
            }

            public GasGraphRuntimeApi Api { get; }
            public byte[] Bools { get; }
            public byte GroundDst { get; }

            public GraphNodeOp[] Ops { get; }
            public byte FeaturedDst { get; }
            public float[] Floats { get; }
            public int[] Ints { get; }
            public Entity[] Entities { get; }
            public Entity[] Targets { get; }
            public int TargetCount { get; }
        }

        private static RunResult CompileAndRun(
            GraphControlFlowDocument doc,
            World world,
            string featuredNodeId,
            Entity caster = default,
            IntVector2 targetPosCm = default,
            Entity[]? candidates = null)
        {
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.ContinuousHeightmap.Name] = new ContinuousHeightmapRuntime(
                    ContinuousHeightmapAsset.CreateSingleLayer(
                        new WorldAabbCm(-10_000, -10_000, 20_000, 20_000),
                        sampleColumns: 2,
                        sampleRows: 2,
                        new short[] { 0, 0, 0, 0 })),
                [CoreServiceKeys.WorldSizeSpec.Name] = new WorldSizeSpec(new WorldAabbCm(-10_000, -10_000, 20_000, 20_000), 100),
            };
            var api = new GasGraphRuntimeApi(world);
            api.BindAimSource(new GraphAimSourceRuntime(world, globals));

            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc);
            Assert.That(result.Diagnostics, Is.Empty, string.Join("; ", result.Diagnostics));
            Assert.That(result.Package.HasValue, Is.True);
            GraphProgramPackage package = result.Package!.Value;
            var ops = Array.ConvertAll(package.Program, instruction => (GraphNodeOp)instruction.Op);

            byte featuredDst = RequireFeaturedDst(result, featuredNodeId);
            var frame = GraphFrame.Bind(
                GraphKind.Query,
                GraphEntityPreset.None,
                world,
                caster,
                default,
                targetPosCm,
                api,
                programs: null,
                new float[GraphVmLimits.MaxFloatRegisters],
                new int[GraphVmLimits.MaxIntRegisters],
                new byte[GraphVmLimits.MaxBoolRegisters],
                new Entity[GraphVmLimits.MaxEntityRegisters],
                new Entity[GraphVmLimits.MaxTargets],
                new int[GraphVmLimits.MaxIntIds],
                new int[GraphVmLimits.MaxCallStackDepth]);
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    frame.Targets[i] = candidates[i];
                }

                frame.TargetList.SetCount(candidates.Length);
            }

            GraphExecutor.Execute(ref frame, package.Program, programAlreadyValidated: false);
            var targetsOut = new Entity[frame.TargetList.Count];
            for (int i = 0; i < targetsOut.Length; i++)
            {
                targetsOut[i] = frame.Targets[i];
            }

            byte groundDst = 0;
            for (int i = 0; i < package.Program.Length; i++)
            {
                if (package.Program[i].Op == (ushort)GraphNodeOp.ScreenPointToGround)
                {
                    groundDst = package.Program[i].Dst;
                }
            }

            return new RunResult(
                ops,
                featuredDst,
                frame.F.ToArray(),
                frame.I.ToArray(),
                frame.E.ToArray(),
                targetsOut,
                frame.TargetList.Count,
                api,
                frame.B.ToArray(),
                groundDst);
        }

        private static byte RequireFeaturedDst(GraphControlFlowCompileResult compiled, string featuredNodeId)
        {
            for (int i = 0; i < compiled.Program.Length; i++)
            {
                if (compiled.SourceMap.TryGetSource(i, out GraphInstructionSource source) &&
                    string.Equals(source.NodeId, featuredNodeId, StringComparison.Ordinal))
                {
                    return compiled.Program[i].Dst;
                }
            }

            Assert.Fail($"Featured node '{featuredNodeId}' not found in the compiled program.");
            return 0;
        }

        private sealed class WorldMappedScreenRayProvider : IScreenRayProvider
        {
            public ScreenRay GetRay(Vector2 screenPosition)
            {
                return new ScreenRay(new Vector3(screenPosition.X / 100f, 10f, screenPosition.Y / 100f), -Vector3.UnitY);
            }
        }

        private sealed class WorldMappedScreenProjector : IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 worldPosition)
            {
                return new Vector2(worldPosition.X * 100f, worldPosition.Z * 100f);
            }
        }
    }
}
