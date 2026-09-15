using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Systems;
using Ludots.Platform.Abstractions;
using Ludots.Tests.TestCommon;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// 保留 lane 世界文本的幽灵生命周期合同：属主被整体剔除后再回屏，其值绑定文本
    /// （血条 current/base）必须以属主当前位置重新锚定——锚点冻结即残留（#用户观测：
    /// 镜头拉远后 HUD 文本铺满没有单位的空地）。
    /// </summary>
    [TestFixture]
    public sealed class MassNavigationHudGhostLifecycleTests
    {
        private static readonly string[] Mods =
        {
            "LudotsCoreMod", "CoreInputMod", "SelectionInteractionMod",
            "MassNavigationMod", "CapabilityStandardMassNavigationLargeWorld10kMod"
        };

        [Test]
        public void HudPipelineBaselineBenchmark()
        {
            var backend = new TestInputBackend();
            var focusOverride = new CameraCullingFocusOverride();
            using GameEngine engine = CreateEngine(backend, focusOverride);
            WorldHudToScreenSystem hudProjection = CreateHudProjection(engine);
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)
                ?? throw new InvalidOperationException("PresentationScreenHudBuffer missing.");
            var builder = new PresentationOverlaySceneBuilder(
                screenHud,
                engine.GetService(CoreServiceKeys.PresentationWorldHudStrings),
                engine.GetService(CoreServiceKeys.PresentationTextCatalog),
                engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection),
                engine.GetService(CoreServiceKeys.ScreenOverlayBuffer),
                engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer));
            var scene = new PresentationOverlayScene(screenHud.Capacity + ScreenOverlayBuffer.MaxItems);
            var sync = RequirePresentationSystem<Ludots.Core.Presentation.Systems.PresenterEntityTransformSyncSystem>(engine);
            var emit = RequirePresentationSystem<Ludots.Core.Presentation.Systems.PresenterEmitSystem>(engine);

            engine.LoadMap(new MapLoadRequest(new MapId("mass_navigation"),
                MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, null) })));
            TickWithOverlay(engine, hudProjection, builder, scene, 90);

            const int Samples = 60;
            var syncMs = new double[Samples];
            var emitMs = new double[Samples];
            var projMs = new double[Samples];
            var buildMs = new double[Samples];
            for (int frame = 0; frame < 16 + Samples; frame++)
            {
                for (int i = 0; i < 100; i++)
                {
                    engine.World.Get<VisualTransform>(CollectAgent(engine, i)).Position.X += 0.01f;
                }

                long a = Stopwatch.GetTimestamp();
                sync.Update(1f / 60f);
                long b = Stopwatch.GetTimestamp();
                emit.Update(1f / 60f);
                long c = Stopwatch.GetTimestamp();
                hudProjection.Update(1f / 60f);
                long d = Stopwatch.GetTimestamp();
                builder.Build(scene);
                long e = Stopwatch.GetTimestamp();
                if (frame >= 16)
                {
                    syncMs[frame - 16] = (b - a) * 1000d / Stopwatch.Frequency;
                    emitMs[frame - 16] = (c - b) * 1000d / Stopwatch.Frequency;
                    projMs[frame - 16] = (d - c) * 1000d / Stopwatch.Frequency;
                    buildMs[frame - 16] = (e - d) * 1000d / Stopwatch.Frequency;
                }
            }

            Console.WriteLine($"hud-baseline sync={Median(syncMs):F3}ms emit={Median(emitMs):F3}ms proj={Median(projMs):F3}ms build={Median(buildMs):F3}ms worldHud={screenHud.TextCount + screenHud.BarCount}");
            Assert.That(screenHud.TextCount + screenHud.BarCount, Is.GreaterThan(0), "benchmark requires live HUD items");
        }

        private static Entity CollectAgent(GameEngine engine, int index)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<VisualTransform, CullState>();
            Entity found = default;
            engine.World.Query(in query, (Entity entity, ref VisualTransform _, ref CullState cull) =>
            {
                if (cull.IsVisible && count++ == index)
                {
                    found = entity;
                }
            });
            return found;
        }

        private static double Median(double[] values)
        {
            Array.Sort(values);
            return values[values.Length / 2];
        }

        private static T RequirePresentationSystem<T>(GameEngine engine)
            where T : class, Arch.System.ISystem<float>
        {
            var field = typeof(GameEngine).GetField(
                "_presentationSystems",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GameEngine presentation systems field is unavailable.");
            var systems = (List<Arch.System.ISystem<float>>)field.GetValue(engine)!;
            foreach (var system in systems)
            {
                if (system is T match)
                {
                    return match;
                }
            }

            throw new InvalidOperationException($"Presentation system {typeof(T).Name} is not registered.");
        }

        [Test]
        public void HudPipelineInlineAbBenchmark()
        {
            PresenterInlineHudFeature.SetOverride(false);
            string classic = RunHudPipelineEngine("classic");
            PresenterInlineHudFeature.SetOverride(true);
            string inline = RunHudPipelineEngine("inline");
            PresenterInlineHudFeature.SetOverride(null);
            Console.WriteLine(classic);
            Console.WriteLine(inline);
        }

        private string RunHudPipelineEngine(string label)
        {
            var backend = new TestInputBackend();
            var focusOverride = new CameraCullingFocusOverride();
            using GameEngine engine = CreateEngine(backend, focusOverride);
            WorldHudToScreenSystem hudProjection = CreateHudProjection(engine);
            var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)
                ?? throw new InvalidOperationException("PresentationWorldHudBuffer missing.");
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)
                ?? throw new InvalidOperationException("PresentationScreenHudBuffer missing.");
            var builder = new PresentationOverlaySceneBuilder(
                screenHud,
                engine.GetService(CoreServiceKeys.PresentationWorldHudStrings),
                engine.GetService(CoreServiceKeys.PresentationTextCatalog),
                engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection),
                engine.GetService(CoreServiceKeys.ScreenOverlayBuffer),
                engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer));
            var scene = new PresentationOverlayScene(screenHud.Capacity + ScreenOverlayBuffer.MaxItems);
            var sync = RequirePresentationSystem<Ludots.Core.Presentation.Systems.PresenterEntityTransformSyncSystem>(engine);
            var emit = RequirePresentationSystem<Ludots.Core.Presentation.Systems.PresenterEmitSystem>(engine);

            engine.LoadMap(new MapLoadRequest(new MapId("mass_navigation"),
                MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, null) })));

            TickWithOverlay(engine, hudProjection, builder, scene, 90);

            int presenterCount = 0;
            var countQuery = new QueryDescription().WithAll<Ludots.Core.Presentation.Presenters.PresenterState>();
            engine.World.Query(in countQuery, (ref Ludots.Core.Presentation.Presenters.PresenterState _) => presenterCount++);

            const int Samples = 60;
            var syncMs = new double[Samples];
            var emitMs = new double[Samples];
            var projMs = new double[Samples];
            var buildMs = new double[Samples];
            for (int frame = 0; frame < 16 + Samples; frame++)
            {
                for (int i = 0; i < 100; i++)
                {
                    engine.World.Get<VisualTransform>(CollectAgent(engine, i)).Position.X += 0.01f;
                }

                long a = Stopwatch.GetTimestamp();
                sync.Update(1f / 60f);
                long b = Stopwatch.GetTimestamp();
                emit.Update(1f / 60f);
                long c = Stopwatch.GetTimestamp();
                hudProjection.Update(1f / 60f);
                long d = Stopwatch.GetTimestamp();
                builder.Build(scene);
                long e = Stopwatch.GetTimestamp();
                if (frame >= 16)
                {
                    syncMs[frame - 16] = (b - a) * 1000d / Stopwatch.Frequency;
                    emitMs[frame - 16] = (c - b) * 1000d / Stopwatch.Frequency;
                    projMs[frame - 16] = (d - c) * 1000d / Stopwatch.Frequency;
                    buildMs[frame - 16] = (e - d) * 1000d / Stopwatch.Frequency;
                }
            }

            Assert.That(screenHud.TextCount + screenHud.BarCount, Is.GreaterThan(0), $"{label}: HUD items must reach screenHud");
            return $"hud-{label} presenters={presenterCount} worldHud={worldHud.Count} screen={screenHud.TextCount + screenHud.BarCount} " +
                $"sync={Median(syncMs):F3} emit={Median(emitMs):F3} proj={Median(projMs):F3} build={Median(buildMs):F3} " +
                $"inlineComposed={emit.InlineHudComposedLastUpdate} inlineSkipped={emit.InlineHudSkippedLastUpdate}";
        }

        [Test]
        public void HealthTextAnchorsRetrackOwnersAfterCullCycle()
        {
            var backend = new TestInputBackend();
            var focusOverride = new CameraCullingFocusOverride();
            using GameEngine engine = CreateEngine(backend, focusOverride);
            WorldHudToScreenSystem hudProjection = CreateHudProjection(engine);
            engine.LoadMap(new MapLoadRequest(new MapId("mass_navigation"),
                MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, null) })));

            Tick(engine, hudProjection, 60);

            var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)
                ?? throw new InvalidOperationException("PresentationWorldHudBuffer missing.");
            Assert.That(worldHud.Count, Is.GreaterThan(0), "agents near the default camera must emit world HUD texts");

            // 采样：每个属主一条文本，记录锚点与属主实时位置。
            Dictionary<Entity, Vector3> anchorsByOwner = new();
            foreach (ref readonly WorldHudItem item in worldHud.GetSpan())
            {
                if (item.Kind != WorldHudItemKind.Text || anchorsByOwner.Count >= 12)
                {
                    continue;
                }

                if (!anchorsByOwner.TryAdd(item.Owner, item.WorldPosition))
                {
                    continue;
                }
            }

            Assert.That(anchorsByOwner.Count, Is.GreaterThan(0), "sampling must capture at least one health text owner");
            int baselineCount = worldHud.Count;

            // 剔除阶段：把剔除焦点指到远离人群的角落、窄视场，全员 ForceCull；
            // 仿真继续，属主继续行走。
            focusOverride.Enabled = true;
            focusOverride.TargetCm = new Vector2(90000f, 90000f);
            focusOverride.DistanceCm = 800f;
            focusOverride.Yaw = 0f;
            focusOverride.Pitch = 45f;
            focusOverride.FovYDeg = 4f;
            Tick(engine, hudProjection, 150);
            int culledCount = worldHud.Count;
            Console.WriteLine($"worldHud count: baseline={baselineCount} culled={culledCount}");

            // 回屏阶段：恢复自然相机，属主重新可见；文本条目必须重新锚定到属主当前位置。
            focusOverride.Enabled = false;
            Tick(engine, hudProjection, 45);

            Dictionary<Entity, Vector3> retracked = new();
            foreach (ref readonly WorldHudItem item in worldHud.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Text)
                {
                    retracked[item.Owner] = item.WorldPosition;
                }
            }

            Console.WriteLine($"worldHud count after restore: {worldHud.Count} (owners sampled: {anchorsByOwner.Count}, retracked owners: {retracked.Count})");

            var query = new QueryDescription().WithAll<VisualTransform>();
            int verified = 0;
            int stale = 0;
            engine.World.Query(in query, (Entity owner, ref VisualTransform visual) =>
            {
                if (!anchorsByOwner.TryGetValue(owner, out Vector3 frozenAnchor) ||
                    !retracked.TryGetValue(owner, out Vector3 currentAnchor))
                {
                    return;
                }

                float ownerDrift = Vector3.Distance(visual.Position, frozenAnchor);
                if (ownerDrift < 1.5f)
                {
                    return; // 没走远的属主无法区分冻结与跟踪
                }

                verified++;
                // 锚点合同：文本必须挂在属主当前位置的附件偏移处（XZ 跟随 + 1.4~1.8m 挂高）。
                Vector2 ownerXz = new(visual.Position.X, visual.Position.Z);
                Vector2 anchorXz = new(currentAnchor.X, currentAnchor.Z);
                float anchorError = Vector2.Distance(ownerXz, anchorXz);
                if (anchorError > 0.5f)
                {
                    stale++;
                    Console.WriteLine(
                        $"STALE owner={owner.Id} ownerPos=({visual.Position.X:F2},{visual.Position.Y:F2},{visual.Position.Z:F2}) " +
                        $"anchor=({currentAnchor.X:F2},{currentAnchor.Y:F2},{currentAnchor.Z:F2}) error={anchorError:F2}m drift={ownerDrift:F2}m");
                }
            });

            Console.WriteLine($"verified={verified} stale={stale}");
            Assert.That(verified, Is.GreaterThan(0),
                "cull cycle must leave walkable sampled owners to verify anchor tracking");
            Assert.That(stale, Is.EqualTo(0),
                $"{stale}/{verified} health texts kept frozen anchors across the cull cycle (ghost remnant contract)");
        }

        [Test]
        public void OverlaySceneTracksScreenHudAcrossCameraDeparture()
        {
            var backend = new TestInputBackend();
            var focusOverride = new CameraCullingFocusOverride();
            using GameEngine engine = CreateEngine(backend, focusOverride);
            WorldHudToScreenSystem hudProjection = CreateHudProjection(engine);

            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)
                ?? throw new InvalidOperationException("PresentationScreenHudBuffer missing.");
            var worldHudStrings = engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
            var textCatalog = engine.GetService(CoreServiceKeys.PresentationTextCatalog);
            var localeSelection = engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection);
            var screenOverlayBuffer = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            var minimapScreenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer);
            var builder = new PresentationOverlaySceneBuilder(
                screenHud, worldHudStrings, textCatalog, localeSelection, screenOverlayBuffer, minimapScreenMarkers);
            var scene = new PresentationOverlayScene(
                screenHud.Capacity + ScreenOverlayBuffer.MaxItems + (minimapScreenMarkers?.Capacity ?? 0));

            engine.LoadMap(new MapLoadRequest(new MapId("mass_navigation"),
                MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, null) })));

            TickWithOverlay(engine, hudProjection, builder, scene, 60);
            int nearScreenTexts = screenHud.TextCount;
            int nearSceneTexts = CountSceneTexts(scene);
            Console.WriteLine($"near: screenTexts={nearScreenTexts} sceneTexts={nearSceneTexts}");

            // 镜头离开：剔除焦点指向远处空角落、窄视场，全员出视野。
            focusOverride.Enabled = true;
            focusOverride.TargetCm = new Vector2(90000f, 90000f);
            focusOverride.DistanceCm = 800f;
            focusOverride.Yaw = 0f;
            focusOverride.Pitch = 45f;
            focusOverride.FovYDeg = 4f;

            // 单帧诊断：离开第一帧投影层记账的 removed 数与 builder 消费后的 scene 孤儿数。
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(1f / 60f);
            HeadlessPresentationTestHost.UpdateCamera(engine);
            hudProjection.Update(1f / 60f);
            int removedRecorded = screenHud.GetRemovedStableIdSpan().Length;
            int dirtyTextsRecorded = screenHud.GetDirtyTextSpan().Length;
            builder.Build(scene);
            Console.WriteLine($"first away frame: removedRecorded={removedRecorded} dirtyTexts={dirtyTextsRecorded} " +
                $"screenTexts={screenHud.TextCount} sceneTexts={CountSceneTexts(scene)}");

            TickWithOverlay(engine, hudProjection, builder, scene, 44);

            int awayScreenTexts = screenHud.TextCount;
            int awaySceneTexts = CountSceneTexts(scene);
            Console.WriteLine($"away: screenTexts={awayScreenTexts} sceneTexts={awaySceneTexts}");
            Assert.That(awaySceneTexts, Is.EqualTo(awayScreenTexts),
                "overlay scene must be a faithful feed of screenHud after the camera departs; orphans = 残留");

            // 回屏后再对账一次（移除→重加的完整周期不得留孤儿）。
            focusOverride.Enabled = false;
            TickWithOverlay(engine, hudProjection, builder, scene, 45);
            Console.WriteLine($"back: screenTexts={screenHud.TextCount} sceneTexts={CountSceneTexts(scene)}");
            Assert.That(CountSceneTexts(scene), Is.EqualTo(screenHud.TextCount),
                "overlay scene must re-sync with screenHud after the camera returns");

            // 部分离场（边缘刮过）：目标压在人群边缘、窄视场，逐帧扫 yaw 模拟镜头边缘刮过——
            // 出画条目必须从 scene 同步消失（黏滞残留的精确复现场景）。
            focusOverride.Enabled = true;
            focusOverride.TargetCm = new Vector2(-3000f, 1500f);
            focusOverride.DistanceCm = 5000f;
            focusOverride.Pitch = 45f;
            focusOverride.FovYDeg = 18f;
            var allRemovedEver = new HashSet<int>();
            for (int step = 0; step <= 60; step++)
            {
                focusOverride.Yaw = 20f + step * 0.8f;
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(1f / 60f);
                HeadlessPresentationTestHost.UpdateCamera(engine);
                hudProjection.Update(1f / 60f);
                ReadOnlySpan<int> removedThisFrame = screenHud.GetRemovedStableIdSpan();
                foreach (int removedId in removedThisFrame)
                {
                    allRemovedEver.Add(removedId);
                }

                builder.Build(scene);
                if (CountSceneTexts(scene) != screenHud.TextCount)
                {
                    var screenIds = new HashSet<int>();
                    foreach (ref readonly var t in screenHud.GetTextSpan())
                    {
                        if (t.StableId > 0)
                        {
                            screenIds.Add(t.StableId);
                        }
                    }

                    var sceneOnly = new List<int>();
                    foreach (ref readonly PresentationOverlayItem item2 in scene.GetSpan())
                    {
                        if (item2.Kind == PresentationOverlayItemKind.Text &&
                            item2.Layer == PresentationOverlayLayer.UnderUi &&
                            item2.StableId > 0 &&
                            !screenIds.Contains(item2.StableId))
                        {
                            sceneOnly.Add(item2.StableId);
                        }
                    }

                    foreach (int orphanId in sceneOnly)
                    {
                        Console.WriteLine($"ORPHAN id={orphanId} step={step} wasEverRemoved={allRemovedEver.Contains(orphanId)}");
                    }
                }
            }

            // 零分配合同：刮边稳态（removed+dirty 同时非空、位置持续变化）下 builder.Build 不得分配。
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int warmup = 0; warmup < 16; warmup++)
            {
                builder.Build(scene);
                hudProjection.Update(1f / 60f);
                builder.Build(scene);
            }

            long allocAfter = GC.GetAllocatedBytesForCurrentThread();
            Console.WriteLine($"builder steady-state allocation: {allocAfter - allocBefore} bytes over 32 builds");
            Assert.That(allocAfter - allocBefore, Is.EqualTo(0),
                "steady-state builder.Build must not allocate (removed-guard reuses its set)");

            TickWithOverlay(engine, hudProjection, builder, scene, 30);
            int partialScreenTexts = screenHud.TextCount;
            int partialSceneTexts = CountSceneTexts(scene);
            int partialOrphans = scene.CountUnderUiTextOrphansWithoutIndex();
            Console.WriteLine($"partial: screenTexts={partialScreenTexts} sceneTexts={partialSceneTexts} orphansWithoutIndex={partialOrphans}");
            Assert.That(partialSceneTexts, Is.EqualTo(partialScreenTexts),
                "partial departure must leave scene exactly tracking screenHud (edge-graze remnant contract)");
            Assert.That(partialOrphans, Is.EqualTo(0),
                "every stable-id text in the UnderUi lane must be present in the stable index");
        }

        [Test]
        public void SkiaCanvasClearsWorldTextsAfterCameraDeparture()
        {
            var backend = new TestInputBackend();
            var focusOverride = new CameraCullingFocusOverride();
            using GameEngine engine = CreateEngine(backend, focusOverride);
            WorldHudToScreenSystem hudProjection = CreateHudProjection(engine);

            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)
                ?? throw new InvalidOperationException("PresentationScreenHudBuffer missing.");
            var worldHudStrings = engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
            var textCatalog = engine.GetService(CoreServiceKeys.PresentationTextCatalog);
            var localeSelection = engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection);
            var screenOverlayBuffer = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            var minimapScreenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer);
            var builder = new PresentationOverlaySceneBuilder(
                screenHud, worldHudStrings, textCatalog, localeSelection, screenOverlayBuffer, minimapScreenMarkers);
            var scene = new PresentationOverlayScene(
                screenHud.Capacity + ScreenOverlayBuffer.MaxItems + (minimapScreenMarkers?.Capacity ?? 0));

            engine.LoadMap(new MapLoadRequest(new MapId("mass_navigation"),
                MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, null) })));

            using var renderer = new Ludots.Presentation.Skia.SkiaOverlayRenderer();
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(1280, 720));
            SkiaSharp.SKCanvas canvas = surface.Canvas;
            var pacer = new PresentationOverlayLanePacer(PresentationOverlayLayer.UnderUi);
            int underlayLayerVersion = -1;

            void RenderUnderlay()
            {
                bool hasUnderlay = scene.ContainsLayer(PresentationOverlayLayer.UnderUi);
                int layerVersion = scene.GetLayerVersion(PresentationOverlayLayer.UnderUi);
                bool refreshUnderlay = (hasUnderlay || underlayLayerVersion >= 0) &&
                    (layerVersion != underlayLayerVersion);
                if (refreshUnderlay)
                {
                    PresentationOverlayLanePacer.LaneRefreshPlan plan = hasUnderlay
                        ? pacer.BuildPlan(scene)
                        : default;
                    if (!hasUnderlay || plan.HasAnyRefresh)
                    {
                        canvas.Clear(SkiaSharp.SKColors.Transparent);
                        if (hasUnderlay)
                        {
                            renderer.Render(scene, canvas, PresentationOverlayLayer.UnderUi, plan);
                        }
                    }

                    if (hasUnderlay)
                    {
                        pacer.MarkPresented(scene, plan);
                    }
                    else
                    {
                        pacer.Reset();
                    }

                    underlayLayerVersion = layerVersion;
                }
            }

            void TickFrames(int frames)
            {
                for (int i = 0; i < frames; i++)
                {
                    engine.SetService(CoreServiceKeys.UiCaptured, false);
                    engine.Tick(1f / 60f);
                    HeadlessPresentationTestHost.UpdateCamera(engine);
                    hudProjection.Update(1f / 60f);
                    builder.Build(scene);
                    RenderUnderlay();
                }
            }

            TickFrames(60);
            long nearInk = CountInk(surface);
            Console.WriteLine($"near: ink={nearInk} screenTexts={screenHud.TextCount}");
            Assert.That(nearInk, Is.GreaterThan(0), "near camera must paint world texts onto the canvas");

            focusOverride.Enabled = true;
            focusOverride.DistanceCm = 800f;
            focusOverride.Yaw = 0f;
            focusOverride.Pitch = 45f;
            focusOverride.FovYDeg = 4f;
            // 连续拖动：每帧把目标挪远一段，模拟玩家拖镜头（条目逐批离屏的混合变更序列）。
            for (int step = 1; step <= 30; step++)
            {
                focusOverride.TargetCm = new Vector2(step * 3000f, step * 3000f);
                TickFrames(1);
                long stepInk = CountInk(surface);
                if (stepInk > 0 && screenHud.TextCount == 0)
                {
                    Console.WriteLine($"LEAK at step {step}: ink={stepInk} screenTexts=0");
                }
            }

            TickFrames(30);
            long awayInk = CountInk(surface);
            Console.WriteLine($"away: ink={awayInk} screenTexts={screenHud.TextCount} sceneTexts={CountSceneTexts(scene)}");
            Assert.That(awayInk, Is.EqualTo(0),
                "canvas must be fully cleared after every owner left the view; leftover ink = HUD 残留");
        }

        private static long CountInk(SkiaSharp.SKSurface surface)
        {
            using SkiaSharp.SKImage image = surface.Snapshot();
            using SkiaSharp.SKPixmap pixmap = new();
            if (!image.PeekPixels(pixmap))
            {
                throw new InvalidOperationException("failed to peek surface pixels.");
            }

            nint addr = pixmap.GetPixels();
            int width = pixmap.Width;
            int height = pixmap.Height;
            long ink = 0;
            unsafe
            {
                byte* row = (byte*)addr;
                for (int y = 0; y < height; y++)
                {
                    byte* pixel = row + (long)y * pixmap.RowBytes;
                    for (int x = 0; x < width; x += 7)
                    {
                        if (pixel[x * 4 + 3] != 0)
                        {
                            ink++;
                        }
                    }
                }
            }

            return ink;
        }

        private static int CountSceneTexts(PresentationOverlayScene scene)
        {
            int count = 0;
            foreach (ref readonly PresentationOverlayItem item in scene.GetSpan())
            {
                if (item.Kind == PresentationOverlayItemKind.Text &&
                    item.Layer == PresentationOverlayLayer.UnderUi)
                {
                    count++;
                }
            }

            return count;
        }

        private static void TickWithOverlay(
            GameEngine engine,
            WorldHudToScreenSystem hudProjection,
            PresentationOverlaySceneBuilder builder,
            PresentationOverlayScene scene,
            int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(1f / 60f);
                HeadlessPresentationTestHost.UpdateCamera(engine);
                hudProjection.Update(1f / 60f);
                builder.Build(scene);
            }
        }

        private static GameEngine CreateEngine(TestInputBackend backend, CameraCullingFocusOverride focusOverride)
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(RepoModPaths.ResolveExplicit(repoRoot, Mods), Path.Combine(repoRoot, "assets"));
            var inputConfig = new Ludots.Core.Input.Config.InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var handler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                handler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, handler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.SetService(CoreServiceKeys.ViewController, (IViewController)new HeadlessViewController(1280f, 720f));
            HeadlessPresentationTestHost.Install(engine, focusOverride);
            engine.Start();
            return engine;
        }

        private static WorldHudToScreenSystem CreateHudProjection(GameEngine engine)
        {
            var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)
                ?? throw new InvalidOperationException("PresentationWorldHudBuffer missing.");
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)
                ?? throw new InvalidOperationException("PresentationScreenHudBuffer missing.");
            var strings = engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
            var projector = engine.GetService(CoreServiceKeys.ScreenProjector)
                ?? throw new InvalidOperationException("ScreenProjector missing.");
            var view = engine.GetService(CoreServiceKeys.ViewController)
                ?? throw new InvalidOperationException("ViewController missing.");
            var timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            return new WorldHudToScreenSystem(engine.World, worldHud, strings, projector, view, screenHud, timings);
        }

        private static void Tick(GameEngine engine, WorldHudToScreenSystem hudProjection, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(1f / 60f);
                HeadlessPresentationTestHost.UpdateCamera(engine);
                hudProjection.Update(1f / 60f);
            }
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "mods")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("repo root not found");
        }

        private sealed class HeadlessViewController : IViewController
        {
            public HeadlessViewController(float width, float height) { Resolution = new Vector2(width, height); }
            public Vector2 Resolution { get; }
            public float Fov => 50f;
            public float AspectRatio => Resolution.X / Resolution.Y;
        }

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);
            public Vector2 MousePosition { get; set; }
            public void SetMousePosition(Vector2 v) => MousePosition = v;
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.Contains(devicePath);
            public Vector2 GetMousePosition() => MousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
            public void SetButton(string devicePath, bool down)
            {
                if (down) _buttons.Add(devicePath);
                else _buttons.Remove(devicePath);
            }
        }
    }
}
