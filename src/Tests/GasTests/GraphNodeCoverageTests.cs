using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
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
    public class GraphNodeCoverageTests
    {
        [Test]
        public void Execute_ControlFlow_Arithmetic_Select_ApplyEffect_SendEvent()
        {
            var world = World.Create();
            try
            {
                var caster = world.Create(new AttributeBuffer(), new Position { GridPos = new IntVector2(0, 0) });
                world.Get<AttributeBuffer>(caster).SetCurrent(0, 0f);

                var target = world.Create(new AttributeBuffer(), new Position { GridPos = new IntVector2(1, 0) });
                world.Get<AttributeBuffer>(target).SetCurrent(0, 7f);

                var effectRequests = new EffectRequestQueue();
                var eventBus = new GameplayEventBus();
                var api = new GasGraphRuntimeApi(world, spatialQueries: null, eventBus: eventBus, effectRequests: effectRequests);

                const int evtId = 123;
                const int tplId = 42;

                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 123 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadAttribute, Dst = 0, A = 2, Imm = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 1f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.AddFloat, Dst = 2, A = 0, B = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 3, ImmF = 2f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.MulFloat, Dst = 4, A = 2, B = 3 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 2, B = 4, Imm = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.CompareGtFloat, Dst = 0, A = 4, B = 3 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.JumpIfFalse, A = 0, Imm = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.SendEvent, A = 2, B = 4, Imm = evtId },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.SendEvent, A = 2, B = 1, Imm = 999 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 1, Imm = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 3 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.SelectEntity, Dst = 4, A = 1, B = 3, C = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 4, B = 1, Imm = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ApplyEffectTemplate, A = 2, Imm = tplId }
                };

                GraphExecutor.Execute(world, caster, target, new IntVector2(0, 0), program, api);

                eventBus.Update();

                That(world.Get<AttributeBuffer>(target).GetCurrent(0), Is.EqualTo(23f));
                That(world.Get<AttributeBuffer>(caster).GetCurrent(0), Is.EqualTo(1f));

                That(eventBus.Events.Count, Is.EqualTo(1));
                That(eventBus.Events[0].TagId, Is.EqualTo(evtId));
                That(eventBus.Events[0].Target, Is.EqualTo(target));
                That(eventBus.Events[0].Magnitude, Is.EqualTo(16f));

                That(effectRequests.Count, Is.EqualTo(1));
                That(effectRequests[0].Source, Is.EqualTo(caster));
                That(effectRequests[0].Target, Is.EqualTo(target));
                That(effectRequests[0].TemplateId, Is.EqualTo(tplId));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void Execute_JumpIfFalse_SkipsSideEffects()
        {
            var world = World.Create();
            try
            {
                var caster = world.Create(new AttributeBuffer(), new Position { GridPos = new IntVector2(0, 0) });
                var target = world.Create(new AttributeBuffer(), new Position { GridPos = new IntVector2(1, 0) });

                var effectRequests = new EffectRequestQueue();
                var eventBus = new GameplayEventBus();
                var api = new GasGraphRuntimeApi(world, spatialQueries: null, eventBus: eventBus, effectRequests: effectRequests);

                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.JumpIfFalse, A = 0, Imm = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.SendEvent, A = 2, B = 0, Imm = 555 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ApplyEffectTemplate, A = 2, Imm = 7 }
                };

                GraphExecutor.Execute(world, caster, target, new IntVector2(0, 0), program, api);
                eventBus.Update();

                That(eventBus.Events.Count, Is.EqualTo(0));
                That(effectRequests.Count, Is.EqualTo(0));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void Execute_AggCount_PathIsExercised()
        {
            var world = World.Create();
            try
            {
                var physics = new PhysicsWorld();
                var caster = world.Create(new Position { GridPos = new IntVector2(0, 0) });
                var target = world.Create(new AttributeBuffer(), new Position { GridPos = new IntVector2(1, 0) });
                world.Get<AttributeBuffer>(target).SetCurrent(0, 0f);

                var e1 = world.Create(new Position { GridPos = new IntVector2(2, 0) });
                physics.Add(e1, new IntRect(2, 0, 1, 1));

                var coords = new SpatialCoordinateConverter();
                var spatial = new SpatialQueryService(new PhysicsWorldSpatialBackend(physics, coords));
                var api = new GasGraphRuntimeApi(world, spatial, coords, eventBus: null, effectRequests: null);

                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.QueryRadius, ImmF = 8.0f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.AggCount, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 1f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 2, B = 1, Imm = 0 }
                };

                GraphExecutor.Execute(world, caster, target, new IntVector2(0, 0), program, api);

                That(world.Get<AttributeBuffer>(target).GetCurrent(0), Is.EqualTo(1f));
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
