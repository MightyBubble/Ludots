using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using GraphInstruction = Ludots.Core.GraphRuntime.GraphInstruction;
using Ludots.Core.Mathematics;
using Ludots.Core.Physics;
using Ludots.Core.Spatial;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class GraphCompilerTests
    {
        [Test]
        public void Compile_BuildsSymbolTable_AndInstructions()
        {
            var cfg = new GraphConfig
            {
                Id = "Test.Graph",
                Kind = "Effect",
                Entry = "t1",
                Nodes = new List<GraphNodeConfig>
                {
                    new GraphNodeConfig { Id = "t1", Op = "LoadExplicitTarget", Next = "c1" },
                    new GraphNodeConfig { Id = "c1", Op = "ConstFloat", FloatValue = 5.0f, Next = "m1" },
                    new GraphNodeConfig { Id = "m1", Op = "ModifyAttributeAdd", Attribute = "Health", Inputs = new List<string> { "t1", "c1" } }
                }
            };

            var (pkg, diags) = GraphCompiler.Compile(cfg);
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
        public void Compile_ExtensionOp_UsesRegisteredHandler()
        {
            var opRegistry = new GasGraphOpRegistry();
            int opCode = opRegistry.Register(
                "ExampleMod.DoubleFloat",
                GraphValueType.Float,
                DoubleFloat,
                GraphValueType.Float);

            var cfg = new GraphConfig
            {
                Id = "ExampleMod.Graph.Double",
                Kind = "Effect",
                Entry = "value",
                Nodes = new List<GraphNodeConfig>
                {
                    new GraphNodeConfig { Id = "value", Op = "ConstFloat", FloatValue = 3.5f, Next = "double" },
                    new GraphNodeConfig { Id = "double", Op = "ExampleMod.DoubleFloat", Inputs = new List<string> { "value" } },
                },
            };

            var (pkg, diags) = GraphCompiler.Compile(cfg, opRegistry);
            That(pkg.HasValue, Is.True);
            for (int i = 0; i < diags.Count; i++)
            {
                That(diags[i].Severity, Is.Not.EqualTo(GraphDiagnosticSeverity.Error), diags[i].Message);
            }

            var handlers = new GasGraphOpHandlerTable(opRegistry);
            float[] f = new float[GraphVmLimits.MaxFloatRegisters];
            int[] ints = new int[GraphVmLimits.MaxIntRegisters];
            byte[] bools = new byte[GraphVmLimits.MaxBoolRegisters];
            Entity[] entities = new Entity[GraphVmLimits.MaxEntityRegisters];
            Entity[] targets = new Entity[GraphVmLimits.MaxTargets];
            using var world = World.Create();
            var state = new GraphExecutionState
            {
                World = world,
                Api = null!,
                F = f,
                I = ints,
                B = bools,
                E = entities,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
            };

            That(pkg!.Value.Program[1].Op, Is.EqualTo(opCode));
            GasGraphOpHandlerTable.Execute(ref state, pkg.Value.Program, handlers);

            That(f[1], Is.EqualTo(7.0f));
        }

        private static void DoubleFloat(ref GraphExecutionState state, in GraphInstruction ins, ref int pc)
        {
            state.F[ins.Dst] = state.F[ins.A] * 2.0f;
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
            world.Add(e1, new GameplayTagContainer());
            world.Add(e1, new AttributeBuffer());
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
            world.Add(e2, new GameplayTagContainer());
            world.Add(e2, new AttributeBuffer());
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
            var tagOps = new TagOps();
            var relationshipRuntime = new RelationshipRuntime(
                world,
                new RelationshipTypeRegistry(),
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer());
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
                new GraphInstruction { Op = (ushort)GraphNodeOp.QueryRadius, ImmF = 8.0f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.QueryFilterTagAny, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.QuerySortStable },
                new GraphInstruction { Op = (ushort)GraphNodeOp.QueryLimit, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.AggMinByDistance, Dst = 2 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 10.0f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 2, B = 0, Imm = 0 }
            };

            GraphExecutor.Execute(world, caster, default, new IntVector2(0, 0), program, api, new GasGraphOpHandlerTable());

            ref var a1 = ref world.Get<AttributeBuffer>(e1);
            ref var a2 = ref world.Get<AttributeBuffer>(e2);
            That(a1.GetCurrent(0), Is.EqualTo(10.0f));
            That(a2.GetCurrent(0), Is.EqualTo(0.0f));

            world.Dispose();
        }
    }
}
