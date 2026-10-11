using Arch.Core;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using Ludots.Platform.Abstractions;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryCastModeAcceptanceTests
{
    [Test]
    public void IsCastModeVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("IsCastMode");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "IsCastMode");
    }

    [Test]
    public void SetCastModeVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("SetCastMode");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "SetCastMode");

        Entity rep = runtime.Context.Target;
        int modeId = RequireGalleryCastMode(runtime);
        Assert.That(runtime.Context.SimWorld.Get<InteractionPref>(rep).DefaultCastModeId, Is.EqualTo(modeId));
    }

    [Test]
    public void SetTargetPosVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("SetTargetPos");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "SetTargetPos");
        Assert.That(runtime.Context.TargetPosCm, Is.EqualTo(new IntVector2(160, -80)));
    }

    [Test]
    public void LoadAbilityCastRangeVignette_QuotesTheAuthoredCentimeters()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadAbilityCastRange");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "LoadAbilityCastRange");
    }

    private static void AssertDetail(GraphOpsNodeGalleryRuntime runtime, string op)
    {
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"{op} detail missing phrase: {runtime.Metrics.Detail}");
        }
    }

    private static int RequireGalleryCastMode(GraphOpsNodeGalleryRuntime runtime)
    {
        object raw = runtime.Context.Globals![CoreServiceKeys.CastModeIdRegistry.Name];
        var modes = (StringIntRegistry)raw;
        Assert.That(modes.TryGetId("castMode.gallery", out int modeId), Is.True);
        return modeId;
    }
}
