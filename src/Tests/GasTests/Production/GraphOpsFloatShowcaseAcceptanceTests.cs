using CapabilityStandardGraphOpsFloatMod.Runtime;
using Ludots.Core.GraphRuntime;
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
        using var runtime = new GraphOpsFloatRuntime();
        runtime.EnsureWorld();
        var details = new List<string>();
        bool sawPermitOn = false;
        bool sawPermitOff = false;
        bool sawInRange = false;
        bool sawOutOfRange = false;
        bool sawHealthDrop = false;

        for (int i = 0; i < 16; i++)
        {
            float previousHealth = runtime.TargetHealth;
            runtime.Tick(0.2f);
            details.Add(runtime.Metrics.Detail);
            AssertBannedEnglish(runtime.Metrics.Detail);
            Assert.That(runtime.LastDamage, Is.GreaterThanOrEqualTo(0f));
            Assert.That(runtime.LastDamage, Is.LessThanOrEqualTo(80f));

            if (runtime.LastPermit) sawPermitOn = true;
            else sawPermitOff = true;
            if (runtime.LastRangeValid) sawInRange = true;
            else sawOutOfRange = true;

            if (runtime.LastApplied)
            {
                Assert.That(runtime.TargetHealth, Is.LessThan(previousHealth));
                sawHealthDrop = true;
            }
            else
            {
                Assert.That(runtime.TargetHealth, Is.EqualTo(previousHealth).Within(0.01f));
            }
        }

        string joined = string.Join('\n', details);
        Assert.Multiple(() =>
        {
            Assert.That(joined, Does.Contain("伤害倍率"));
            Assert.That(joined, Does.Contain("衰减"));
            Assert.That(joined, Does.Contain("钳制"));
            Assert.That(joined, Does.Contain("负面修正翻成正数再加算"));
            Assert.That(joined, Does.Contain("出手许可：开"));
            Assert.That(joined, Does.Contain("出手许可：关"));
            Assert.That(joined, Does.Contain("射程判定：够得着"));
            Assert.That(joined, Does.Contain("射程判定：够不着"));
            Assert.That(runtime.LastDamage, Is.GreaterThanOrEqualTo(0f));
            Assert.That(runtime.LastDamage, Is.LessThanOrEqualTo(80f));
            Assert.That(runtime.Metrics.ThinkWaves, Is.GreaterThan(0));
            Assert.That(sawPermitOn, Is.True);
            Assert.That(sawPermitOff, Is.True);
            Assert.That(sawInRange, Is.True);
            Assert.That(sawOutOfRange, Is.True);
            Assert.That(sawHealthDrop, Is.True, "Player should see the health bar drop when a permitted in-range hit lands.");
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
        GraphControlFlowCompileResult compiled = GraphOpsFloatGraphAuthoring.CompileValidationGraph(distance: 30f, permit: true);
        Assert.That(compiled.Succeeded, Is.True);

        var emitted = compiled.Program.Select(i => (GraphNodeOp)i.Op).ToHashSet();
        Assert.Multiple(() =>
        {
            Assert.That(emitted, Does.Contain(GraphNodeOp.ConstBool));
            Assert.That(emitted, Does.Contain(GraphNodeOp.ClampFloat));
            Assert.That(emitted, Does.Contain(GraphNodeOp.CompareGtFloat));
        });

        GraphInstruction permit = compiled.Program.Single(i => i.Op == (ushort)GraphNodeOp.ConstBool);
        Assert.That(permit.Imm, Is.EqualTo(1));

        GraphControlFlowCompileResult closed = GraphOpsFloatGraphAuthoring.CompileValidationGraph(distance: 30f, permit: false);
        GraphInstruction closedPermit = closed.Program.Single(i => i.Op == (ushort)GraphNodeOp.ConstBool);
        Assert.That(closedPermit.Imm, Is.EqualTo(0));
    }

    private static void AssertBannedEnglish(string detail)
    {
        Assert.That(detail, Does.Not.Contain("tally"));
        Assert.That(detail, Does.Not.Contain("Validation"));
        Assert.That(detail, Does.Not.Contain("FuncLib"));
        Assert.That(detail, Does.Not.Contain("True"));
        Assert.That(detail, Does.Not.Contain("False"));
    }
}
