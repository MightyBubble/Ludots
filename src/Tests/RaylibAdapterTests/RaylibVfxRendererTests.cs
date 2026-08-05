using System.Numerics;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibVfxRendererTests
{
    [Test]
    public void TryBuildDirectVfxVisual_PerformerVfxPrimitive_BuildsLoopEffectVisual()
    {
        var primitive = new PrimitiveDrawItem
        {
            MeshAssetId = 42,
            Position = new Vector3(1f, 2f, 3f),
            Rotation = Quaternion.Identity,
            Scale = new Vector3(2f, 3f, 4f),
            Color = new Vector4(0.3f, 0.4f, 0.5f, 0.6f),
            StableId = 99,
            RenderPath = VisualRenderPath.StaticMesh,
            AssetKind = AssetKind.VFX,
            Mobility = VisualMobility.Movable,
            Visibility = VisualVisibility.Visible,
        };

        bool handled = RaylibPrimitiveRenderer.TryBuildDirectVfxVisual(
            in primitive,
            scaleMul: 0.5f,
            out PrefabFinalizedVisual visual);

        Assert.That(handled, Is.True);
        Assert.That(visual.Kind, Is.EqualTo(PrefabVisualPartKind.Vfx));
        Assert.That(visual.StableId, Is.EqualTo(99));
        Assert.That(visual.EffectAssetId, Is.EqualTo(42));
        Assert.That(visual.VfxSpawnMode, Is.EqualTo(PrefabVfxSpawnMode.Loop));
        Assert.That(visual.Scale, Is.EqualTo(new Vector3(1f, 1.5f, 2f)));
    }

    [Test]
    public void ComposeEffectKey_UsesFullStableAndEffectIdentity()
    {
        RaylibVfxEffectKey first = RaylibVfxRenderer.ComposeEffectKey(17, 42);
        RaylibVfxEffectKey same = RaylibVfxRenderer.ComposeEffectKey(17, 42);
        RaylibVfxEffectKey differentEffect = RaylibVfxRenderer.ComposeEffectKey(17, 43);

        Assert.That(first, Is.EqualTo(same));
        Assert.That(first, Is.Not.EqualTo(differentEffect));
    }

    [Test]
    public void BuildEmitterPlan_PrimitiveSphereLoop_ProducesAnimatedParticlePlan()
    {
        PrefabFinalizedVisual visual = PrefabFinalizedVisual.Vfx(
            stableId: 17,
            position: new Vector3(1f, 2f, 3f),
            rotation: Quaternion.Identity,
            scale: new Vector3(2f, 1f, 1f),
            color: new Vector4(0.35f, 0.95f, 1f, 0.85f),
            effectAssetId: 42,
            spawnMode: PrefabVfxSpawnMode.Loop);
        MeshAssetDescriptor descriptor = CreateSphereEffectDescriptor(42);

        RaylibVfxEmitterPlan plan = RaylibVfxRenderer.BuildEmitterPlan(
            in visual,
            in descriptor,
            ageSeconds: 1.25d);

        Assert.That(plan.Shape, Is.EqualTo(VfxEmitterShape.PrimitiveSphere));
        Assert.That(plan.SpawnMode, Is.EqualTo(PrefabVfxSpawnMode.Loop));
        Assert.That(plan.ParticleCount, Is.EqualTo(24));
        Assert.That(plan.RingSegments, Is.EqualTo(20));
        Assert.That(plan.ShellRadius, Is.GreaterThan(0f));
        Assert.That(plan.OrbitPhase, Is.GreaterThan(0f));
        Assert.That(plan.CoreColor.W, Is.GreaterThan(0f));
        Assert.That(plan.Life01, Is.EqualTo(0f));
    }

    [Test]
    public void BuildEmitterPlan_Once_FadesOutByLifetime()
    {
        PrefabFinalizedVisual visual = PrefabFinalizedVisual.Vfx(
            stableId: 23,
            position: Vector3.Zero,
            rotation: Quaternion.Identity,
            scale: Vector3.One,
            color: Vector4.One,
            effectAssetId: 77,
            spawnMode: PrefabVfxSpawnMode.Once);
        MeshAssetDescriptor descriptor = CreateSphereEffectDescriptor(77);

        RaylibVfxEmitterPlan plan = RaylibVfxRenderer.BuildEmitterPlan(
            in visual,
            in descriptor,
            ageSeconds: 1.0d);

        Assert.That(plan.Life01, Is.EqualTo(1f));
        Assert.That(plan.CoreColor.W, Is.EqualTo(0f));
        Assert.That(plan.ShellColor.W, Is.EqualTo(0f));
        Assert.That(plan.ParticleColor.W, Is.EqualTo(0f));
    }

    [Test]
    public void BuildEmitterPlan_RejectsEffectAssetWithoutEmitterData()
    {
        PrefabFinalizedVisual visual = PrefabFinalizedVisual.Vfx(
            stableId: 31,
            position: Vector3.Zero,
            rotation: Quaternion.Identity,
            scale: Vector3.One,
            color: Vector4.One,
            effectAssetId: 88,
            spawnMode: PrefabVfxSpawnMode.Loop);
        MeshAssetDescriptor descriptor = MeshAssetDescriptor.Billboard(88);

        Assert.That(
            () => RaylibVfxRenderer.BuildEmitterPlan(in visual, in descriptor, ageSeconds: 0d),
            Throws.InvalidOperationException.With.Message.Contains("must declare vfx emitter data"));
    }

    [Test]
    public void BuildEmitterPlan_PrimitiveCube_UsesAuthoredShapeAndParticleBudget()
    {
        PrefabFinalizedVisual visual = PrefabFinalizedVisual.Vfx(
            stableId: 41,
            position: Vector3.Zero,
            rotation: Quaternion.Identity,
            scale: Vector3.One,
            color: Vector4.One,
            effectAssetId: 91,
            spawnMode: PrefabVfxSpawnMode.Loop);
        MeshAssetDescriptor descriptor = MeshAssetDescriptor.Primitive(91, PrimitiveMeshKind.Cube);
        descriptor.VfxEffectData = new VfxEffectAssetData(new VfxEmitterDescriptor(
            VfxEmitterShape.PrimitiveCube,
            particleCount: 9,
            ringSegments: 11,
            radiusScale: 0.8f,
            coreRadiusScale: 0.2f,
            particleRadiusScale: 0.06f,
            lifetimeSeconds: 0.5f,
            pulseSpeedRadPerSecond: 3.2f,
            orbitSpeedRadPerSecond: 1.1f));

        RaylibVfxEmitterPlan plan = RaylibVfxRenderer.BuildEmitterPlan(
            in visual,
            in descriptor,
            ageSeconds: 0.2d);

        Assert.That(plan.Shape, Is.EqualTo(VfxEmitterShape.PrimitiveCube));
        Assert.That(plan.ParticleCount, Is.EqualTo(9));
        Assert.That(plan.RingSegments, Is.EqualTo(11));
    }

    private static MeshAssetDescriptor CreateSphereEffectDescriptor(int id)
    {
        MeshAssetDescriptor descriptor = MeshAssetDescriptor.Primitive(id, PrimitiveMeshKind.Sphere);
        descriptor.VfxEffectData = new VfxEffectAssetData(new VfxEmitterDescriptor(
            VfxEmitterShape.PrimitiveSphere,
            particleCount: 24,
            ringSegments: 20,
            radiusScale: 1.15f,
            coreRadiusScale: 0.28f,
            particleRadiusScale: 0.085f,
            lifetimeSeconds: 0.75f,
            pulseSpeedRadPerSecond: 5.2f,
            orbitSpeedRadPerSecond: 1.7f));
        return descriptor;
    }
}
