using System.Numerics;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Particles;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;
using Raylib_cs;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibVfxRendererTests
{
    [Test]
    public void Draw_RejectsNonVfxPresentationItems()
    {
        var meshes = new MeshAssetRegistry();
        MeshAssetDescriptor descriptor = CreateSphereEffectDescriptor(ParticleVfxSpawnMode.Loop);
        int effectAssetId = meshes.Register("effect.looping.smoke", in descriptor);
        PrimitiveDrawItem meshItem = CreateVfxItem(effectAssetId, stableId: 99);
        meshItem.AssetKind = AssetKind.Mesh;
        var renderer = new RaylibVfxRenderer();
        renderer.BeginFrame();

        Assert.That(
            () => renderer.Draw(in meshItem, meshes, CreateCamera(), timeSeconds: 0d),
            Throws.InvalidOperationException.With.Message.Contains("AssetKind"));
    }

    [Test]
    public void ComposeVfxKey_UsesFullStableAndEffectIdentity()
    {
        RaylibVfxKey first = RaylibVfxRenderer.ComposeVfxKey(17, 42);
        RaylibVfxKey same = RaylibVfxRenderer.ComposeVfxKey(17, 42);
        RaylibVfxKey differentVfx = RaylibVfxRenderer.ComposeVfxKey(17, 43);

        Assert.That(first, Is.EqualTo(same));
        Assert.That(first, Is.Not.EqualTo(differentVfx));
    }

    [Test]
    public void Draw_EffectAssetWithoutParticlePayload_ThrowsExplicitQuarksReferenceError()
    {
        var meshes = new MeshAssetRegistry();
        MeshAssetDescriptor descriptor = MeshAssetDescriptor.Primitive(0, PrimitiveMeshKind.Sphere);
        int effectAssetId = meshes.Register("effect.missing.particle", in descriptor);
        PrimitiveDrawItem visual = CreateVfxItem(effectAssetId, stableId: 31);
        var renderer = new RaylibVfxRenderer();
        renderer.BeginFrame();

        Assert.That(
            () => renderer.Draw(in visual, meshes, CreateCamera(), timeSeconds: 0d),
            Throws.InvalidOperationException.With.Message.Contains("registered Quarks particle VFX"));
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

    private static PrimitiveDrawItem CreateVfxItem(int effectAssetId, int stableId)
    {
        return new PrimitiveDrawItem
        {
            MeshAssetId = effectAssetId,
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One,
            Color = Vector4.One,
            StableId = stableId,
            RenderPath = VisualRenderPath.StaticMesh,
            AssetKind = AssetKind.VFX,
            Mobility = VisualMobility.Movable,
            Visibility = VisualVisibility.Visible,
        };
    }

    private static MeshAssetDescriptor CreateSphereEffectDescriptor(ParticleVfxSpawnMode spawnMode)
    {
        MeshAssetDescriptor descriptor = MeshAssetDescriptor.Primitive(0, PrimitiveMeshKind.Sphere);
        descriptor.VfxData = new VfxAssetData(
                CreateParticleVfx(ParticleRenderMode.Primitive, spawnMode),
                particleVfxAssetId: 12);
        return descriptor;
    }

    private static ParticleVfxAssetData CreateParticleVfx(
        ParticleRenderMode renderMode,
        ParticleVfxSpawnMode spawnMode = ParticleVfxSpawnMode.Loop)
    {
        return new ParticleVfxAssetData(
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
