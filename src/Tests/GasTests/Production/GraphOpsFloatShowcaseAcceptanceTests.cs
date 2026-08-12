using CapabilityStandardGraphOpsFloatMod.Runtime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[Category("ci-gate")]
public sealed class GraphOpsFloatShowcaseAcceptanceTests
{
    [Test]
    public void RegistryName_DelegatesToSeparatedSuite()
    {
        var runtime = new GraphOpsFloatRuntime();
        runtime.EnsureWorld();
        for (int i = 0; i < 8; i++) runtime.Tick(0.2f);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain("伤害倍率"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("衰减"));
            Assert.That(runtime.Metrics.Detail, Does.Contain("钳制"));
            Assert.That(runtime.LastDamage, Is.GreaterThanOrEqualTo(0f));
            Assert.That(runtime.LastDamage, Is.LessThanOrEqualTo(80f));
            Assert.That(runtime.Metrics.ThinkWaves, Is.GreaterThan(0));
        });
    }

    [Test]
    public void FrontDoor_EffectGraph_ContainsAllFloatOps()
    {
        GraphControlFlowCompileResult compiled = GraphOpsFloatGraphAuthoring.CompileEffectGraph(distance: 20f);
        Assert.That(compiled.Succeeded, Is.True);

        GraphNodeOp[] required =
        [
            GraphNodeOp.ConstFloat,
            GraphNodeOp.AddFloat,
            GraphNodeOp.MulFloat,
            GraphNodeOp.SubFloat,
            GraphNodeOp.DivFloat,
            GraphNodeOp.MinFloat,
            GraphNodeOp.MaxFloat,
            GraphNodeOp.ClampFloat,
            GraphNodeOp.AbsFloat,
            GraphNodeOp.NegFloat,
            GraphNodeOp.CompareGtFloat,
            GraphNodeOp.RandomFloat01
        ];

        var emitted = compiled.Program.Select(i => (GraphNodeOp)i.Op).ToHashSet();
        foreach (GraphNodeOp op in required)
        {
            Assert.That(emitted, Does.Contain(op), $"Effect graph missing {op}");
        }
    }

    [Test]
    public void FrontDoor_ValidationGraph_CompilesAndUsesClampAndCompare()
    {
        GraphControlFlowCompileResult compiled = GraphOpsFloatGraphAuthoring.CompileValidationGraph(distance: 30f);
        Assert.That(compiled.Succeeded, Is.True);

        var emitted = compiled.Program.Select(i => (GraphNodeOp)i.Op).ToHashSet();
        Assert.Multiple(() =>
        {
            Assert.That(emitted, Does.Contain(GraphNodeOp.ClampFloat));
            Assert.That(emitted, Does.Contain(GraphNodeOp.CompareGtFloat));
        });
    }
}
