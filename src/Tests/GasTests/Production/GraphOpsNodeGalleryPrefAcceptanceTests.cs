using Arch.Core;
using CapabilityStandardGraphOpsNodeGalleryMod.Runtime;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using Ludots.Platform.Abstractions;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryPrefAcceptanceTests
{
    [Test]
    public void IsPrefVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("IsPref");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "IsPref");
    }

    [Test]
    public void SetPrefVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("SetPref");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "SetPref");

        Entity rep = runtime.Context.Target;
        (int keyId, int valueId) = RequireGalleryPref(runtime);
        Assert.That(runtime.Context.SimWorld.Get<InteractionPref>(rep).ResolvePref(0, keyId), Is.EqualTo(valueId));
    }

    [Test]
    public void ActivateContextFromPrefVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("ActivateContextFromPref");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "ActivateContextFromPref");
    }

    [Test]
    public void DeactivateOwnContextVignette_ExecutesOpWithoutThrow()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("DeactivateOwnContext");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "DeactivateOwnContext");
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
    public void LoadSlotAbilityIdVignette_QuotesTheInstalledAbilityId()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadSlotAbilityId");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "LoadSlotAbilityId");
        int abilityId = AbilityIdRegistry.GetId("Ability.GraphOps.CastRange");
        Assert.That(abilityId, Is.GreaterThan(0));
        Assert.That(runtime.Metrics.Detail, Does.Contain($"技能编号 {abilityId}"));
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

    [Test]
    public void LoadAbilityEffectFloatVignette_QuotesTheAuthoredRadius()
    {
        using var runtime = new GraphOpsNodeGalleryRuntime();
        runtime.BindOp("LoadAbilityEffectFloat");
        runtime.EnsureWorld();
        runtime.Tick(0.35f);
        AssertDetail(runtime, "LoadAbilityEffectFloat");
    }

    private static void AssertDetail(GraphOpsNodeGalleryRuntime runtime, string op)
    {
        foreach (string phrase in runtime.Vignette.AssertDetailContains)
        {
            Assert.That(runtime.Metrics.Detail, Does.Contain(phrase),
                $"{op} detail missing phrase: {runtime.Metrics.Detail}");
        }
    }

    private static (int KeyId, int ValueId) RequireGalleryPref(GraphOpsNodeGalleryRuntime runtime)
    {
        object raw = runtime.Context.Globals![CoreServiceKeys.InteractionPrefCatalog.Name];
        var catalog = (InteractionPrefCatalog)raw;
        int keyId = catalog.Keys.GetId("pref.gallery.sample");
        int valueId = catalog.Values.GetId("interaction.context.gallery.pref");
        Assert.That(keyId, Is.GreaterThan(0));
        Assert.That(valueId, Is.GreaterThan(0));
        return (keyId, valueId);
    }
}
