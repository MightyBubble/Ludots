using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using GraphInstruction = Ludots.Core.GraphRuntime.GraphInstruction;
using Ludots.Core.Mathematics;
using Ludots.Core.Physics;
using Ludots.Core.Spatial;
using NUnit.Framework;
using static NUnit.Framework.Assert;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class GraphControlFlowCompilerTests
    {
        [Test]
        public void Compile_BuildsSymbolTable_AndInstructions()
        {
            var cfg = new GraphControlFlowDocument
            {
                Id = "Test.Graph",
                Kind = "Effect",
                Entry = "t1",
                Nodes = new List<GraphControlFlowNode>
                {
                    new GraphControlFlowNode { Id = "t1", Op = "LoadExplicitTarget" },
                    new GraphControlFlowNode { Id = "c1", Op = "ConstFloat", FloatValue = 5.0f },
                    new GraphControlFlowNode { Id = "m1", Op = "ModifyAttributeAdd", Attribute = "Health" }
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("t1", GraphControlFlowPorts.Next, "c1"),
                    new("c1", GraphControlFlowPorts.Next, "m1")
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("t1", GraphControlFlowPorts.Value, "m1", GraphControlFlowPorts.Target),
                    new("c1", GraphControlFlowPorts.Value, "m1", GraphControlFlowPorts.Value)
                },
            };

            var (pkg, _, diags) = GraphControlFlowCompiler.CompileWithOutputs(cfg);
            That(pkg.HasValue, Is.True);
            for (int i = 0; i < diags.Count; i++)
            {
                That(diags[i].Severity, Is.Not.EqualTo(GraphDiagnosticSeverity.Error), diags[i].Message);
            }

            var p = pkg!.Value;
            That(p.GraphName, Is.EqualTo("Test.Graph"));
            That(p.Symbols, Does.Contain("Health"));
            That(p.Program.Length, Is.GreaterThan(0));
        }

        [Test]
        public void Execute_NullHandlerTable_ThrowsArgumentNullException()
        {
            Throws<System.ArgumentNullException>(ExecuteWithNullHandlerTable);
        }

        private static void ExecuteWithNullHandlerTable()
        {
            using var world = World.Create();
            Entity[] targets = new Entity[GraphVmLimits.MaxTargets];
            var state = new GraphExecutionState
            {
                World = world,
                F = new float[GraphVmLimits.MaxFloatRegisters],
                I = new int[GraphVmLimits.MaxIntRegisters],
                B = new byte[GraphVmLimits.MaxBoolRegisters],
                E = new Entity[GraphVmLimits.MaxEntityRegisters],
                Targets = targets,
                TargetList = new GraphTargetList(targets),
                CallStack = new int[GraphVmLimits.MaxCallStackDepth],
            };

            GasGraphOpHandlerTable.Execute(ref state, System.ReadOnlySpan<GraphInstruction>.Empty, null!);
        }

    }

    [TestFixture]
    public class GraphExecutorQueryTests
    {
        [Test]
        public void Execute_QueryFilterAggregate_ModifiesNearestTaggedTarget()
        {
            var world = World.Create();
            var physics = new PhysicsWorld();

            var caster = world.Create();
            world.Add(caster, new Position { GridPos = new IntVector2(0, 0) });

            var e1 = world.Create();
            world.Add(e1, new Position { GridPos = new IntVector2(2, 0) });
            world.Add(e1, WorldPositionCm.FromCm(250, 50));
            world.Add(e1, new GameplayTagContainer());
            world.Add(e1, new AttributeBuffer());
            world.Add(e1, new DirtyFlags());
            unsafe
            {
                ref var tags = ref world.Get<GameplayTagContainer>(e1);
                tags.AddTag(1);
                ref var attr = ref world.Get<AttributeBuffer>(e1);
                attr.SetCurrent(0, 0f);
            }
            physics.Add(e1, new IntRect(2, 0, 1, 1));

            var e2 = world.Create();
            world.Add(e2, new Position { GridPos = new IntVector2(6, 0) });
            world.Add(e2, WorldPositionCm.FromCm(650, 50));
            world.Add(e2, new GameplayTagContainer());
            world.Add(e2, new AttributeBuffer());
            world.Add(e2, new DirtyFlags());
            unsafe
            {
                ref var tags = ref world.Get<GameplayTagContainer>(e2);
                tags.AddTag(1);
                ref var attr = ref world.Get<AttributeBuffer>(e2);
                attr.SetCurrent(0, 0f);
            }
            physics.Add(e2, new IntRect(6, 0, 1, 1));

            var coords = new SpatialCoordinateConverter();
            var spatial = new SpatialQueryService(new PhysicsWorldSpatialBackend(physics, coords));
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
            var relationshipRuntime = new RelationshipRuntime(
                world,
                new RelationshipTypeRegistry(),
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(),
                new RelationshipReverseIndex(world));
            var entityQueries = new EntitySetQueryRuntime(world, tagOps, relationshipRuntime);
            var api = new GasGraphRuntimeApi(
                world,
                spatial,
                coords,
                null,
                tagOps: tagOps,
                relationshipRuntime: relationshipRuntime,
                entityQueries: entityQueries);

            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.QueryRadius, ImmF = 800.0f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.QueryFilterTagAny, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.QuerySortStable },
                new GraphInstruction { Op = (ushort)GraphNodeOp.QueryLimit, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.AggMinByDistance, Dst = 2 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 10.0f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 2, B = 0, Imm = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt }
            };

            GraphExecutor.Execute(world, caster, default, new IntVector2(0, 0), program, api, GraphKind.Effect, new GasGraphOpHandlerTable());

            ref var a1 = ref world.Get<AttributeBuffer>(e1);
            ref var a2 = ref world.Get<AttributeBuffer>(e2);
            That(a1.GetCurrent(0), Is.EqualTo(10.0f));
            That(a2.GetCurrent(0), Is.EqualTo(0.0f));

            world.Dispose();
        }
    }
}
