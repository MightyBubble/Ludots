using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryMapVarAcceptanceTests
{
    [Test]
    public void ReadMapVarIntVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ReadMapVarInt");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"ReadMapVarInt detail missing phrase: {runtime.Metrics.Detail}");
        }
    }

    [Test]
    public void ReadMapVarFloatVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ReadMapVarFloat");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"ReadMapVarFloat detail missing phrase: {runtime.Metrics.Detail}");
        }
    }

    [Test]
    public void WriteMapVarIntVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("WriteMapVarInt");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"WriteMapVarInt detail missing phrase: {runtime.Metrics.Detail}");
        }
    }

    [Test]
    public void WriteMapVarFloatVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("WriteMapVarFloat");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"WriteMapVarFloat detail missing phrase: {runtime.Metrics.Detail}");
        }
    }
}
