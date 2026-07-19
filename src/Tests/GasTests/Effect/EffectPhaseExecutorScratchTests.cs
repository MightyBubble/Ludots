using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class EffectPhaseExecutorScratchTests
    {
        [Test]
        public void ExecuteGraph_ResetsReferencedScratchRegistersBeforeReuse()
        {
            using var world = World.Create();
            var target = world.Create(new AttributeBuffer());

            const int attributeId = 7;
            var programs = new GraphProgramRegistry();
            programs.Register(1, new[]
            {
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstFloat,
                    Dst = 31,
                    ImmF = 5f,
                }
            });
            programs.Register(2, new[]
            {
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ModifyAttributeAdd,
                    A = 1,
                    B = 31,
                    Imm = attributeId,
                }
            });

            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry());
            var api = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, effectRequests: null);

            executor.ExecuteGraph(world, api, target, target, default, default, 1);
            executor.ExecuteGraph(world, api, target, target, default, default, 2);

            Assert.That(world.Get<AttributeBuffer>(target).GetCurrent(attributeId), Is.EqualTo(0f));
        }
    }
}
