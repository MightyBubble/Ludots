using System.Numerics;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Particles;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;
using Raylib_cs;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibVfxRendererTests
{
    [Test]
    public void TryBuildDirectVfxVisual_PerformerVfxPrimitive_UsesAuthoredEffectSpawnMode()
    {
        var meshes = new MeshAssetRegistry();
        MeshAssetDescriptor descriptor = CreateSphereEffectDescriptor(PrefabVfxSpawnMode.Loop);
        int effectAssetId = meshes.Register("effect.looping.smoke", in descriptor);
        var primitive = new PrimitiveDrawItem
        {
            MeshAssetId = effectAssetId,
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
            meshes,
            scaleMul: 0.5f,
            out PrefabFinalizedVisual visual);

        Assert.That(handled, Is.True);
        Assert.That(visual.Kind, Is.EqualTo(PrefabVisualPartKind.Vfx));
        Assert.That(visual.StableId, Is.EqualTo(99));
        Assert.That(visual.EffectAssetId, Is.EqualTo(effectAssetId));
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
    public void Draw_EffectAssetWithoutParticlePayload_ThrowsExplicitQuarksReferenceError()
    {
        var meshes = new MeshAssetRegistry();
        MeshAssetDescriptor descriptor = MeshAssetDescriptor.Primitive(0, PrimitiveMeshKind.Sphere);
        int effectAssetId = meshes.Register("effect.missing.particle", in descriptor);
        PrefabFinalizedVisual visual = CreateVisual(effectAssetId, stableId: 31);
        var renderer = new RaylibVfxRenderer();
        renderer.BeginFrame();

        Assert.That(
            () => renderer.Draw(in visual, meshes, CreateCamera(), timeSeconds: 0d),
            Throws.InvalidOperationException.With.Message.Contains("registered Quarks particle effect"));
    }

    [Test]
    public void ToRaylibBlendMode_MapsAuthoredQuarksBlendModes()
    {
        Assert.That(RaylibVfxRenderer.ToRaylibBlendMode(ParticleBlendMode.Alpha), Is.EqualTo(BlendMode.BLEND_ALPHA));
        Assert.That(RaylibVfxRenderer.ToRaylibBlendMode(ParticleBlendMode.Additive), Is.EqualTo(BlendMode.BLEND_ADDITIVE));
        Assert.That(RaylibVfxRenderer.ToRaylibBlendMode(ParticleBlendMode.PremultipliedAlpha), Is.EqualTo(BlendMode.BLEND_ALPHA_PREMULTIPLY));
        Assert.That(RaylibVfxRenderer.ToRaylibBlendMode(ParticleBlendMode.Multiply), Is.EqualTo(BlendMode.BLEND_MULTIPLIED));
    }

    [Test]
    public void BuildTextureSourceRectangle_MapsFrameIndexIntoAuthoredSheetGrid()
    {
        var texture = new Texture2D
        {
            id = 77,
            width = 256,
            height = 128,
        };
        var sheet = new ParticleTextureSheetAsset(
            "effect.quarks.fire.sheet",
            columns: 4,
            rows: 2,
            frameCount: 8,
            framesPerSecond: 16f,
            new ParticleIntRange(0, 0),
            ParticleTextureSheetPlaybackMode.Loop);

        Rectangle frame = RaylibVfxRenderer.BuildTextureSourceRectangle(texture, sheet, frameIndex: 5);

        Assert.That(frame.x, Is.EqualTo(64f));
        Assert.That(frame.y, Is.EqualTo(64f));
        Assert.That(frame.width, Is.EqualTo(64f));
        Assert.That(frame.height, Is.EqualTo(64f));
    }

    [Test]
    public void BuildTextureSourceRectangle_RejectsTextureSizeThatDoesNotMatchSheetGrid()
    {
        var texture = new Texture2D
        {
            id = 77,
            width = 250,
            height = 128,
        };
        var sheet = new ParticleTextureSheetAsset(
            "effect.quarks.fire.sheet",
            columns: 4,
            rows: 2,
            frameCount: 8,
            framesPerSecond: 16f,
            new ParticleIntRange(0, 0),
            ParticleTextureSheetPlaybackMode.Loop);

        Assert.That(
            () => RaylibVfxRenderer.BuildTextureSourceRectangle(texture, sheet, frameIndex: 0),
            Throws.InvalidOperationException.With.Message.Contains("divisible"));
    }

    private static PrefabFinalizedVisual CreateVisual(int effectAssetId, int stableId)
    {
        return PrefabFinalizedVisual.Vfx(
            stableId,
            position: Vector3.Zero,
            rotation: Quaternion.Identity,
            scale: Vector3.One,
            color: Vector4.One,
            effectAssetId,
            spawnMode: PrefabVfxSpawnMode.Loop);
    }

    private static MeshAssetDescriptor CreateSphereEffectDescriptor(PrefabVfxSpawnMode spawnMode)
    {
        MeshAssetDescriptor descriptor = MeshAssetDescriptor.Primitive(0, PrimitiveMeshKind.Sphere);
        descriptor.VfxEffectData = new VfxEffectAssetData(
                CreateParticleEffect(ParticleRenderMode.Primitive, spawnMode),
                particleEffectAssetId: 12);
        return descriptor;
    }

    private static ParticleEffectAssetData CreateParticleEffect(
        ParticleRenderMode renderMode,
        PrefabVfxSpawnMode spawnMode = PrefabVfxSpawnMode.Loop)
    {
        return new ParticleEffectAssetData(
            spawnMode,
            ParticleEmitterShapeKind.Cone,
            renderMode,
            ParticleBlendMode.Alpha,
            ParticlePrimitiveKind.Sphere,
                maxParticles: 16,
            seed: 9876u,
            durationSeconds: 1f,
            emissionRatePerSecond: 12f,
            burstCount: 4,
            shapeRadius: 0.2f,
            shapeAngleRadians: 0.35f,
            shapeThickness: 0.8f,
            new ParticleValueRange(0.5f, 0.8f),
            new ParticleValueRange(0.6f, 1.4f),
            new ParticleValueRange(0.08f, 0.12f),
            new Vector4(1f, 0.8f, 0.3f, 1f),
            new ParticleScalarCurve(
                new[]
                {
                    new ParticleCurveKey(0f, 1f),
                    new ParticleCurveKey(1f, 0.1f),
                }),
            new ParticleColorGradient(
                new[]
                {
                    new ParticleColorKey(0f, Vector4.One),
                    new ParticleColorKey(1f, new Vector4(0.2f, 0.4f, 1f, 0f)),
                }),
            new Vector3(0f, 0.2f, 0f),
            drag: 0.05f,
            worldSpace: true,
            textureSheet: null,
            stretchedLengthScale: 0f,
                trailLengthSeconds: 0f);
    }

    private static Camera3D CreateCamera()
    {
        return new Camera3D
        {
            position = new Vector3(0f, 4f, 8f),
            target = Vector3.Zero,
            up = Vector3.UnitY,
            fovy = 45f,
            projection = CameraProjection.CAMERA_PERSPECTIVE,
        };
    }
}
