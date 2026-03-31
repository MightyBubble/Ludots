using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    public sealed class RelationshipShowcasePlayableAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string InputBackendKey = "Tests.RelationshipShowcase.InputBackend";
        private const string HeadlessCameraKey = "Tests.RelationshipShowcase.HeadlessCamera";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "RelationshipShowcaseMod"
        };

        [Test]
        public void RelationshipShowcase_PlayableAcceptance_WritesArtifacts()
        {
            string repoRoot = FindRepoRoot();
            RelationshipShowcaseMod.Runtime.RelationshipShowcaseConfig showcaseConfig = LoadShowcaseConfig(repoRoot);
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "relationship-showcase");
            string screensDir = Path.Combine(artifactDir, "screens");
            Directory.CreateDirectory(artifactDir);
            Directory.CreateDirectory(screensDir);

            var timeline = new List<string>();
            var snapshots = new List<AcceptanceSnapshot>();
            var frameTimesMs = new List<double>();
            string selectedHero = showcaseConfig.Scenario.Heroes[0].Name;
            string enemyFocus = showcaseConfig.Presentation.EnemyFocusPendingText;

            using var engine = CreateEngine();
            var backend = GetInputBackend(engine);
            var screen = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) as ScreenOverlayBuffer
                ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");
            var ground = engine.GetService(CoreServiceKeys.GroundOverlayBuffer) as GroundOverlayBuffer
                ?? throw new InvalidOperationException("GroundOverlayBuffer missing.");
            var runtime = engine.GetService(CoreServiceKeys.RelationshipRuntime) as RelationshipRuntime
                ?? throw new InvalidOperationException("RelationshipRuntime missing.");
            var metrics = engine.GetService(CoreServiceKeys.RelationshipMetricRegistry) as RelationshipMetricRegistry
                ?? throw new InvalidOperationException("RelationshipMetricRegistry missing.");
            var types = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry) as RelationshipTypeRegistry
                ?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");

            LoadMap(engine, showcaseConfig.MapId, frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

            string liuBeiName = showcaseConfig.Scenario.Heroes[0].Name;
            string guanYuName = showcaseConfig.Scenario.Heroes[1].Name;
            string zhangFeiName = showcaseConfig.Scenario.Heroes[2].Name;
            string rebelCaptainName = showcaseConfig.Scenario.Enemies[0].Name;
            string rebelSpearmanName = showcaseConfig.Scenario.Enemies[1].Name;
            Entity liuBei = FindEntityByName(engine.World, liuBeiName);
            Entity guanYu = FindEntityByName(engine.World, guanYuName);
            Entity zhangFei = FindEntityByName(engine.World, zhangFeiName);
            Entity rebelCaptain = FindEntityByName(engine.World, rebelCaptainName);
            Entity rebelSpearman = FindEntityByName(engine.World, rebelSpearmanName);

            int loyaltyId = metrics.GetId(showcaseConfig.Metrics.Loyalty);
            int supportId = metrics.GetId(showcaseConfig.Metrics.Support);
            int threatId = metrics.GetId(showcaseConfig.Metrics.Threat);
            int socialBondTypeId = types.GetId(showcaseConfig.Types.SocialBond);
            int hostilityTypeId = types.GetId(showcaseConfig.Types.Hostility);
            int shieldId = AttributeRegistry.GetId("Shield");
            int moveSpeedId = AttributeRegistry.GetId("MoveSpeed");
            int healthId = AttributeRegistry.GetId("Health");
            int synergyTagId = TagRegistry.GetId(showcaseConfig.Tags.Synergy);
            int focusTagId = TagRegistry.GetId(showcaseConfig.Tags.FocusedByEnemy);

            IReadOnlyList<string> overlayText = ExtractScreenText(screen);
            Assert.That(overlayText.Any(text => text.Contains(showcaseConfig.Presentation.TitlePrefix, StringComparison.Ordinal)), Is.True);
            Assert.That(CountGroundOverlays(ground, GroundOverlayShape.Ring), Is.GreaterThanOrEqualTo(1));
            CaptureSnapshot(engine, screen, ground, runtime, metrics, showcaseConfig, socialBondTypeId, hostilityTypeId, snapshots, "map_loaded", selectedHero, enemyFocus);
            timeline.Add("[T+001] relationship_showcase booted with Peach Garden panel text and world highlight rings visible.");

            PressButton(engine, backend, "<Keyboard>/4", frameTimesMs);
            Assert.That(ScreenContains(screen, showcaseConfig.Logs.RallyDenied), Is.True);
            CaptureSnapshot(engine, screen, ground, runtime, metrics, showcaseConfig, socialBondTypeId, hostilityTypeId, snapshots, "guard_branch_rally_denied", selectedHero, enemyFocus);
            timeline.Add("[T+002] Rally guard branch rejected because trust, oath, or synergy thresholds were still locked.");

            float guanShieldBeforeDoctrine = ReadAttribute(engine.World, guanYu, shieldId);
            float zhangShieldBeforeDoctrine = ReadAttribute(engine.World, zhangFei, shieldId);
            PressButton(engine, backend, "<Keyboard>/1", frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () =>
                    runtime.GetMetric(liuBei, guanYu, socialBondTypeId, loyaltyId) >= 60 &&
                    runtime.GetMetric(liuBei, zhangFei, socialBondTypeId, loyaltyId) >= 60 &&
                    HasTag(engine.World, FindTeamEntity(engine, showcaseConfig.SynergyTeamId), synergyTagId),
                maxFrames: 30);

            short loyaltyToGuan = runtime.GetMetric(liuBei, guanYu, socialBondTypeId, loyaltyId);
            short loyaltyToZhang = runtime.GetMetric(liuBei, zhangFei, socialBondTypeId, loyaltyId);
            float guanShieldAfterDoctrine = ReadAttribute(engine.World, guanYu, shieldId);
            float zhangShieldAfterDoctrine = ReadAttribute(engine.World, zhangFei, shieldId);
            Assert.That(loyaltyToGuan, Is.GreaterThanOrEqualTo(60));
            Assert.That(loyaltyToZhang, Is.GreaterThanOrEqualTo(60));
            Assert.That(ScreenContains(screen, $"{showcaseConfig.Presentation.TrustedLabel}: {showcaseConfig.Presentation.ReadyText}"), Is.True);
            Assert.That(ScreenContains(screen, $"{showcaseConfig.Presentation.SynergyLabel}: {showcaseConfig.Presentation.ReadyText}"), Is.True);
            Assert.That(guanShieldAfterDoctrine, Is.GreaterThan(guanShieldBeforeDoctrine));
            Assert.That(zhangShieldAfterDoctrine, Is.GreaterThan(zhangShieldBeforeDoctrine));
            Assert.That(HasTag(engine.World, FindTeamEntity(engine, showcaseConfig.SynergyTeamId), synergyTagId), Is.True);
            CaptureSnapshot(engine, screen, ground, runtime, metrics, showcaseConfig, socialBondTypeId, hostilityTypeId, snapshots, "doctrine_trust_synergy", selectedHero, enemyFocus);
            timeline.Add($"[T+003] Liu Bei.Benevolence Doctrine -> Loyalty(Liu->Guan={loyaltyToGuan}, Liu->Zhang={loyaltyToZhang}) | Trusted callbacks fired | Shu synergy online.");

            float guanSpeedBeforeDrill = ReadAttribute(engine.World, guanYu, moveSpeedId);
            float zhangSpeedBeforeDrill = ReadAttribute(engine.World, zhangFei, moveSpeedId);
            PressButton(engine, backend, "<Keyboard>/2", frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () =>
                    runtime.GetMetric(guanYu, zhangFei, socialBondTypeId, supportId) >= 55 &&
                    runtime.GetMetric(zhangFei, guanYu, socialBondTypeId, supportId) >= 55,
                maxFrames: 30);

            short guanToZhangSupport = runtime.GetMetric(guanYu, zhangFei, socialBondTypeId, supportId);
            short zhangToGuanSupport = runtime.GetMetric(zhangFei, guanYu, socialBondTypeId, supportId);
            float guanSpeedAfterDrill = ReadAttribute(engine.World, guanYu, moveSpeedId);
            float zhangSpeedAfterDrill = ReadAttribute(engine.World, zhangFei, moveSpeedId);
            Assert.That(guanToZhangSupport, Is.GreaterThanOrEqualTo(55));
            Assert.That(zhangToGuanSupport, Is.GreaterThanOrEqualTo(55));
            Assert.That(ScreenContains(screen, $"{showcaseConfig.Presentation.OathBondLabel}: {showcaseConfig.Presentation.ReadyText}"), Is.True);
            Assert.That(guanSpeedAfterDrill, Is.GreaterThan(guanSpeedBeforeDrill));
            Assert.That(zhangSpeedAfterDrill, Is.GreaterThan(zhangSpeedBeforeDrill));
            CaptureSnapshot(engine, screen, ground, runtime, metrics, showcaseConfig, socialBondTypeId, hostilityTypeId, snapshots, "oath_rank_up", selectedHero, enemyFocus);
            timeline.Add($"[T+004] Guan Yu + Zhang Fei.Oath Drill -> Support rank crossed to {guanToZhangSupport}/{zhangToGuanSupport} and movement buffs landed through GAS.");

            PressButton(engine, backend, "<Keyboard>/tab", frameTimesMs);
            selectedHero = guanYuName;
            Assert.That(ScreenContains(screen, $"{showcaseConfig.Presentation.SelectedHeroLabel}: {guanYuName}"), Is.True);
            CaptureSnapshot(engine, screen, ground, runtime, metrics, showcaseConfig, socialBondTypeId, hostilityTypeId, snapshots, "selection_rotated", selectedHero, enemyFocus);
            timeline.Add("[T+005] Player rotated focus with Tab to Guan Yu, proving the showcase is playable through authoritative input.");

            float guanHealthBeforeTaunt = ReadAttribute(engine.World, guanYu, healthId);
            PressButton(engine, backend, "<Keyboard>/3", frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () =>
                    ReadAttribute(engine.World, guanYu, healthId) < guanHealthBeforeTaunt &&
                    ScreenContains(screen, $"{showcaseConfig.Presentation.EnemyFocusLabel}: {guanYuName}"),
                maxFrames: 80);

            short captainThreat = runtime.GetMetric(rebelCaptain, guanYu, hostilityTypeId, threatId);
            short spearmanThreat = runtime.GetMetric(rebelSpearman, guanYu, hostilityTypeId, threatId);
            float guanHealthAfterTaunt = ReadAttribute(engine.World, guanYu, healthId);
            enemyFocus = guanYuName;
            Assert.That(captainThreat, Is.GreaterThanOrEqualTo(70));
            Assert.That(spearmanThreat, Is.GreaterThanOrEqualTo(70));
            Assert.That(guanHealthAfterTaunt, Is.LessThan(guanHealthBeforeTaunt));
            Assert.That(HasTag(engine.World, guanYu, focusTagId), Is.True);
            CaptureSnapshot(engine, screen, ground, runtime, metrics, showcaseConfig, socialBondTypeId, hostilityTypeId, snapshots, "threat_focus_and_enemy_strike", selectedHero, enemyFocus);
            timeline.Add($"[T+006] Guan Yu.Taunt -> Threat(Captain={captainThreat}, Spearman={spearmanThreat}) | enemy focus locked on Guan Yu | HP {guanHealthBeforeTaunt:0} -> {guanHealthAfterTaunt:0}.");

            float liuShieldBeforeRally = ReadAttribute(engine.World, liuBei, shieldId);
            float zhangShieldBeforeRally = ReadAttribute(engine.World, zhangFei, shieldId);
            PressButton(engine, backend, "<Keyboard>/4", frameTimesMs);
            Tick(engine, 6, frameTimesMs);
            float liuShieldAfterRally = ReadAttribute(engine.World, liuBei, shieldId);
            float zhangShieldAfterRally = ReadAttribute(engine.World, zhangFei, shieldId);
            Assert.That(liuShieldAfterRally, Is.GreaterThan(liuShieldBeforeRally));
            Assert.That(zhangShieldAfterRally, Is.GreaterThan(zhangShieldBeforeRally));
            CaptureSnapshot(engine, screen, ground, runtime, metrics, showcaseConfig, socialBondTypeId, hostilityTypeId, snapshots, "rally_banner", selectedHero, enemyFocus);
            timeline.Add($"[T+007] Guan Yu.Rally Banner converted relationship state into shared GAS buffs | Liu Shield {liuShieldBeforeRally:0}->{liuShieldAfterRally:0} | Zhang Shield {zhangShieldBeforeRally:0}->{zhangShieldAfterRally:0}.");

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(snapshots));
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, snapshots, frameTimesMs));
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());
            WriteAcceptanceScreenshots(snapshots, screensDir);
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);

            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, new SkiaImageSizeProvider());

            var view = new StubViewController(1920f, 1080f);
            engine.SetService(CoreServiceKeys.ViewController, view);
            var cameraAdapter = new StubCameraAdapter();
            var timingDiagnostics = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter, timingDiagnostics);
            var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, view);
            var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, view);
            screenProjector.BindPresenter(cameraPresenter);
            screenRayProvider.BindPresenter(cameraPresenter);
            engine.SetService(CoreServiceKeys.ScreenProjector, screenProjector);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, screenRayProvider);

            var culling = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, view, timingDiagnostics);
            engine.RegisterPresentationSystem(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
            engine.GlobalContext[HeadlessCameraKey] = new HeadlessCameraRuntime(
                cameraPresenter,
                engine.GetService(CoreServiceKeys.PresentationFrameSetup));

            engine.Start();
            return engine;
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new TestInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.GlobalContext[InputBackendKey] = backend;
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(InputBackendKey, out object? backendObj) &&
                   backendObj is TestInputBackend backend
                ? backend
                : throw new InvalidOperationException("Relationship showcase test input backend missing.");
        }

        private static Entity FindTeamEntity(GameEngine engine, int teamId)
        {
            return engine.GetService(CoreServiceKeys.TeamEntityLookup) is Ludots.Core.Gameplay.Teams.TeamEntityLookup lookup
                ? lookup.Get(teamId)
                : Entity.Null;
        }

        private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs, int frames = 12)
        {
            engine.LoadMap(mapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null, $"{mapId} should create a live map session.");
            Tick(engine, frames, frameTimesMs);
        }

        private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
        {
            for (int i = 0; i < frames; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
                UpdateHeadlessCamera(engine);
                frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
            }
        }

        private static void TickUntil(GameEngine engine, List<double> frameTimesMs, Func<bool> predicate, int maxFrames)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                Tick(engine, 1, frameTimesMs);
            }

            Assert.That(predicate(), Is.True, $"Condition was not satisfied within {maxFrames} frames.");
        }

        private static void PressButton(GameEngine engine, TestInputBackend backend, string devicePath, List<double> frameTimesMs)
        {
            backend.SetButton(devicePath, true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton(devicePath, false);
            Tick(engine, 2, frameTimesMs);
        }

        private static void UpdateHeadlessCamera(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(HeadlessCameraKey, out object? runtimeObj) ||
                runtimeObj is not HeadlessCameraRuntime runtime)
            {
                return;
            }

            float alpha = runtime.PresentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
            runtime.CameraPresenter.Update(engine.GameSession.Camera, alpha);
        }

        private static void CaptureSnapshot(
            GameEngine engine,
            ScreenOverlayBuffer screen,
            GroundOverlayBuffer ground,
            RelationshipRuntime runtime,
            RelationshipMetricRegistry metrics,
            RelationshipShowcaseMod.Runtime.RelationshipShowcaseConfig showcaseConfig,
            int socialBondTypeId,
            int hostilityTypeId,
            List<AcceptanceSnapshot> snapshots,
            string step,
            string selectedHero,
            string enemyFocus)
        {
            Entity liuBei = FindEntityByName(engine.World, showcaseConfig.Scenario.Heroes[0].Name);
            Entity guanYu = FindEntityByName(engine.World, showcaseConfig.Scenario.Heroes[1].Name);
            Entity zhangFei = FindEntityByName(engine.World, showcaseConfig.Scenario.Heroes[2].Name);
            Entity rebelCaptain = FindEntityByName(engine.World, showcaseConfig.Scenario.Enemies[0].Name);

            int loyaltyId = metrics.GetId(showcaseConfig.Metrics.Loyalty);
            int supportId = metrics.GetId(showcaseConfig.Metrics.Support);
            int threatId = metrics.GetId(showcaseConfig.Metrics.Threat);
            int shieldId = AttributeRegistry.GetId("Shield");
            int healthId = AttributeRegistry.GetId("Health");
            Entity selectedEntity = FindEntityByName(engine.World, selectedHero);
            if (selectedEntity == Entity.Null)
            {
                selectedEntity = liuBei;
            }

            var overlayCounts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["screen"] = screen.Count,
                ["ground"] = ground.Count,
                ["rings"] = CountGroundOverlays(ground, GroundOverlayShape.Ring)
            };

            snapshots.Add(new AcceptanceSnapshot(
                Step: step,
                SelectedHero: selectedHero,
                EnemyFocus: enemyFocus,
                TrustedUnlocked: ScreenContains(screen, $"{showcaseConfig.Presentation.TrustedLabel}: {showcaseConfig.Presentation.ReadyText}"),
                OathBondUnlocked: ScreenContains(screen, $"{showcaseConfig.Presentation.OathBondLabel}: {showcaseConfig.Presentation.ReadyText}"),
                SynergyActive: ScreenContains(screen, $"{showcaseConfig.Presentation.SynergyLabel}: {showcaseConfig.Presentation.ReadyText}"),
                LoyaltyLiuToGuan: runtime.GetMetric(liuBei, guanYu, socialBondTypeId, loyaltyId),
                LoyaltyLiuToZhang: runtime.GetMetric(liuBei, zhangFei, socialBondTypeId, loyaltyId),
                SupportGuanToZhang: runtime.GetMetric(guanYu, zhangFei, socialBondTypeId, supportId),
                ThreatCaptainToSelected: runtime.GetMetric(rebelCaptain, selectedEntity, hostilityTypeId, threatId),
                OverlayCounts: overlayCounts,
                ScreenText: ExtractScreenText(screen).TakeLast(8).ToArray(),
                RecentLog: ExtractScreenText(screen).Where(line => line.StartsWith("[T+", StringComparison.Ordinal)).TakeLast(5).ToArray(),
                Heroes: new[]
                {
                    BuildHeroSnapshot(engine.World, liuBei, shieldId, healthId),
                    BuildHeroSnapshot(engine.World, guanYu, shieldId, healthId),
                    BuildHeroSnapshot(engine.World, zhangFei, shieldId, healthId),
            }));
        }

        private static RelationshipShowcaseMod.Runtime.RelationshipShowcaseConfig LoadShowcaseConfig(string repoRoot)
        {
            string path = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "relationship",
                "RelationshipShowcaseMod",
                "assets",
                "RelationshipShowcaseConfig.json");
            using var stream = File.OpenRead(path);
            return RelationshipShowcaseMod.Runtime.RelationshipShowcaseConfig.Load(stream);
        }

        private static HeroSnapshot BuildHeroSnapshot(World world, Entity entity, int shieldId, int healthId)
        {
            if (entity == Entity.Null || !world.IsAlive(entity) || !world.TryGet(entity, out Name name))
            {
                return new HeroSnapshot("missing", 0f, 0f);
            }

            return new HeroSnapshot(
                name.Value,
                ReadAttribute(world, entity, healthId),
                ReadAttribute(world, entity, shieldId));
        }

        private static IReadOnlyList<string> ExtractScreenText(ScreenOverlayBuffer overlay)
        {
            var lines = new List<string>(overlay.Count);
            ReadOnlySpan<ScreenOverlayItem> items = overlay.GetSpan();
            for (int i = 0; i < items.Length; i++)
            {
                ref readonly ScreenOverlayItem item = ref items[i];
                if (item.Kind != ScreenOverlayItemKind.Text)
                {
                    continue;
                }

                string? text = overlay.GetString(item.StringId);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lines.Add(text);
                }
            }

            return lines;
        }

        private static bool ScreenContains(ScreenOverlayBuffer overlay, string fragment)
        {
            return ExtractScreenText(overlay).Any(line => line.Contains(fragment, StringComparison.Ordinal));
        }

        private static string ReadPanelValue(ScreenOverlayBuffer overlay, string prefix)
        {
            IReadOnlyList<string> lines = ExtractScreenText(overlay);
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                string line = lines[i];
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return line[prefix.Length..].Trim();
                }
            }

            return string.Empty;
        }

        private static int CountGroundOverlays(GroundOverlayBuffer ground, GroundOverlayShape shape)
        {
            int count = 0;
            ReadOnlySpan<GroundOverlayItem> items = ground.GetSpan();
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Shape == shape)
                {
                    count++;
                }
            }

            return count;
        }

        private static float ReadAttribute(World world, Entity entity, int attributeId)
        {
            if (entity == Entity.Null || !world.IsAlive(entity) || attributeId < 0 || !world.Has<AttributeBuffer>(entity))
            {
                return 0f;
            }

            return world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);
        }

        private static bool HasTag(World world, Entity entity, int tagId)
        {
            return tagId > 0 &&
                   entity != Entity.Null &&
                   world.IsAlive(entity) &&
                   world.Has<GameplayTagContainer>(entity) &&
                   world.Get<GameplayTagContainer>(entity).HasTag(tagId);
        }

        private static Entity FindEntityByName(World world, string name)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (string.Equals(entityName.Value, name, StringComparison.Ordinal))
                {
                    result = entity;
                }
            });

            if (result == Entity.Null)
            {
                throw new InvalidOperationException($"Entity '{name}' was not found.");
            }

            return result;
        }

        private static string BuildTraceJsonl(IReadOnlyList<AcceptanceSnapshot> snapshots)
        {
            var lines = new List<string>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                AcceptanceSnapshot snapshot = snapshots[i];
                lines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"relationship-showcase-{i + 1:000}",
                    step = snapshot.Step,
                    selected_hero = snapshot.SelectedHero,
                    enemy_focus = snapshot.EnemyFocus,
                    trusted_unlocked = snapshot.TrustedUnlocked,
                    oath_bond_unlocked = snapshot.OathBondUnlocked,
                    synergy_active = snapshot.SynergyActive,
                    loyalty_liu_to_guan = snapshot.LoyaltyLiuToGuan,
                    loyalty_liu_to_zhang = snapshot.LoyaltyLiuToZhang,
                    support_guan_to_zhang = snapshot.SupportGuanToZhang,
                    threat_captain_to_selected = snapshot.ThreatCaptainToSelected,
                    overlay_counts = snapshot.OverlayCounts,
                    screen_text = snapshot.ScreenText,
                    recent_log = snapshot.RecentLog,
                    heroes = snapshot.Heroes,
                    status = "done"
                }));
            }

            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        private static string BuildBattleReport(
            IReadOnlyList<string> timeline,
            IReadOnlyList<AcceptanceSnapshot> snapshots,
            IReadOnlyList<double> frameTimesMs)
        {
            AcceptanceSnapshot final = snapshots[^1];
            double medianTick = Median(frameTimesMs);
            double maxTick = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();

            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: relationship-showcase");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: prove one reusable relationship runtime can drive CRPG trust, JRPG support rank, auto-battler synergy tiers, and Three Kingdoms oath fantasy inside a playable Ludots mod.");
            sb.AppendLine("- Gameplay domain: ECS relationship edges, team meta-entity synergy, GAS effects, Trigger callbacks, input-driven showcase presentation, and deterministic battle telemetry.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: none");
            sb.AppendLine("- Map: `relationship_showcase`");
            sb.AppendLine($"- Mods: `{string.Join("`, `", AcceptanceMods)}`");
            sb.AppendLine("- Clock profile: fixed `1/60s`, headless `GameEngine.Tick()` loop.");
            sb.AppendLine("- Input source: production `InputConfigPipelineLoader` + `PlayerInputHandler` backed by a deterministic keyboard backend.");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Load the Peach Garden showcase and confirm the relationship panel plus world rings render.");
            sb.AppendLine("2. Trigger the rally guard branch before trust, oath, and synergy are ready.");
            sb.AppendLine("3. Cast Benevolence Doctrine to unlock CRPG-style trust thresholds and auto-battler team synergy.");
            sb.AppendLine("4. Run Oath Drill to push JRPG-style support rank over the unlock threshold.");
            sb.AppendLine("5. Rotate selection, taunt into enemy focus, wait for threat-driven strikes, then rally to cash out the unlocked relationship state as GAS buffs.");
            sb.AppendLine();
            sb.AppendLine("## Expected Outcomes");
            sb.AppendLine("- Primary success condition: relationship callbacks, team synergy, Trigger events, and GAS effects all resolve on the production runtime path.");
            sb.AppendLine("- Failure branch condition: pressing rally before unlocks exist must deny cleanly without granting buffs.");
            sb.AppendLine("- Key metrics: loyalty, support, threat, synergy state, selected/focused hero, shield/health deltas, overlay visibility, and recent battle log lines.");
            sb.AppendLine();
            sb.AppendLine("## Evidence Artifacts");
            sb.AppendLine("- `artifacts/acceptance/relationship-showcase/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/relationship-showcase/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/relationship-showcase/path.mmd`");
            sb.AppendLine("- `artifacts/acceptance/relationship-showcase/screens/01_doctrine_trust_synergy.png`");
            sb.AppendLine("- `artifacts/acceptance/relationship-showcase/screens/02_rally_banner.png`");
            sb.AppendLine("- `artifacts/acceptance/relationship-showcase/screens/timeline.png`");
            sb.AppendLine("- `artifacts/techdebt/2026-03-23-raylib-relationship-showcase-launch.md`");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            for (int i = 0; i < timeline.Count; i++)
            {
                sb.AppendLine($"- {timeline[i]}");
            }

            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine("- verdict: the showcase stayed on the shared Ludots relationship, trigger, and GAS infrastructure from threshold unlocks to enemy focus and final rally conversion.");
            sb.AppendLine($"- reason: final state is selected `{final.SelectedHero}`, enemy focus `{final.EnemyFocus}`, trusted `{final.TrustedUnlocked}`, oath `{final.OathBondUnlocked}`, synergy `{final.SynergyActive}`, support `{final.SupportGuanToZhang}`, threat `{final.ThreatCaptainToSelected}`.");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- snapshots captured: `{snapshots.Count}`");
            sb.AppendLine($"- median headless tick: `{medianTick:F3}ms`");
            sb.AppendLine($"- max headless tick: `{maxTick:F3}ms`");
            sb.AppendLine($"- final loyalty: `Liu->Guan {final.LoyaltyLiuToGuan}`, `Liu->Zhang {final.LoyaltyLiuToZhang}`");
            sb.AppendLine($"- final support: `Guan->Zhang {final.SupportGuanToZhang}`");
            sb.AppendLine($"- final overlay counts: `screen={final.OverlayCounts["screen"]}`, `ground={final.OverlayCounts["ground"]}`, `rings={final.OverlayCounts["rings"]}`");
            sb.AppendLine("- reusable wiring: `RelationshipRuntime`, `RelationshipChangeBuffer`, `RelationshipCatalogPipelineLoader`, `RelationshipCatalogInstaller`, `RelationshipProcessingSystem`, `RelationshipCallbackProcessor`, `RelationshipSynergyProcessor`, `TriggerManager`, `EffectRequestQueue`, `TeamEntityLookup`");
            sb.AppendLine();
            sb.AppendLine("## Open Tech Debt");
            sb.AppendLine("- debt_id: `TD-2026-03-23-raylib-relationship-showcase-launch`");
            sb.AppendLine("- status: `open`");
            sb.AppendLine("- note: headless acceptance and PNG evidence are complete, but live raylib launch still hits a host-side `Arch` assembly load failure recorded in `artifacts/techdebt/2026-03-23-raylib-relationship-showcase-launch.md`.");
            return sb.ToString();
        }

        private static void WriteAcceptanceScreenshots(IReadOnlyList<AcceptanceSnapshot> snapshots, string screensDir)
        {
            AcceptanceSnapshot doctrine = GetSnapshotOrThrow(snapshots, "doctrine_trust_synergy");
            AcceptanceSnapshot rally = GetSnapshotOrThrow(snapshots, "rally_banner");

            WriteAcceptanceSnapshotPng(doctrine, Path.Combine(screensDir, "01_doctrine_trust_synergy.png"));
            WriteAcceptanceSnapshotPng(rally, Path.Combine(screensDir, "02_rally_banner.png"));
            WriteAcceptanceTimelinePng(snapshots, Path.Combine(screensDir, "timeline.png"));
        }

        private static AcceptanceSnapshot GetSnapshotOrThrow(IReadOnlyList<AcceptanceSnapshot> snapshots, string step)
        {
            AcceptanceSnapshot? match = snapshots.FirstOrDefault(snapshot => string.Equals(snapshot.Step, step, StringComparison.Ordinal));
            if (match == null)
            {
                throw new InvalidOperationException($"Acceptance snapshot '{step}' was not captured.");
            }

            return match;
        }

        private static void WriteAcceptanceSnapshotPng(AcceptanceSnapshot snapshot, string path)
        {
            using var bitmap = new Bitmap(1600, 900);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.FromArgb(10, 16, 24));

            using var panelBrush = new SolidBrush(Color.FromArgb(18, 30, 44));
            using var panelPen = new Pen(Color.FromArgb(70, 122, 168), 2f);
            using var titleBrush = new SolidBrush(Color.FromArgb(246, 212, 108));
            using var bodyBrush = new SolidBrush(Color.White);
            using var minorBrush = new SolidBrush(Color.FromArgb(188, 205, 222));
            using var accentBrush = new SolidBrush(Color.FromArgb(119, 215, 173));
            using var titleFont = new Font("Consolas", 22f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var bodyFont = new Font("Consolas", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var minorFont = new Font("Consolas", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var accentFont = new Font("Consolas", 14f, FontStyle.Bold, GraphicsUnit.Pixel);

            using (GraphicsPath panelPath = CreateRoundedRect(new RectangleF(36, 36, 1528, 828), 18f))
            {
                graphics.FillPath(panelBrush, panelPath);
                graphics.DrawPath(panelPen, panelPath);
            }

            graphics.DrawString($"Relationship Showcase | {snapshot.Step}", titleFont, titleBrush, 72, 72);
            graphics.DrawString($"Selected Hero: {snapshot.SelectedHero}", bodyFont, bodyBrush, 72, 122);
            graphics.DrawString($"Enemy Focus: {snapshot.EnemyFocus}", bodyFont, bodyBrush, 72, 154);
            graphics.DrawString($"Liu->Guan Loyalty: {snapshot.LoyaltyLiuToGuan}   Liu->Zhang Loyalty: {snapshot.LoyaltyLiuToZhang}", bodyFont, bodyBrush, 72, 196);
            graphics.DrawString($"Guan->Zhang Support: {snapshot.SupportGuanToZhang}   Captain Threat: {snapshot.ThreatCaptainToSelected}", bodyFont, bodyBrush, 72, 228);
            graphics.DrawString($"Overlay Counts: screen={snapshot.OverlayCounts["screen"]} ground={snapshot.OverlayCounts["ground"]} rings={snapshot.OverlayCounts["rings"]}", minorFont, minorBrush, 72, 262);

            DrawStatusPill(graphics, 72, 300, snapshot.TrustedUnlocked ? "Trusted Ready" : "Trusted Locked", snapshot.TrustedUnlocked);
            DrawStatusPill(graphics, 280, 300, snapshot.OathBondUnlocked ? "Oath Bond Ready" : "Oath Bond Locked", snapshot.OathBondUnlocked);
            DrawStatusPill(graphics, 538, 300, snapshot.SynergyActive ? "Synergy Ready" : "Synergy Locked", snapshot.SynergyActive);

            graphics.DrawString("Hero State", accentFont, accentBrush, 72, 386);
            int heroY = 458;
            for (int i = 0; i < snapshot.Heroes.Count; i++)
            {
                HeroSnapshot hero = snapshot.Heroes[i];
                graphics.DrawString($"{hero.Name}: HP {hero.Health:0}  Shield {hero.Shield:0}", bodyFont, bodyBrush, 72, heroY);
                heroY += 28;
            }

            graphics.DrawString("Recent Battle Log", accentFont, accentBrush, 72, 560);
            int recentLogY = 638;
            for (int i = 0; i < snapshot.RecentLog.Count; i++)
            {
                graphics.DrawString(snapshot.RecentLog[i], minorFont, minorBrush, 72, recentLogY);
                recentLogY += 24;
            }

            graphics.DrawString("Visible Panel Text", accentFont, accentBrush, 820, 122);
            int panelY = 182;
            for (int i = 0; i < snapshot.ScreenText.Count; i++)
            {
                graphics.DrawString(snapshot.ScreenText[i], minorFont, minorBrush, 820, panelY);
                panelY += 24;
            }

            bitmap.Save(path, ImageFormat.Png);
        }

        private static void WriteAcceptanceTimelinePng(IReadOnlyList<AcceptanceSnapshot> snapshots, string path)
        {
            if (snapshots.Count == 0)
            {
                return;
            }

            int height = Math.Max(320, 120 + snapshots.Count * 92);
            using var bitmap = new Bitmap(1600, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.FromArgb(8, 12, 18));

            using var titleBrush = new SolidBrush(Color.White);
            using var stepBrush = new SolidBrush(Color.FromArgb(246, 212, 108));
            using var detailBrush = new SolidBrush(Color.FromArgb(188, 205, 222));
            using var boxBrush = new SolidBrush(Color.FromArgb(20, 30, 44));
            using var boxPen = new Pen(Color.FromArgb(53, 83, 107), 1.5f);
            using var titleFont = new Font("Consolas", 20f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var stepFont = new Font("Consolas", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var detailFont = new Font("Consolas", 12f, FontStyle.Regular, GraphicsUnit.Pixel);

            graphics.DrawString("Relationship showcase acceptance timeline", titleFont, titleBrush, 24, 18);
            int y = 94;
            for (int i = 0; i < snapshots.Count; i++)
            {
                AcceptanceSnapshot snapshot = snapshots[i];
                using GraphicsPath boxPath = CreateRoundedRect(new RectangleF(40, y - 30, 1520, 68), 12f);
                graphics.FillPath(boxBrush, boxPath);
                graphics.DrawPath(boxPen, boxPath);
                graphics.DrawString($"{i + 1:000} {snapshot.Step}", stepFont, stepBrush, 68, y - 12);
                graphics.DrawString($"selected={snapshot.SelectedHero} | focus={snapshot.EnemyFocus} | trust={snapshot.TrustedUnlocked} | oath={snapshot.OathBondUnlocked} | synergy={snapshot.SynergyActive} | support={snapshot.SupportGuanToZhang} | threat={snapshot.ThreatCaptainToSelected}", detailFont, detailBrush, 420, y - 8);
                y += 92;
            }

            bitmap.Save(path, ImageFormat.Png);
        }

        private static void DrawStatusPill(Graphics graphics, float x, float y, string label, bool active)
        {
            using var fill = new SolidBrush(active ? Color.FromArgb(44, 108, 82) : Color.FromArgb(84, 56, 56));
            using var stroke = new Pen(active ? Color.FromArgb(119, 215, 173) : Color.FromArgb(227, 128, 128), 1.5f);
            using var textBrush = new SolidBrush(Color.White);
            using var textFont = new Font("Consolas", 13f, FontStyle.Regular, GraphicsUnit.Pixel);

            float width = 180f + label.Length * 2f;
            using GraphicsPath pillPath = CreateRoundedRect(new RectangleF(x, y, width, 38f), 19f);
            graphics.FillPath(fill, pillPath);
            graphics.DrawPath(stroke, pillPath);
            graphics.DrawString(label, textFont, textBrush, x + 16f, y + 10f);
        }

        private static GraphicsPath CreateRoundedRect(RectangleF rect, float radius)
        {
            float diameter = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Load relationship_showcase -> panel and rings visible] --> B{Press Rally before unlocks?}",
                "    B -- yes --> C[Guard branch: deny rally and keep buffs locked]",
                "    B -- no --> D[Doctrine: loyalty thresholds cross -> Trusted callbacks fire]",
                "    C --> D",
                "    D --> E[Synergy: Shu team tier activates on team meta entity]",
                "    E --> F[Oath Drill: support rank crosses -> Oath Bond unlocked]",
                "    F --> G[Tab: selected hero rotates through authoritative input]",
                "    G --> H[Taunt: threat spikes on selected hero and focus tag lands]",
                "    H --> I[Enemy pressure tick: hostile team strikes highest-threat target]",
                "    I --> J[Rally: unlocked relationship state converts into GAS buffs]"
            }) + Environment.NewLine;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string srcDir = Path.Combine(dir.FullName, "src");
                string assetsDir = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(srcDir) && Directory.Exists(assetsDir))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private static double Median(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            var ordered = values.OrderBy(v => v).ToArray();
            int middle = ordered.Length / 2;
            if ((ordered.Length & 1) == 0)
            {
                return (ordered[middle - 1] + ordered[middle]) * 0.5d;
            }

            return ordered[middle];
        }

        private sealed record AcceptanceSnapshot(
            string Step,
            string SelectedHero,
            string EnemyFocus,
            bool TrustedUnlocked,
            bool OathBondUnlocked,
            bool SynergyActive,
            short LoyaltyLiuToGuan,
            short LoyaltyLiuToZhang,
            short SupportGuanToZhang,
            short ThreatCaptainToSelected,
            IReadOnlyDictionary<string, int> OverlayCounts,
            IReadOnlyList<string> ScreenText,
            IReadOnlyList<string> RecentLog,
            IReadOnlyList<HeroSnapshot> Heroes);

        private sealed record HeroSnapshot(
            string Name,
            float Health,
            float Shield);

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);

            public void SetButton(string devicePath, bool pressed)
            {
                _buttons[devicePath] = pressed;
            }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out bool pressed) && pressed;
            public Vector2 GetMousePosition() => new(960f, 540f);
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class StubViewController : IViewController
        {
            public StubViewController(float width, float height)
            {
                Resolution = new Vector2(width, height);
            }

            public Vector2 Resolution { get; }
            public float Fov => 60f;
            public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
        }

        private sealed class HeadlessCameraRuntime
        {
            public HeadlessCameraRuntime(CameraPresenter cameraPresenter, PresentationFrameSetupSystem? presentationFrameSetup)
            {
                CameraPresenter = cameraPresenter;
                PresentationFrameSetup = presentationFrameSetup;
            }

            public CameraPresenter CameraPresenter { get; }
            public PresentationFrameSetupSystem? PresentationFrameSetup { get; }
        }

        private sealed class StubCameraAdapter : ICameraAdapter
        {
            public CameraRenderState3D LastState { get; private set; }

            public void UpdateCamera(in CameraRenderState3D state)
            {
                LastState = state;
            }
        }
    }
}
