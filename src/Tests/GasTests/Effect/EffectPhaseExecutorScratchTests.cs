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
            var target = world.Create(new AttributeBuffer(), new DirtyFlags());

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
            }, GraphKind.Effect);
            programs.Register(2, new[]
            {
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ModifyAttributeAdd,
                    A = 1,
                    B = 31,
                    Imm = attributeId,
                }
            }, GraphKind.Effect);

            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry());
            var api = new GasGraphRuntimeApi(
                world,
                spatialQueries: null,
                coords: null,
                eventBus: null,
                effectRequests: null,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

            executor.ExecuteGraph(world, api, target, target, default, default, 1);
            executor.ExecuteGraph(world, api, target, target, default, default, 2);

            Assert.That(world.Get<AttributeBuffer>(target).GetCurrent(attributeId), Is.EqualTo(0f));
        }

        [Test]
        public void ExecuteGraph_WhenProgramIdExceedsScratchCapacity_FailsWithoutResizing()
        {
            using var world = World.Create();
            var target = world.Create(new AttributeBuffer(), new DirtyFlags());

            var programs = new GraphProgramRegistry();
            programs.Register(2, new[]
            {
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstFloat,
                    Dst = 0,
                    ImmF = 1f,
                }
            }, GraphKind.Effect);

            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry(),
                graphProgramScratchCapacity: 2);
            var api = new GasGraphRuntimeApi(
                world,
                spatialQueries: null,
                coords: null,
                eventBus: null,
                effectRequests: null,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                executor.ExecuteGraph(world, api, target, target, default, default, 2))!;

            Assert.That(ex.Message, Does.StartWith(EffectPhaseExecutor.GraphProgramScratchCapacityExceededError));
        }
    }
}
