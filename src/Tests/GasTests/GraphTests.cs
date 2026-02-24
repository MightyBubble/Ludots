using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using GraphInstruction = Ludots.Core.GraphRuntime.GraphInstruction;
using GraphProgramBlob = Ludots.Core.GraphRuntime.GraphProgramBlob;
using GraphProgramPackage = Ludots.Core.GraphRuntime.GraphProgramPackage;
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
        public void Blob_RoundTrip_PreservesGraphNameSymbolsAndProgram()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 1.0f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.QueryFilterTagAll, Imm = 0 }
            };

            var pkg = new GraphProgramPackage("G1", new[] { "Tag.A" }, program);

            using var ms = new MemoryStream();
            GraphProgramBlob.Write(ms, new List<GraphProgramPackage> { pkg });
            ms.Position = 0;

            string readName = string.Empty;
            string[] readSymbols = null;
            GraphInstruction[] readProgram = null;
            GraphProgramBlob.Read(ms, (name, symbols, prog) =>
            {
                readName = name;
                readSymbols = symbols;
                readProgram = prog;
            });

            That(readName, Is.EqualTo("G1"));
            That(readSymbols, Is.Not.Null);
            That(readSymbols, Does.Contain("Tag.A"));
            That(readProgram, Is.Not.Null);
            That(readProgram.Length, Is.EqualTo(2));
            That(readProgram[0].Op, Is.EqualTo((ushort)GraphNodeOp.ConstFloat));
            That(readProgram[1].Op, Is.EqualTo((ushort)GraphNodeOp.QueryFilterTagAll));
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
            var api = new GasGraphRuntimeApi(world, spatial, coords, null);

            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.QueryRadius, ImmF = 8.0f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.QueryFilterTagAll, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.QuerySortStable },
                new GraphInstruction { Op = (ushort)GraphNodeOp.QueryLimit, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.AggMinByDistance, Dst = 2 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 10.0f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 2, B = 0, Imm = 0 }
            };

            GraphExecutor.Execute(world, caster, default, new IntVector2(0, 0), program, api);

            ref var a1 = ref world.Get<AttributeBuffer>(e1);
            ref var a2 = ref world.Get<AttributeBuffer>(e2);
            That(a1.GetCurrent(0), Is.EqualTo(10.0f));
            That(a2.GetCurrent(0), Is.EqualTo(0.0f));

            world.Dispose();
        }
    }
}
