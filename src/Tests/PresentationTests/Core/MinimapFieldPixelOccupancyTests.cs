using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Minimap;
using Ludots.Tests;
using Ludots.Tests.TestCommon;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class MinimapFieldPixelOccupancyTests
{
    private static readonly string[] EngineMods =
    {
        "LudotsCoreMod",
    };

    [Test]
    public void SameWorldPoint_UnlimitedKeepsEveryMarker_CapOneKeepsSinglePixel()
    {
        using GameEngine engine = CreateEngine();
        Entity viewer = engine.World.Create();
        ClientLocalSeatTestBindings.BindSoleSeat(engine, viewer, 1, "seat.0");

        var markers = new MinimapMarkerBuffer(8);
        var screenMarkers = new MinimapScreenMarkerBuffer(8);
        var color = new Vector4(0.2f, 0.8f, 1f, 1f);
        markers.BeginFrame();
        for (int i = 0; i < 8; i++)
        {
            Assert.That(markers.TryAdd(7001 + i, Entity.Null, 1000f, 0f, in color, 8f), Is.True);
        }

        MinimapRuntime unlimited = CreateRuntime(maxMarkersPerFieldPixel: 0);
        unlimited.Visible = true;
        unlimited.UseRtsFullMapPreset();
        unlimited.Refresh(engine, markers, screenMarkers);
        Assert.That(unlimited.VisibleMarkerCount, Is.EqualTo(8),
            $"unlimited projected {unlimited.VisibleMarkerCount} of 8 coincident markers; bounds={engine.WorldSizeSpec.Bounds.Left},{engine.WorldSizeSpec.Bounds.Top},{engine.WorldSizeSpec.Bounds.Right},{engine.WorldSizeSpec.Bounds.Bottom}");

        MinimapRuntime capped = CreateRuntime(maxMarkersPerFieldPixel: 1);
        capped.Visible = true;
        capped.UseRtsFullMapPreset();
        capped.Refresh(engine, markers, screenMarkers);
        Assert.That(capped.VisibleMarkerCount, Is.EqualTo(1));
        Assert.That(capped.VisibleMarkerCount, Is.LessThan(unlimited.VisibleMarkerCount));
    }

    [Test]
    public void NegativeCap_FailsClosed()
    {
        var config = new MinimapRuntimeConfig
        {
            InitialZoomNormalized = 1f,
            WheelZoomNormalizedStep = 0.08f,
            ButtonZoomNormalizedStep = 0.18f,
            ZoomSliderEnabled = true,
            ModeToggleEnabled = true,
            RotateToggleEnabled = true,
            DebugMarkerSampleCapacity = 8,
            MinZoomExtentMode = MinimapZoomExtentMode.ExplicitCm,
            MinZoomExplicitHalfExtentCm = 750f,
            MaxZoomExtentMode = MinimapZoomExtentMode.ExplicitCm,
            MaxZoomExplicitHalfExtentCm = 5000f,
            MaxMarkersPerFieldPixel = -1,
        };

        Assert.Throws<InvalidOperationException>(() => _ = new MinimapRuntime(config));
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, EngineMods),
            Path.Combine(repoRoot, "assets"));
        return engine;
    }

    private static MinimapRuntime CreateRuntime(int maxMarkersPerFieldPixel)
    {
        return new MinimapRuntime(new MinimapRuntimeConfig
        {
            InitialZoomNormalized = 1f,
            WheelZoomNormalizedStep = 0.08f,
            ButtonZoomNormalizedStep = 0.18f,
            ZoomSliderEnabled = true,
            ModeToggleEnabled = true,
            RotateToggleEnabled = true,
            DebugMarkerSampleCapacity = 8,
            MinZoomExtentMode = MinimapZoomExtentMode.ExplicitCm,
            MinZoomExplicitHalfExtentCm = 750f,
            MaxZoomExtentMode = MinimapZoomExtentMode.ExplicitCm,
            MaxZoomExplicitHalfExtentCm = 5000f,
            MaxMarkersPerFieldPixel = maxMarkersPerFieldPixel,
        });
    }

    private static string FindRepoRoot()
    {
        string current = TestContext.CurrentContext.WorkDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "mods")) &&
                File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current)!;
        }

        throw new DirectoryNotFoundException("Repository root not found from test work directory.");
    }
}
