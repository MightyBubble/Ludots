using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Systems;
using Ludots.Presentation.Skia;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class HudFrameConsistencyTests
{
    [TestCase(1000, false)]
    [TestCase(5000, false)]
    [TestCase(10000, false)]
    [TestCase(1000, true)]
    [TestCase(5000, true)]
    [TestCase(10000, true)]
    public void WorldHud_CameraRotationAndHold_DrawsCurrentProjectedPositions(int count, bool updateValues)
    {
        var world = World.Create();
        try
        {
            var worldHud = new WorldHudBatchBuffer(count * 2);
            var screenHud = new ScreenHudBatchBuffer(count * 2);
            var view = new FixedView();
            var camera = new CameraManager();
            camera.ApplyPose(new CameraPoseRequest { DistanceCm = 16000, Pitch = 50, TargetCm = Vector2.Zero });
            var projector = new CoreScreenProjector(camera, view);
            using var projection = new WorldHudToScreenSystem(world, worldHud, null, projector, view, screenHud);
            var builder = new PresentationOverlaySceneBuilder(screenHud, null, null, null, null);
            var scene = new PresentationOverlayScene(count * 2);
            var pacer = new PresentationOverlayLanePacer(PresentationOverlayLayer.UnderUi);
            using var renderer = new SkiaOverlayRenderer();
            using var actual = SKSurface.Create(new SKImageInfo(640, 480));
            using var expected = SKSurface.Create(new SKImageInfo(640, 480));
            int side = (int)Math.Ceiling(Math.Sqrt(count));
            for (int i = 0; i < count; i++)
            {
                var position = new Vector3((i % side - side / 2) * 3, 0, (i / side - side / 2) * 3);
                worldHud.TryAdd(new WorldHudItem
                {
                    StableId = i * 2 + 1, DirtySerial = 1, Kind = WorldHudItemKind.Bar,
                    WorldPosition = position, Width = 12, Height = 3, Value0 = 0.5f,
                    Color0 = Vector4.One, Color1 = new Vector4(0, 1, 0, 1),
                });
                worldHud.TryAdd(new WorldHudItem
                {
                    StableId = i * 2 + 2, DirtySerial = 1, Kind = WorldHudItemKind.Text,
                    WorldPosition = position + Vector3.UnitY * 2, FontSize = 12,
                    Color0 = Vector4.One, Value0 = 1, Id1 = (int)WorldHudValueMode.Constant,
                });
            }

            int[] yaws = { 45, 45, 46, 47, 48, 49, 50, 55, 55, 65, 65, 45, 45 };
            for (int frame = 0; frame < yaws.Length; frame++)
            {
                if (updateValues)
                {
                    for (int i = 0; i < count; i++)
                    {
                        Assert.That(worldHud.TryGetByStableId(i * 2 + 1, out var bar), Is.True);
                        bar.DirtySerial = frame + 2;
                        bar.Value0 = (frame + 1) / 20f;
                        Assert.That(worldHud.TryAdd(in bar), Is.True);
                        Assert.That(worldHud.TryGetByStableId(i * 2 + 2, out var text), Is.True);
                        text.DirtySerial = frame + 2;
                        text.Value0 = frame + 2;
                        Assert.That(worldHud.TryAdd(in text), Is.True);
                    }
                }
                camera.ApplyPose(new CameraPoseRequest { Yaw = yaws[frame] });
                projection.Update(1f / 60);
                builder.Build(scene);
                Assert.That(screenHud.TextCount, Is.GreaterThan(48));
                if (updateValues)
                {
                    foreach (var text in screenHud.GetTextSpan())
                    {
                        Assert.That(text.Value0, Is.EqualTo(frame + 2), $"text content frame={frame}");
                    }
                    foreach (var bar in screenHud.GetBarSpan())
                    {
                        Assert.That(bar.Value0, Is.EqualTo((frame + 1) / 20f), $"bar content frame={frame}");
                    }
                }
                var plan = pacer.BuildPlan(scene);
                actual.Canvas.Clear(SKColors.Transparent);
                renderer.Render(scene, actual.Canvas, PresentationOverlayLayer.UnderUi, plan);
                pacer.MarkPresented(scene, plan);
                var referenceScene = new PresentationOverlayScene(count * 2);
                new PresentationOverlaySceneBuilder(screenHud, null, null, null, null).Build(referenceScene);
                foreach (var kind in new[] { PresentationOverlayItemKind.Bar, PresentationOverlayItemKind.Text })
                {
                    var items = new System.Collections.Generic.Dictionary<int, PresentationOverlayItem>();
                    foreach (var item in referenceScene.GetLaneSpan(PresentationOverlayLayer.UnderUi, kind))
                    {
                        items.Add(item.StableId, item);
                    }
                    Assert.That(scene.GetLaneSpan(PresentationOverlayLayer.UnderUi, kind).Length, Is.EqualTo(items.Count));
                    foreach (var item in scene.GetLaneSpan(PresentationOverlayLayer.UnderUi, kind))
                    {
                        Assert.That(item, Is.EqualTo(items[item.StableId]),
                            $"camera-{count}-frame-{frame} {kind} stableId={item.StableId}");
                    }
                }
                using var referenceRenderer = new SkiaOverlayRenderer();
                expected.Canvas.Clear(SKColors.Transparent);
                referenceRenderer.Render(scene, expected.Canvas, PresentationOverlayLayer.UnderUi,
                    new PresentationOverlayLanePacer.LaneRefreshPlan(byte.MaxValue));
                AssertSamePixels(actual, expected, $"camera-{count}-frame-{frame}");
            }
        }
        finally
        {
            World.Destroy(world);
        }
    }

    [TestCase(1000)]
    [TestCase(5000)]
    [TestCase(10000)]
    public void RetainedText_MoveThenHold_MatchesCurrentFrame(int count)
    {
        var scene = new PresentationOverlayScene(count);
        var pacer = new PresentationOverlayLanePacer(PresentationOverlayLayer.UnderUi);
        using var renderer = new SkiaOverlayRenderer();
        using var actual = SKSurface.Create(new SKImageInfo(640, 480));
        using var expected = SKSurface.Create(new SKImageInfo(640, 480));
        int[] offsets = { 0, 0, 40, 40, 80, 80, 0, 0 };
        for (int frame = 0; frame < offsets.Length; frame++)
        {
            scene.BeginBuild();
            for (int i = 0; i < count; i++)
            {
                scene.TryAddText(PresentationOverlayLayer.UnderUi,
                    10 + (i % 32) * 17 + offsets[frame], 10 + (i / 32) * 18,
                    "1", 12, Vector4.One, i + 1, i + 1);
            }
            scene.EndBuild();
            var plan = pacer.BuildPlan(scene);
            actual.Canvas.Clear(SKColors.Transparent);
            renderer.Render(scene, actual.Canvas, PresentationOverlayLayer.UnderUi, plan);
            pacer.MarkPresented(scene, plan);
            using var reference = new SkiaOverlayRenderer();
            expected.Canvas.Clear(SKColors.Transparent);
            reference.Render(scene, expected.Canvas, PresentationOverlayLayer.UnderUi,
                new PresentationOverlayLanePacer.LaneRefreshPlan(byte.MaxValue));
            AssertSamePixels(actual, expected, $"move-hold-{count}-frame-{frame}");
        }
    }

    [TestCase(PresentationOverlayItemKind.Text)]
    [TestCase(PresentationOverlayItemKind.Bar)]
    public void RetainedLane_PartialMovement_DoesNotTranslateStationaryItems(PresentationOverlayItemKind kind)
    {
        const int count = 64;
        var hud = new ScreenHudBatchBuffer(count);
        var builder = new PresentationOverlaySceneBuilder(hud, null, null, null, null);
        var scene = new PresentationOverlayScene(count);
        using var renderer = new SkiaOverlayRenderer();
        using var actual = SKSurface.Create(new SKImageInfo(640, 480));
        using var expected = SKSurface.Create(new SKImageInfo(640, 480));
        for (int frame = 0; frame < 4; frame++)
        {
            hud.BeginProjectedBuild();
            for (int i = 0; i < count; i++)
            {
                float x = 10 + (i % 8) * 65 + (frame < 3 || i % 2 == 0 ? frame * 5 : 10);
                float y = 10 + (i / 8) * 45;
                if (kind == PresentationOverlayItemKind.Text)
                {
                    hud.TryUpsertProjectedText(new ScreenHudTextItem
                    {
                        StableId = i + 1, DirtySerial = i + 1, ScreenX = x, ScreenY = y,
                        FontSize = 12, Color0 = Vector4.One, Value0 = 1,
                        Id1 = (int)WorldHudValueMode.Constant,
                    }, i);
                }
                else
                {
                    hud.TryUpsertProjectedBar(new ScreenHudBarItem
                    {
                        StableId = i + 1, DirtySerial = i + 1, ScreenX = x, ScreenY = y,
                        Width = 30, Height = 4, Color0 = Vector4.One, Color1 = Vector4.One, Value0 = 1,
                    }, i);
                }
            }
            hud.EndProjectedBuild();
            builder.Build(scene);
            Assert.That(scene.GetLaneSpan(PresentationOverlayLayer.UnderUi, kind).Length, Is.EqualTo(count));
            actual.Canvas.Clear(SKColors.Transparent);
            renderer.Render(scene, actual.Canvas, PresentationOverlayLayer.UnderUi);
            using var reference = new SkiaOverlayRenderer();
            expected.Canvas.Clear(SKColors.Transparent);
            reference.Render(scene, expected.Canvas, PresentationOverlayLayer.UnderUi);
            AssertSamePixels(actual, expected, $"partial-{kind}-frame-{frame}");
        }
    }

    private static void AssertSamePixels(SKSurface actual, SKSurface expected, string name)
    {
        using var actualImage = actual.Snapshot();
        using var expectedImage = expected.Snapshot();
        using var actualBitmap = SKBitmap.FromImage(actualImage);
        using var expectedBitmap = SKBitmap.FromImage(expectedImage);
        bool equal = actualBitmap.GetPixelSpan().SequenceEqual(expectedBitmap.GetPixelSpan());
        if (!equal)
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "hud-frame-consistency");
            Directory.CreateDirectory(directory);
            using var actualPng = actualImage.Encode(SKEncodedImageFormat.Png, 100);
            using var expectedPng = expectedImage.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(directory, name + "-actual.png"), actualPng.ToArray());
            File.WriteAllBytes(Path.Combine(directory, name + "-expected.png"), expectedPng.ToArray());
        }
        Assert.That(equal, Is.True, $"{name}: retained HUD must draw the same positions as a fresh render of this frame");
    }

    private sealed class FixedView : IViewController
    {
        public Vector2 Resolution => new(640, 480);
        public float Fov => 60;
        public float AspectRatio => Resolution.X / Resolution.Y;
    }
}
