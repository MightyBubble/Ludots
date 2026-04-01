using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            "NarrativeFrontendMod",
            "RelationshipShowcaseMod"
        };

        [Test]
        public void RelationshipShowcase_PlayableAcceptance_WritesArtifacts()
        {
            string repoRoot = FindRepoRoot();
            RelationshipShowcaseTestConfig showcaseConfig = LoadShowcaseConfig(repoRoot);
            RelationshipShowcaseTestFrontendConfig frontendConfig = LoadFrontendConfig(repoRoot);
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "relationship-showcase");
            string screensDir = Path.Combine(artifactDir, "screens");
            AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

            var timeline = new List<string>();
            var snapshots = new List<AcceptanceSnapshot>();
            var frames = new List<UiAcceptanceEvidenceFrame>();
            var frameTimesMs = new List<double>();
            string selectedHero = showcaseConfig.Scenario.Heroes[0].Name;
            string enemyFocus = showcaseConfig.Presentation.EnemyFocusPendingText;

            using var engine = CreateEngine();
            var backend = GetInputBackend(engine);
            var uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
                ?? throw new InvalidOperationException("UIRoot missing.");
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

            TickUntil(
                engine,
                frameTimesMs,
                () =>
                {
                    IReadOnlyList<string> currentUiText = AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot);
                    return currentUiText.Any(text => text.Contains(frontendConfig.PromptRibbon.Title, StringComparison.Ordinal)) &&
                           currentUiText.Any(text => text.Contains(frontendConfig.StatusPanel.Title, StringComparison.Ordinal));
                },
                maxFrames: 30);

            IReadOnlyList<string> uiText = AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot);
            Assert.That(uiText.Any(text => text.Contains(frontendConfig.PromptRibbon.Title, StringComparison.Ordinal)), Is.True);
            Assert.That(uiText.Any(text => text.Contains(frontendConfig.StatusPanel.Title, StringComparison.Ordinal)), Is.True);
            Assert.That(CountGroundOverlays(ground, GroundOverlayShape.Ring), Is.GreaterThanOrEqualTo(1));
            CaptureSnapshot(engine, uiRoot, ground, runtime, metrics, showcaseConfig, socialBondTypeId, hostilityTypeId, snapshots, frames, "map_loaded", selectedHero, enemyFocus);
            timeline.Add("[T+001] relationship_showcase booted with Peach Garden panel text mounted and GroundOverlayBuffer ring telemetry already live.");

            PressButton(engine, backend, "<Keyboard>/4", frameTimesMs);
            Assert.That(AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot).Any(text => text.Contains(showcaseConfig.Logs.RallyDenied, StringComparison.Ordinal)), Is.True);
            CaptureSnapshot(engine, uiRoot, ground, runtime, metrics, showcaseConfig, socialBondTypeId, hostilityTypeId, snapshots, frames, "guard_branch_rally_denied", selectedHero, enemyFocus);
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
            Assert.That(AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot).Any(text => text.Contains(showcaseConfig.Presentation.TrustedLabel, StringComparison.Ordinal)), Is.True);
            Assert.That(AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot).Any(text => text.Contains(showcaseConfig.Presentation.ReadyText, StringComparison.Ordinal)), Is.True);
            Assert.That(guanShieldAfterDoctrine, Is.GreaterThan(guanShieldBeforeDoctrine));
            Assert.That(zhangShieldAfterDoctrine, Is.GreaterThan(zhangShieldBeforeDoctrine));
            Assert.That(HasTag(engine.World, FindTeamEntity(engine, showcaseConfig.SynergyTeamId), synergyTagId), Is.True);
            CaptureSnapshot(engine, uiRoot, ground, runtime, metrics, showcaseConfig, socialBondTypeId, hostilityTypeId, snapshots, frames, "doctrine_trust_synergy", selectedHero, enemyFocus);
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
            Assert.That(guanSpeedAfterDrill, Is.GreaterThan(guanSpeedBeforeDrill));
            Assert.That(zhangSpeedAfterDrill, Is.GreaterThan(zhangSpeedBeforeDrill));
            CaptureSnapshot(engine, uiRoot, ground, runtime, metrics, showcaseConfig, socialBondTypeId, hostilityTypeId, snapshots, frames, "oath_rank_up", selectedHero, enemyFocus);
            timeline.Add($"[T+004] Guan Yu + Zhang Fei.Oath Drill -> Support rank crossed to {guanToZhangSupport}/{zhangToGuanSupport} and movement buffs landed through GAS.");

            PressButton(engine, backend, "<Keyboard>/tab", frameTimesMs);
            selectedHero = guanYuName;
            Assert.That(AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot).Any(text => text.Contains(guanYuName, StringComparison.Ordinal)), Is.True);
            CaptureSnapshot(engine, uiRoot, ground, runtime, metrics, showcaseConfig, socialBondTypeId, hostilityTypeId, snapshots, frames, "selection_rotated", selectedHero, enemyFocus);
            timeline.Add("[T+005] Player rotated focus with Tab to Guan Yu, proving the showcase is playable through authoritative input.");

            float guanHealthBeforeTaunt = ReadAttribute(engine.World, guanYu, healthId);
            PressButton(engine, backend, "<Keyboard>/3", frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () =>
                    ReadAttribute(engine.World, guanYu, healthId) < guanHealthBeforeTaunt &&
                    AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot).Count(text => string.Equals(text, guanYuName, StringComparison.Ordinal)) >= 2 &&
                    AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot).All(text => !text.Contains(showcaseConfig.Presentation.EnemyFocusPendingText, StringComparison.Ordinal)),
                maxFrames: 80);

            short captainThreat = runtime.GetMetric(rebelCaptain, guanYu, hostilityTypeId, threatId);
            short spearmanThreat = runtime.GetMetric(rebelSpearman, guanYu, hostilityTypeId, threatId);
            float guanHealthAfterTaunt = ReadAttribute(engine.World, guanYu, healthId);
            enemyFocus = guanYuName;
            Assert.That(captainThreat, Is.GreaterThanOrEqualTo(70));
            Assert.That(spearmanThreat, Is.GreaterThanOrEqualTo(70));
            Assert.That(guanHealthAfterTaunt, Is.LessThan(guanHealthBeforeTaunt));
            Assert.That(HasTag(engine.World, guanYu, focusTagId), Is.True);
            CaptureSnapshot(engine, uiRoot, ground, runtime, metrics, showcaseConfig, socialBondTypeId, hostilityTypeId, snapshots, frames, "threat_focus_and_enemy_strike", selectedHero, enemyFocus);
            timeline.Add($"[T+006] Guan Yu.Taunt -> Threat(Captain={captainThreat}, Spearman={spearmanThreat}) | enemy focus locked on Guan Yu | HP {guanHealthBeforeTaunt:0} -> {guanHealthAfterTaunt:0}.");

            float liuShieldBeforeRally = ReadAttribute(engine.World, liuBei, shieldId);
            float zhangShieldBeforeRally = ReadAttribute(engine.World, zhangFei, shieldId);
            PressButton(engine, backend, "<Keyboard>/4", frameTimesMs);
            Tick(engine, 6, frameTimesMs);
            float liuShieldAfterRally = ReadAttribute(engine.World, liuBei, shieldId);
            float zhangShieldAfterRally = ReadAttribute(engine.World, zhangFei, shieldId);
            Assert.That(liuShieldAfterRally, Is.GreaterThan(liuShieldBeforeRally));
            Assert.That(zhangShieldAfterRally, Is.GreaterThan(zhangShieldBeforeRally));
            CaptureSnapshot(engine, uiRoot, ground, runtime, metrics, showcaseConfig, socialBondTypeId, hostilityTypeId, snapshots, frames, "rally_banner", selectedHero, enemyFocus);
            timeline.Add($"[T+007] Guan Yu.Rally Banner converted relationship state into shared GAS buffs | Liu Shield {liuShieldBeforeRally:0}->{liuShieldAfterRally:0} | Zhang Shield {zhangShieldBeforeRally:0}->{zhangShieldAfterRally:0}.");

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(snapshots));
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, snapshots, frames, frameTimesMs));
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());
            AcceptanceUiEvidenceWriter.WriteTimelineSheet(frames, screensDir, Path.Combine(screensDir, "timeline.png"), "Relationship showcase 5W1H screenshot flow");
            AcceptanceUiEvidenceWriter.WriteFiveWOneHMarkdown("relationship-showcase", frames, Path.Combine(artifactDir, "5w1h.md"));
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
            UIRoot uiRoot,
            GroundOverlayBuffer ground,
            RelationshipRuntime runtime,
            RelationshipMetricRegistry metrics,
            RelationshipShowcaseTestConfig showcaseConfig,
            int socialBondTypeId,
            int hostilityTypeId,
            List<AcceptanceSnapshot> snapshots,
            List<UiAcceptanceEvidenceFrame> frames,
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

            (string when, string who, string what, string where, string why, string how) = GetEvidenceMetadata(step);
            UiAcceptanceEvidenceFrame frame = AcceptanceUiEvidenceWriter.CaptureFrame(
                uiRoot,
                Path.Combine(FindRepoRoot(), "artifacts", "acceptance", "relationship-showcase", "screens"),
                snapshots.Count + 1,
                step,
                when,
                who,
                what,
                where,
                why,
                how);
            frames.Add(frame);

            snapshots.Add(new AcceptanceSnapshot(
                Step: step,
                ScreenshotFileName: frame.ScreenshotFileName,
                SelectedHero: selectedHero,
                EnemyFocus: enemyFocus,
                TrustedUnlocked: AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot).Any(text => text.Contains(showcaseConfig.Presentation.TrustedLabel, StringComparison.Ordinal)) &&
                                 AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot).Any(text => text.Contains(showcaseConfig.Presentation.ReadyText, StringComparison.Ordinal)),
                OathBondUnlocked: HasTag(engine.World, guanYu, TagRegistry.GetId(showcaseConfig.Tags.OathBond)) &&
                                  HasTag(engine.World, zhangFei, TagRegistry.GetId(showcaseConfig.Tags.OathBond)),
                SynergyActive: HasTag(engine.World, FindTeamEntity(engine, showcaseConfig.SynergyTeamId), TagRegistry.GetId(showcaseConfig.Tags.Synergy)),
                LoyaltyLiuToGuan: runtime.GetMetric(liuBei, guanYu, socialBondTypeId, loyaltyId),
                LoyaltyLiuToZhang: runtime.GetMetric(liuBei, zhangFei, socialBondTypeId, loyaltyId),
                SupportGuanToZhang: runtime.GetMetric(guanYu, zhangFei, socialBondTypeId, supportId),
                ThreatCaptainToSelected: runtime.GetMetric(rebelCaptain, selectedEntity, hostilityTypeId, threatId),
                GroundRingCount: CountGroundOverlays(ground, GroundOverlayShape.Ring),
                UiText: frame.UiHead,
                Heroes: new[]
                {
                    BuildHeroSnapshot(engine.World, liuBei, shieldId, healthId),
                    BuildHeroSnapshot(engine.World, guanYu, shieldId, healthId),
                    BuildHeroSnapshot(engine.World, zhangFei, shieldId, healthId),
            }));
        }

        private static RelationshipShowcaseTestConfig LoadShowcaseConfig(string repoRoot)
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
            return JsonSerializer.Deserialize<RelationshipShowcaseTestConfig>(stream, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize relationship showcase config.");
        }

        private static RelationshipShowcaseTestFrontendConfig LoadFrontendConfig(string repoRoot)
        {
            string path = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "relationship",
                "RelationshipShowcaseMod",
                "assets",
                "Frontend",
                "relationship_frontend.json");
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<RelationshipShowcaseTestFrontendConfig>(stream, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize relationship showcase frontend config.");
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private sealed class RelationshipShowcaseTestConfig
        {
            public string MapId { get; set; } = string.Empty;
            public int SynergyTeamId { get; set; }
            public RelationshipScenarioConfig Scenario { get; set; } = new();
            public RelationshipMetricNames Metrics { get; set; } = new();
            public RelationshipTypeNames Types { get; set; } = new();
            public RelationshipTagNames Tags { get; set; } = new();
            public RelationshipPresentationConfig Presentation { get; set; } = new();
            public RelationshipLogConfig Logs { get; set; } = new();
        }

        private sealed class RelationshipScenarioConfig
        {
            public RelationshipActorConfig[] Heroes { get; set; } = Array.Empty<RelationshipActorConfig>();
            public RelationshipActorConfig[] Enemies { get; set; } = Array.Empty<RelationshipActorConfig>();
        }

        private sealed class RelationshipActorConfig
        {
            public string Name { get; set; } = string.Empty;
        }

        private sealed class RelationshipMetricNames
        {
            public string Loyalty { get; set; } = string.Empty;
            public string Support { get; set; } = string.Empty;
            public string Threat { get; set; } = string.Empty;
        }

        private sealed class RelationshipTypeNames
        {
            public string SocialBond { get; set; } = string.Empty;
            public string Hostility { get; set; } = string.Empty;
        }

        private sealed class RelationshipTagNames
        {
            public string Synergy { get; set; } = string.Empty;
            public string FocusedByEnemy { get; set; } = string.Empty;
            public string OathBond { get; set; } = string.Empty;
        }

        private sealed class RelationshipPresentationConfig
        {
            public string EnemyFocusPendingText { get; set; } = string.Empty;
            public string TrustedLabel { get; set; } = string.Empty;
            public string ReadyText { get; set; } = string.Empty;
        }

        private sealed class RelationshipLogConfig
        {
            public string RallyDenied { get; set; } = string.Empty;
        }

        private sealed class RelationshipShowcaseTestFrontendConfig
        {
            public RelationshipSurfaceTitleConfig PromptRibbon { get; set; } = new();
            public RelationshipSurfaceTitleConfig StatusPanel { get; set; } = new();
        }

        private sealed class RelationshipSurfaceTitleConfig
        {
            public string Title { get; set; } = string.Empty;
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
                    logical_time = $"T+{i + 1:000}",
                    step = snapshot.Step,
                    screenshot = snapshot.ScreenshotFileName,
                    selected_hero = snapshot.SelectedHero,
                    enemy_focus = snapshot.EnemyFocus,
                    trusted_unlocked = snapshot.TrustedUnlocked,
                    oath_bond_unlocked = snapshot.OathBondUnlocked,
                    synergy_active = snapshot.SynergyActive,
                    loyalty_liu_to_guan = snapshot.LoyaltyLiuToGuan,
                    loyalty_liu_to_zhang = snapshot.LoyaltyLiuToZhang,
                    support_guan_to_zhang = snapshot.SupportGuanToZhang,
                    threat_captain_to_selected = snapshot.ThreatCaptainToSelected,
                    ground_ring_count = snapshot.GroundRingCount,
                    ui_text = snapshot.UiText,
                    heroes = snapshot.Heroes,
                    status = "done"
                }));
            }

            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        private static string BuildBattleReport(
            IReadOnlyList<string> timeline,
            IReadOnlyList<AcceptanceSnapshot> snapshots,
            IReadOnlyList<UiAcceptanceEvidenceFrame> frames,
            IReadOnlyList<double> frameTimesMs)
        {
            AcceptanceSnapshot final = snapshots[^1];
            double medianTick = Median(frameTimesMs);
            double maxTick = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
            string buildStamp = typeof(GameEngine).Assembly.GetName().Version?.ToString() ?? "unknown";

            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: relationship-showcase");
            sb.AppendLine();
            sb.AppendLine("## Header");
            sb.AppendLine("- scenario: `relationship-showcase`");
            sb.AppendLine($"- build: `GameEngine {buildStamp}`");
            sb.AppendLine($"- execution_timestamp_utc: `{DateTimeOffset.UtcNow:O}`");
            sb.AppendLine("- map: `relationship_showcase`");
            sb.AppendLine("- clock: `fixed 1/60s`");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: prove one reusable relationship runtime can drive CRPG trust, JRPG support rank, auto-battler synergy tiers, and Three Kingdoms oath fantasy inside a playable Ludots mod.");
            sb.AppendLine("- Gameplay domain: ECS relationship edges, team meta-entity synergy, GAS effects, Trigger callbacks, authoritative input, ground overlay rings, and one reusable narrative frontend scene.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: none");
            sb.AppendLine("- Map: `relationship_showcase`");
            sb.AppendLine($"- Mods: `{string.Join("`, `", AcceptanceMods)}`");
            sb.AppendLine("- Clock profile: fixed `1/60s`, headless `GameEngine.Tick()` loop.");
            sb.AppendLine("- Input source: production `InputConfigPipelineLoader` + `PlayerInputHandler` backed by a deterministic keyboard backend.");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Load the Peach Garden showcase, confirm the relationship panel mounts, and verify ground ring telemetry is non-zero.");
            sb.AppendLine("2. Trigger the rally guard branch before trust, oath, and synergy are ready.");
            sb.AppendLine("3. Cast Benevolence Doctrine to unlock CRPG-style trust thresholds and auto-battler team synergy.");
            sb.AppendLine("4. Run Oath Drill to push JRPG-style support rank over the unlock threshold.");
            sb.AppendLine("5. Rotate selection, taunt into enemy focus, wait for threat-driven strikes, then rally to cash out the unlocked relationship state as GAS buffs.");
            sb.AppendLine();
            sb.AppendLine("## Expected Outcomes");
            sb.AppendLine("- Primary success condition: relationship callbacks, team synergy, Trigger events, and GAS effects all resolve on the production runtime path.");
            sb.AppendLine("- Failure branch condition: pressing rally before unlocks exist must deny cleanly without granting buffs.");
            sb.AppendLine("- Key metrics: loyalty, support, threat, synergy state, selected/focused hero, shield/health deltas, UI surface text, and ground ring telemetry.");
            sb.AppendLine();
            sb.AppendLine("## Evidence Artifacts");
            sb.AppendLine("- `artifacts/acceptance/relationship-showcase/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/relationship-showcase/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/relationship-showcase/path.mmd`");
            sb.AppendLine("- `artifacts/acceptance/relationship-showcase/5w1h.md`");
            for (int i = 0; i < frames.Count; i++)
            {
                sb.AppendLine($"- `artifacts/acceptance/relationship-showcase/screens/{frames[i].ScreenshotFileName}`");
            }
            sb.AppendLine("- `artifacts/acceptance/relationship-showcase/screens/timeline.png`");
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
            sb.AppendLine($"- final ground rings: `{final.GroundRingCount}`");
            sb.AppendLine($"- final ui excerpt: `{string.Join(" | ", final.UiText.Take(5))}`");
            sb.AppendLine("- reusable wiring: `RelationshipRuntime`, `RelationshipChangeBuffer`, `RelationshipCatalogPipelineLoader`, `RelationshipCatalogInstaller`, `RelationshipProcessingSystem`, `RelationshipCallbackProcessor`, `RelationshipSynergyProcessor`, `TriggerManager`, `EffectRequestQueue`, `TeamEntityLookup`");
            return sb.ToString();
        }

        private static (string When, string Who, string What, string Where, string Why, string How) GetEvidenceMetadata(string step)
        {
            return step switch
            {
                "map_loaded" => ("T+001", "Liu Bei, Guan Yu, Zhang Fei, and the shared relationship HUD", "Boot the showcase and confirm faction state, named bonds, notification stack, and threat banner while ring telemetry is already active.", "relationship_showcase", "Verify the shared frontend is mounted before any relationship action mutates runtime state.", "Load the real mod set, tick the engine, snapshot the mounted UIRoot scene, and record GroundOverlayBuffer ring counts beside the screenshot."),
                "guard_branch_rally_denied" => ("T+002", "Guan Yu's rally input and the locked relationship state", "Trigger the rally guard branch before trust, oath, and synergy unlock.", "The Peach Garden formation before thresholds are met.", "Prove the runtime can deny a branch cleanly without leaking buffs.", "Press Rally immediately and wait for the denial message to surface on the frontend."),
                "doctrine_trust_synergy" => ("T+003", "Liu Bei and the doctrine-driven trust state", "Cast Benevolence Doctrine and show trust plus synergy thresholds crossing together.", "The center formation after doctrine resolves.", "Validate CRPG trust callbacks and auto-battler synergy reuse the same runtime edges.", "Apply doctrine through authoritative input, then wait for loyalty metrics, synergy tags, and shield buffs."),
                "oath_rank_up" => ("T+004", "Guan Yu, Zhang Fei, and the oath support ladder", "Run Oath Drill and capture the JRPG-style support rank upgrade.", "The brotherhood formation while the notebook and flow review remain visible.", "Prove support-rank style progression is just another projection of the same relationship runtime.", "Trigger drill input and wait until both support metrics cross the unlock threshold."),
                "selection_rotated" => ("T+005", "The selected hero panel and Guan Yu focus", "Rotate player focus with Tab and show the frontend updating the selected hero.", "The same tactical setup with authoritative input still active.", "Demonstrate the frontend reflects gameplay-owned selection instead of owning selection itself.", "Press Tab and snapshot the shared status panel once Guan Yu becomes the active hero."),
                "threat_focus_and_enemy_strike" => ("T+006", "Guan Yu, enemy focus, and the threat banner", "Taunt into enemy focus and capture the strike feedback on the threat surfaces.", "The battlefield after hostility metrics spike onto Guan Yu.", "Validate Detroit-like consequence visibility and RTS/4X threat readability through the same projection kit.", "Apply Taunt, wait for threat metrics and enemy strike damage, then snapshot the updated frontend."),
                "rally_banner" => ("T+007", "Guan Yu, the brotherhood buffs, and the completed readiness stack", "Cash out the unlocked relationship state into Rally Banner buffs.", "The Peach Garden formation after trust, oath, and synergy are all online.", "Show relationship state can convert into reusable GAS outcomes once every gate is satisfied.", "Press Rally after all thresholds are live and verify shields rise while the frontend reflects the pay-off."),
                _ => ($"T+{step}", "Relationship showcase actors", "Capture the current relationship frontend state.", "The active showcase map.", "Keep the screenshot flow auditable.", "Snapshot the mounted UIRoot scene.")
            };
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Load relationship_showcase -> panel mounts and ring telemetry is live] --> B{Press Rally before unlocks?}",
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
            string ScreenshotFileName,
            string SelectedHero,
            string EnemyFocus,
            bool TrustedUnlocked,
            bool OathBondUnlocked,
            bool SynergyActive,
            short LoyaltyLiuToGuan,
            short LoyaltyLiuToZhang,
            short SupportGuanToZhang,
            short ThreatCaptainToSelected,
            int GroundRingCount,
            IReadOnlyList<string> UiText,
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
