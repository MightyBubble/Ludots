using System.Numerics;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;
using Raylib_cs;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;

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
    public void ContinuousHeightmapRenderProfile_DisablesWaterByDefaultForFlatShowcaseMaps()
    {
        ContinuousHeightmapRenderProfile profile = ContinuousHeightmapRenderProfile.CreateDefault();

        float effectiveSeaLevelCm = RaylibContinuousHeightmapRenderer.ResolveEffectiveSeaLevelCm(profile, minHeightCm: 0f);

        Assert.That(profile.WaterEnabled, Is.False);
        Assert.That(effectiveSeaLevelCm, Is.LessThan(0f));
    }

    [Test]
    public void ContinuousHeightmapRenderProfile_WhenWaterEnabled_UsesAuthoredSeaLevel()
    {
        var profile = new ContinuousHeightmapRenderProfile
        {
            WaterEnabled = true,
            SeaLevelCm = 125f,
            DisplayHeightScale = 250f,
            ColorContrast = 1.35f
        };

        float effectiveSeaLevelCm = RaylibContinuousHeightmapRenderer.ResolveEffectiveSeaLevelCm(profile, minHeightCm: -600f);

        Assert.That(effectiveSeaLevelCm, Is.EqualTo(125f));
    }

    [Test]
    public void ContinuousHeightmapRenderProfile_RejectsDisplayScaleOutsideCoreRange()
    {
        var profile = new ContinuousHeightmapRenderProfile
        {
            DisplayHeightScale = ContinuousHeightmapRenderProfile.MaxDisplayHeightScale + 1f
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

    [Test]
    public void RaylibSkyboxConfig_ValidatesSunDiskParams()
    {
        RaylibSkyboxConfig defaults = RaylibSkyboxConfig.CreateDefault();
        Assert.That(defaults.SunDiskSharpness, Is.EqualTo(720f));
        Assert.That(defaults.SunDiskIntensity, Is.EqualTo(2.4f));
        Assert.That(defaults.SunGlowSharpness, Is.EqualTo(22f));
        Assert.That(defaults.SunGlowIntensity, Is.EqualTo(0.34f));

        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with { SunDiskSharpness = invalid }).Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with { SunDiskIntensity = invalid }).Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with { SunGlowSharpness = invalid }).Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with { SunGlowIntensity = invalid }).Validate());
        }
    }
}
