using System.Numerics;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;
using Raylib_cs;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibRenderEnvironmentConfigTests
{
    [Test]
    public void CreateDefault_ProvidesNormalizedLightingAndEnabledWorldPostProcess()
    {
        RaylibRenderEnvironmentConfig config = RaylibRenderEnvironmentConfig.CreateDefault();

        Assert.That(config.Skybox.Enabled, Is.True);
        Assert.That(config.PostProcess.Enabled, Is.True);
        Assert.That(config.Lighting.SunDirection.Length(), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(config.Lighting.FogFarMeters, Is.GreaterThan(config.Lighting.FogNearMeters));
        Assert.That(config.Water.WaveAmplitudeMeters, Is.GreaterThan(0f));
    }

    [Test]
    public void NormalizeAndValidate_RejectsInvertedFogRange()
    {
        RaylibRenderEnvironmentConfig config = RaylibRenderEnvironmentConfig.CreateDefault() with
        {
            Lighting = RaylibLightingConfig.CreateDefault() with
            {
                FogNearMeters = 2000f,
                FogFarMeters = 1000f
            }
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => config.NormalizeAndValidate());
    }

    [Test]
    public void VisualHeightmapRenderProfile_DisablesWaterByDefaultForFlatShowcaseMaps()
    {
        VisualHeightmapRenderProfile profile = VisualHeightmapRenderProfile.CreateDefault();

        float effectiveSeaLevelCm = RaylibVisualHeightmapRenderer.ResolveEffectiveSeaLevelCm(profile, minHeightCm: 0f);

        Assert.That(profile.WaterEnabled, Is.False);
        Assert.That(effectiveSeaLevelCm, Is.LessThan(0f));
    }

    [Test]
    public void VisualHeightmapRenderProfile_WhenWaterEnabled_UsesAuthoredSeaLevel()
    {
        var profile = new VisualHeightmapRenderProfile
        {
            WaterEnabled = true,
            SeaLevelCm = 125f,
            DisplayHeightScale = 250f,
            ColorContrast = 1.35f
        };

        float effectiveSeaLevelCm = RaylibVisualHeightmapRenderer.ResolveEffectiveSeaLevelCm(profile, minHeightCm: -600f);

        Assert.That(effectiveSeaLevelCm, Is.EqualTo(125f));
    }

    [Test]
    public void VisualHeightmapRenderProfile_RejectsDisplayScaleOutsideCoreRange()
    {
        var profile = new VisualHeightmapRenderProfile
        {
            DisplayHeightScale = VisualHeightmapRenderProfile.MaxDisplayHeightScale + 1f
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => profile.NormalizeAndValidate());
    }

    [Test]
    public void BuildTextureSourceRectangle_FlipsRenderTextureForWindowComposite()
    {
        Texture2D texture = new()
        {
            width = 1280,
            height = 720
        };

        Rectangle source = RaylibPostProcessRenderer.BuildTextureSourceRectangle(texture);

        Assert.That(source.x, Is.EqualTo(0f));
        Assert.That(source.y, Is.EqualTo(0f));
        Assert.That(source.width, Is.EqualTo(1280f));
        Assert.That(source.height, Is.EqualTo(-720f));
    }

    [Test]
    public void NeedsRenderTargetResize_TracksWindowSizeExactly()
    {
        Assert.That(RaylibPostProcessRenderer.NeedsRenderTargetResize(false, 0, 0, 1280, 720), Is.True);
        Assert.That(RaylibPostProcessRenderer.NeedsRenderTargetResize(true, 1280, 720, 1280, 720), Is.False);
        Assert.That(RaylibPostProcessRenderer.NeedsRenderTargetResize(true, 1280, 720, 1600, 900), Is.True);
    }
}
