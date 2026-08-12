using System.Linq;
using CapabilityStandardGraphOpsBlackboardMod.Runtime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[Category("ci-gate")]
public sealed class GraphOpsBlackboardShowcaseAcceptanceTests
{
    [Test]
    public void RegistryName_MemoConfigAndLifecycleTransaction_DetailIsPlayerReadable()
    {
        var runtime = new GraphOpsBlackboardRuntime();
        runtime.EnsureWorld();
        for (int i = 0; i < 10; i++) runtime.Tick(0.2f);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("黑板记事"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("读配置"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("生命周期"));
            Assert.That(runtime.LifecycleStarts, Is.GreaterThan(0));
            Assert.That(runtime.Metrics.ThinkWaves, Is.GreaterThan(0));
            Assert.That(runtime.Metrics.MaxThinkMs, Is.LessThan(25.0));
        });
    }

    [Test]
    public void FrontDoor_MemoGraph_ContainsAllBlackboardAndConfigOps()
    {
        GraphControlFlowCompileResult compiled = GraphOpsBlackboardGraphAuthoring.CompileMemoGraph();
        Assert.That(compiled.Succeeded, Is.True);

        GraphNodeOp[] required =
        [
            GraphNodeOp.LoadContextSource,
            GraphNodeOp.LoadContextTargetContext,
            GraphNodeOp.WriteBlackboardEntity,
            GraphNodeOp.WriteBlackboardFloat,
            GraphNodeOp.WriteBlackboardInt,
            GraphNodeOp.ReadBlackboardFloat,
            GraphNodeOp.ReadBlackboardInt,
            GraphNodeOp.ReadBlackboardEntity,
            GraphNodeOp.LoadConfigFloat,
            GraphNodeOp.LoadConfigInt,
            GraphNodeOp.LoadConfigEffectId
        ];

        var emitted = compiled.Program.Select(i => (GraphNodeOp)i.Op).ToHashSet();
        foreach (GraphNodeOp op in required)
        {
            Assert.That(emitted, Does.Contain(op), $"Memo graph missing {op}");
        }
    }

    [Test]
    public void FrontDoor_LifecycleGraph_ContainsTransactionAndBuiltinOps()
    {
        GraphControlFlowCompileResult compiled = GraphOpsBlackboardGraphAuthoring.CompileLifecycleGraph();
        Assert.That(compiled.Succeeded, Is.True);

        var emitted = compiled.Program.Select(i => (GraphNodeOp)i.Op).ToHashSet();
        Assert.Multiple(() =>
        {
            Assert.That(emitted, Does.Contain(GraphNodeOp.BeginLifecycleTransaction));
            Assert.That(emitted, Does.Contain(GraphNodeOp.InvokeBuiltin));
        });
    }
}
