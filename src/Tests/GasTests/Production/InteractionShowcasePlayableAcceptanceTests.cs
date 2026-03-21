using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Selection;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    public sealed class InteractionShowcasePlayableAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string HubMapId = "interaction_showcase_hub";
        private const string StressMapId = "interaction_showcase_stress";
        private const string WowModeId = "Interaction.Mode.WoW";
        private const string LolModeId = "Interaction.Mode.LoL";
        private const string Sc2ModeId = "Interaction.Mode.SC2";
        private const string IndicatorModeId = "Interaction.Mode.Indicator";
        private const string ActionModeId = "Interaction.Mode.Action";
        private const string StressTelemetryKey = "InteractionShowcaseMod.StressTelemetry";
        private const string TestInputBackendKey = "Tests.InteractionShowcase.InputBackend";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "InteractionShowcaseMod"
        };

        private static readonly string[] TrackedNames =
        {
            "Arcweaver",
            "Vanguard",
            "Commander",
            "EnemySkirmisherA",
            "EnemySkirmisherB",
            "EnemyBruiserA",
            "EnemyBruiserB",
            "StressRedAnchor",
            "StressBlueAnchor"
        };

        private static readonly string[] TrackedTagNames =
        {
            "State.Arcweaver.Guarding",
            "State.Vanguard.IronWall",
            "Cooldown.Arcweaver.Z",
            "Cooldown.Vanguard.E",
            "Cooldown.Commander.Z",
            "Cooldown.Commander.E",
            "Cooldown.Commander.R"
        };

        [Test]
        public void InteractionShowcase_PlayableFlow_WritesAcceptanceArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "interaction-showcase");
            Directory.CreateDirectory(artifactDir);

            var snapshots = new List<AcceptanceSnapshot>();
            var timeline = new List<string>();
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();

            var uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
                ?? throw new InvalidOperationException("UIRoot was not installed.");
            var backend = GetInputBackend(engine);
            var overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
                ?? throw new InvalidOperationException("GroundOverlayBuffer was not installed.");

            LoadMap(engine, HubMapId, frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

            var hubText = ExtractUiText(uiRoot);
            Assert.That(uiRoot.Scene, Is.Not.Null, "Showcase HUD should mount on hub map.");
            Assert.That(hubText.Any(text => text.Contains("Ludots Interaction Showcase", StringComparison.Ordinal)), Is.True);
            CaptureSnapshot(engine, uiRoot, overlays, snapshots, frameTimesMs, "hub_loaded");
            timeline.Add($"[T+001] Hub loaded | HUD mounted | active mode {GetActiveModeId(engine)}");

            SelectNamedEntity(engine, backend, "Arcweaver", frameTimesMs);
            AssertSelectedEntity(engine, "Arcweaver");
            PressButton(engine, backend, "<Keyboard>/f1", frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(WowModeId));
            PressButton(engine, backend, "<Keyboard>/f2", frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(LolModeId));
            CaptureSnapshot(engine, uiRoot, overlays, snapshots, frameTimesMs, "arcweaver_selected_lol");
            timeline.Add("[T+002] Arcweaver selected | switched F1 WoW then F2 LoL");

            float skirmisherAHealthBeforeQ = ReadHealth(engine.World, "EnemySkirmisherA");
            SetMouseWorld(engine, backend, GetEntityScreen(engine, "EnemySkirmisherA"), frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/q", frameTimesMs);
            Tick(engine, 4, frameTimesMs);
            float skirmisherAHealthAfterQ = ReadHealth(engine.World, "EnemySkirmisherA");
            Assert.That(skirmisherAHealthAfterQ, Is.LessThan(skirmisherAHealthBeforeQ));
            CaptureSnapshot(engine, uiRoot, overlays, snapshots, frameTimesMs, "arcweaver_q_smartcast");
            timeline.Add($"[T+003] LoL smart-cast Q hit EnemySkirmisherA | HP {skirmisherAHealthBeforeQ:0} -> {skirmisherAHealthAfterQ:0}");

            DragSelectNamed(engine, backend, frameTimesMs, "Arcweaver", "Vanguard", "Commander");
            AssertSelectionCount(engine, 3);

            RightClickWorld(engine, backend, new Vector2(1500f, 900f), frameTimesMs);
            HoldModifier(engine, backend, "<Keyboard>/leftShift", true, frameTimesMs);
            RightClickWorld(engine, backend, new Vector2(1650f, 900f), frameTimesMs);
            HoldModifier(engine, backend, "<Keyboard>/leftShift", false, frameTimesMs);
            Tick(engine, 2, frameTimesMs);
            AssertQueuedMoveOrders(engine, minimumQueuedOrders: 1);
            AssertMoveRuntimeActivated(engine, "Arcweaver", "Vanguard", "Commander");

            Vector2 arcBeforeMove = ReadPosition(engine.World, "Arcweaver");
            Tick(engine, 20, frameTimesMs);
            Vector2 arcAfterMove = ReadPosition(engine.World, "Arcweaver");
            Assert.That(Vector2.Distance(arcAfterMove, arcBeforeMove), Is.GreaterThan(60f));

            PressButton(engine, backend, "<Keyboard>/s", frameTimesMs);
            Tick(engine, 12, frameTimesMs);
            Vector2 vanguardAfterStop = ReadPosition(engine.World, "Vanguard");
            Tick(engine, 12, frameTimesMs);
            Vector2 vanguardAfterStopSettled = ReadPosition(engine.World, "Vanguard");
            Assert.That(Vector2.Distance(vanguardAfterStopSettled, vanguardAfterStop), Is.LessThan(2f));
            CaptureSnapshot(engine, uiRoot, overlays, snapshots, frameTimesMs, "multi_select_queue_stop");
            timeline.Add("[T+004] Drag-selected all three heroes | RMB move | Shift-queued second move | S stop halted formation");

            SelectNamedEntity(engine, backend, "Arcweaver", frameTimesMs);
            AssertSelectedEntity(engine, "Arcweaver");

            Vector2 arcweaverBeforeBlink = ReadPosition(engine.World, "Arcweaver");
            SetMouseWorld(engine, backend, arcweaverBeforeBlink + new Vector2(400f, 0f), frameTimesMs);
            backend.SetButton("<Keyboard>/w", true);
            Tick(engine, 1, frameTimesMs);
            string blinkInputDiagnosticsFrame1 = BuildInputActionDiagnostics(engine, "SkillW");
            Tick(engine, 1, frameTimesMs);
            string blinkInputDiagnosticsFrame2 = BuildInputActionDiagnostics(engine, "SkillW");
            backend.SetButton("<Keyboard>/w", false);
            Tick(engine, 2, frameTimesMs);
            string blinkCastDiagnostics = BuildAbilityDiagnostics(engine, "Arcweaver");
            Tick(engine, 12, frameTimesMs);
            Vector2 arcweaverAfterBlink = ReadPosition(engine.World, "Arcweaver");
            Assert.That(
                Vector2.Distance(arcweaverAfterBlink, arcweaverBeforeBlink),
                Is.GreaterThan(180f),
                $"{blinkInputDiagnosticsFrame1} || {blinkInputDiagnosticsFrame2} || {blinkCastDiagnostics} || {BuildAbilityDiagnostics(engine, "Arcweaver")}");

            int arcGuardTagId = TagRegistry.GetId("State.Arcweaver.Guarding");
            PressButton(engine, backend, "<Keyboard>/f", frameTimesMs);
            Tick(engine, 2, frameTimesMs);
            Assert.That(HasTag(engine.World, "Arcweaver", arcGuardTagId), Is.True);
            Tick(engine, 14, frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/f", frameTimesMs);
            Tick(engine, 2, frameTimesMs);
            Assert.That(HasTag(engine.World, "Arcweaver", arcGuardTagId), Is.False);

            Vector2 arcweaverBeforeDash = ReadPosition(engine.World, "Arcweaver");
            SetMouseWorld(engine, backend, arcweaverBeforeDash + new Vector2(260f, 0f), frameTimesMs);
            DoubleTapKey(engine, backend, "<Keyboard>/z", frameTimesMs);
            Tick(engine, 12, frameTimesMs);
            Vector2 arcweaverAfterDash = ReadPosition(engine.World, "Arcweaver");
            Assert.That(Vector2.Distance(arcweaverAfterDash, arcweaverBeforeDash), Is.GreaterThan(120f));

            float skirmisherAHealthBeforeVector = ReadHealth(engine.World, "EnemySkirmisherA");
            Vector2 runeOrigin = ReadPosition(engine.World, "Arcweaver");
            Vector2 runeEndpoint = ReadPosition(engine.World, "EnemySkirmisherA");
            PressChord(engine, backend, "<Keyboard>/x", "<Keyboard>/c", frameTimesMs);
            string runeBurstAfterChord = BuildInputActionDiagnostics(engine, "RuneBurst");
            string arcweaverAfterChord = BuildAbilityDiagnostics(engine, "Arcweaver");
            LeftClickWorld(engine, backend, runeOrigin, frameTimesMs);
            string runeBurstAfterOrigin = BuildInputActionDiagnostics(engine, "RuneBurst");
            string arcweaverAfterOrigin = BuildAbilityDiagnostics(engine, "Arcweaver");
            LeftClickWorld(engine, backend, runeEndpoint, frameTimesMs);
            string runeBurstAfterEndpoint = BuildInputActionDiagnostics(engine, "RuneBurst");
            string arcweaverAfterEndpoint = BuildAbilityDiagnostics(engine, "Arcweaver");
            Tick(engine, 4, frameTimesMs);
            float skirmisherAHealthAfterVector = ReadHealth(engine.World, "EnemySkirmisherA");
            Assert.That(
                skirmisherAHealthAfterVector,
                Is.LessThan(skirmisherAHealthBeforeVector),
                $"{runeBurstAfterChord} || {arcweaverAfterChord} || {runeBurstAfterOrigin} || {arcweaverAfterOrigin} || {runeBurstAfterEndpoint} || {arcweaverAfterEndpoint} || selected={string.Join(",", GetSelectedNames(engine))}");
            CaptureSnapshot(engine, uiRoot, overlays, snapshots, frameTimesMs, "arcweaver_movement_toggle_vector");
            timeline.Add("[T+005] Arcweaver demoed point blink, toggle stance, double-tap dash, and vector rune burst");

            SelectNamedEntity(engine, backend, "Vanguard", frameTimesMs);
            AssertSelectedEntity(engine, "Vanguard");

            string vanguardTarget = FindNearestEnemy(engine.World, "Vanguard");
            PressButton(engine, backend, "<Keyboard>/f4", frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(IndicatorModeId));
            SetMouseWorld(engine, backend, GetEntityScreen(engine, vanguardTarget), frameTimesMs);
            int baselineRings = CountOverlays(overlays, GroundOverlayShape.Ring);
            HoldButton(engine, backend, "<Keyboard>/r", holdFrames: 2, frameTimesMs);
            int ringOverlays = CountOverlays(overlays, GroundOverlayShape.Ring);
            string vanguardIndicatorDiagnostics = string.Join(" || ",
                BuildInputActionDiagnostics(engine, "SkillR"),
                BuildAbilityDiagnostics(engine, "Vanguard"),
                BuildSelectionStateDiagnostics(engine),
                BuildOverlayDiagnostics(overlays));
            Assert.That(ringOverlays, Is.GreaterThan(baselineRings), vanguardIndicatorDiagnostics);
            float vanguardTargetHealthBeforeR = ReadHealth(engine.World, vanguardTarget);
            ReleaseButton(engine, backend, "<Keyboard>/r", frameTimesMs);
            Tick(engine, 4, frameTimesMs);
            float vanguardTargetHealthAfterR = ReadHealth(engine.World, vanguardTarget);
            Assert.That(vanguardTargetHealthAfterR, Is.LessThan(vanguardTargetHealthBeforeR));

            PressButton(engine, backend, "<Keyboard>/f3", frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(Sc2ModeId));
            float vanguardTargetHealthBeforeCancel = ReadHealth(engine.World, vanguardTarget);
            SetMouseWorld(engine, backend, GetEntityScreen(engine, vanguardTarget), frameTimesMs);
            int baselineAimCastOverlays = overlays.Count;
            PressButton(engine, backend, "<Keyboard>/e", frameTimesMs);
            Tick(engine, 1, frameTimesMs);
            string vanguardAimDiagnostics = string.Join(" || ",
                BuildInputActionDiagnostics(engine, "SkillE"),
                BuildAbilityDiagnostics(engine, "Vanguard"),
                BuildSelectionStateDiagnostics(engine),
                BuildOverlayDiagnostics(overlays));
            Assert.That(CountOverlays(overlays, GroundOverlayShape.Cone), Is.GreaterThan(0), vanguardAimDiagnostics);
            RightClickWorld(engine, backend, GetEntityScreen(engine, vanguardTarget), frameTimesMs);
            Tick(engine, 2, frameTimesMs);
            Assert.That(CountOverlays(overlays, GroundOverlayShape.Cone), Is.EqualTo(0), BuildOverlayDiagnostics(overlays));
            Assert.That(overlays.Count, Is.EqualTo(baselineAimCastOverlays), BuildOverlayDiagnostics(overlays));
            float vanguardTargetHealthAfterCancel = ReadHealth(engine.World, vanguardTarget);
            Assert.That(vanguardTargetHealthAfterCancel, Is.EqualTo(vanguardTargetHealthBeforeCancel).Within(0.001f));

            PressButton(engine, backend, "<Keyboard>/e", frameTimesMs);
            Tick(engine, 1, frameTimesMs);
            LeftClickWorld(engine, backend, GetEntityScreen(engine, vanguardTarget), frameTimesMs);
            Tick(engine, 4, frameTimesMs);
            float vanguardTargetHealthAfterConfirm = ReadHealth(engine.World, vanguardTarget);
            Assert.That(vanguardTargetHealthAfterConfirm, Is.LessThan(vanguardTargetHealthAfterCancel));
            CaptureSnapshot(engine, uiRoot, overlays, snapshots, frameTimesMs, "vanguard_indicator_and_aimcast");
            timeline.Add("[T+006] Vanguard demoed indicator-release ring AoE plus SC2 aim-cast cancel/confirm branches");

            SelectNamedEntity(engine, backend, "Commander", frameTimesMs);
            AssertSelectedEntity(engine, "Commander");
            PressButton(engine, backend, "<Keyboard>/f5", frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(ActionModeId));
            string commanderTarget = FindNearestEnemy(engine.World, "Commander");
            SetMouseWorld(engine, backend, GetEntityScreen(engine, commanderTarget), frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/space", frameTimesMs);
            Tick(engine, 3, frameTimesMs);
            Assert.That(HasAnyTag(engine.World, "Commander", "Cooldown.Commander.Z", "Cooldown.Commander.E", "Cooldown.Commander.R"), Is.True);
            CaptureSnapshot(engine, uiRoot, overlays, snapshots, frameTimesMs, "commander_context_scored");
            timeline.Add("[T+007] Commander context-scored action fired from Space and resolved into a concrete cooldown-gated skill");

            engine.UnloadMap(HubMapId);
            Tick(engine, 2, frameTimesMs);
            LoadMap(engine, StressMapId, frameTimesMs);
            var stressText = ExtractUiText(uiRoot);
            Assert.That(stressText.Any(text => text.Contains("Stress Throughput", StringComparison.Ordinal)), Is.True);

            TickUntil(engine, frameTimesMs, () =>
            {
                var telemetry = ReadStressState(engine);
                return telemetry.RequestedRed == 1600 &&
                       telemetry.RequestedBlue == 1600 &&
                       telemetry.LiveRed >= 1500 &&
                       telemetry.LiveBlue >= 1500 &&
                       telemetry.WavesDispatched > 0 &&
                       telemetry.PeakProjectileCount > 0;
            }, maxFrames: 600);

            var stressState = ReadStressState(engine);
            Assert.That(stressState.LiveRed, Is.GreaterThanOrEqualTo(1500));
            Assert.That(stressState.LiveBlue, Is.GreaterThanOrEqualTo(1500));
            Assert.That(stressState.WavesDispatched, Is.GreaterThan(0));
            Assert.That(stressState.PeakProjectileCount, Is.GreaterThan(0));
            CaptureSnapshot(engine, uiRoot, overlays, snapshots, frameTimesMs, "stress_saturated");
            timeline.Add($"[T+008] Stress map saturated to red={stressState.LiveRed} blue={stressState.LiveBlue} with peak projectiles={stressState.PeakProjectileCount}");

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(snapshots));
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, snapshots, frameTimesMs));
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());
        }

        [Test]
        public void InteractionShowcase_ArcweaverBlinkStep_DirectPointCast_MovesActor()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();

            LoadMap(engine, HubMapId, frameTimesMs);
            Entity actor = FindEntityByName(engine.World, "Arcweaver");
            Assert.That(actor, Is.Not.EqualTo(Entity.Null));

            Vector2 before = ReadPosition(engine.World, "Arcweaver");
            Vector2 target = before + new Vector2(400f, 0f);

            CastAbilityAtWorldPoint(engine, actor, slot: 1, target);
            Tick(engine, 6, frameTimesMs);

            Vector2 after = ReadPosition(engine.World, "Arcweaver");
            Assert.That(
                Vector2.Distance(after, before),
                Is.GreaterThan(180f),
                BuildAbilityDiagnostics(engine, "Arcweaver"));
        }

        [Test]
        public void InteractionShowcase_ArcweaverBlinkStep_SmartCastInput_MovesActor()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            var backend = GetInputBackend(engine);

            LoadMap(engine, HubMapId, frameTimesMs);
            SelectNamedEntity(engine, backend, "Arcweaver", frameTimesMs);
            AssertSelectedEntity(engine, "Arcweaver");
            PressButton(engine, backend, "<Keyboard>/f2", frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(LolModeId));

            Vector2 before = ReadPosition(engine.World, "Arcweaver");
            SetMouseWorld(engine, backend, before + new Vector2(400f, 0f), frameTimesMs);

            backend.SetButton("<Keyboard>/w", true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton("<Keyboard>/w", false);
            Tick(engine, 6, frameTimesMs);

            Vector2 after = ReadPosition(engine.World, "Arcweaver");
            Assert.That(
                Vector2.Distance(after, before),
                Is.GreaterThan(180f),
                $"{BuildInputActionDiagnostics(engine, "SkillW")} || {BuildAbilityDiagnostics(engine, "Arcweaver")}");
        }

        [Test]
        public void InteractionShowcase_ArcweaverGuardToggle_SecondCastDeactivates()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            var backend = GetInputBackend(engine);

            LoadMap(engine, HubMapId, frameTimesMs);
            SelectNamedEntity(engine, backend, "Arcweaver", frameTimesMs);
            AssertSelectedEntity(engine, "Arcweaver");

            Entity actor = FindEntityByName(engine.World, "Arcweaver");
            int guardTagId = TagRegistry.GetId("State.Arcweaver.Guarding");
            int cooldownTagId = TagRegistry.GetId("Cooldown.Arcweaver.F");

            PressButton(engine, backend, "<Keyboard>/f", frameTimesMs);
            Tick(engine, 2, frameTimesMs);

            string afterActivate = BuildTagStateDiagnostics(engine, actor, guardTagId, cooldownTagId);
            Assert.That(HasTag(engine.World, actor, guardTagId), Is.True, afterActivate);
            Assert.That(GetTagCount(engine.World, actor, cooldownTagId), Is.EqualTo(1), afterActivate);

            Tick(engine, 14, frameTimesMs);
            string beforeDeactivate = BuildTagStateDiagnostics(engine, actor, guardTagId, cooldownTagId);

            PressButton(engine, backend, "<Keyboard>/f", frameTimesMs);
            Tick(engine, 2, frameTimesMs);

            string afterDeactivate = BuildTagStateDiagnostics(engine, actor, guardTagId, cooldownTagId);
            Assert.That(
                HasTag(engine.World, actor, guardTagId),
                Is.False,
                $"{afterActivate} || {beforeDeactivate} || {afterDeactivate} || {BuildAbilityDiagnostics(engine, "Arcweaver")}");
        }

        [Test]
        public void InteractionShowcase_ArcweaverRuneBurst_DirectVectorCast_DamagesTarget()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();

            LoadMap(engine, HubMapId, frameTimesMs);
            Entity actor = FindEntityByName(engine.World, "Arcweaver");
            Assert.That(actor, Is.Not.EqualTo(Entity.Null));

            float healthBefore = ReadHealth(engine.World, "EnemySkirmisherA");
            Vector2 origin = ReadPosition(engine.World, "Arcweaver");
            Vector2 endpoint = ReadPosition(engine.World, "EnemySkirmisherA");

            CastAbilityWithVector(engine, actor, slot: 7, origin, endpoint);
            Tick(engine, 6, frameTimesMs);

            float healthAfter = ReadHealth(engine.World, "EnemySkirmisherA");
            Assert.That(
                healthAfter,
                Is.LessThan(healthBefore),
                $"{BuildAbilityDiagnostics(engine, "Arcweaver")} || {BuildRuneBurstResolverDiagnostics(engine, actor, origin, endpoint)}");
        }

        [Test]
        public void InteractionShowcase_ArcweaverRuneBurst_ChordInput_DamagesTarget()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            var backend = GetInputBackend(engine);

            LoadMap(engine, HubMapId, frameTimesMs);
            SelectNamedEntity(engine, backend, "Arcweaver", frameTimesMs);
            AssertSelectedEntity(engine, "Arcweaver");

            float healthBefore = ReadHealth(engine.World, "EnemySkirmisherA");
            Vector2 origin = ReadPosition(engine.World, "Arcweaver");
            Vector2 endpoint = ReadPosition(engine.World, "EnemySkirmisherA");

            PressChord(engine, backend, "<Keyboard>/x", "<Keyboard>/c", frameTimesMs);
            LeftClickWorld(engine, backend, origin, frameTimesMs);
            LeftClickWorld(engine, backend, endpoint, frameTimesMs);
            Tick(engine, 6, frameTimesMs);

            float healthAfter = ReadHealth(engine.World, "EnemySkirmisherA");
            Assert.That(
                healthAfter,
                Is.LessThan(healthBefore),
                $"{BuildInputActionDiagnostics(engine, "RuneBurst")} || {BuildAbilityDiagnostics(engine, "Arcweaver")} || {BuildRuneBurstResolverDiagnostics(engine, FindEntityByName(engine.World, "Arcweaver"), origin, endpoint)}");
        }

        [Test]
        public void InteractionShowcase_ArcweaverRuneBurst_AfterArcDash_DirectVectorCast_DamagesTarget()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            var backend = GetInputBackend(engine);

            LoadMap(engine, HubMapId, frameTimesMs);
            SelectNamedEntity(engine, backend, "Arcweaver", frameTimesMs);
            AssertSelectedEntity(engine, "Arcweaver");
            PressButton(engine, backend, "<Keyboard>/f2", frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(LolModeId));

            Vector2 beforeDash = ReadPosition(engine.World, "Arcweaver");
            SetMouseWorld(engine, backend, beforeDash + new Vector2(260f, 0f), frameTimesMs);
            DoubleTapKey(engine, backend, "<Keyboard>/z", frameTimesMs);
            Tick(engine, 12, frameTimesMs);

            float healthBefore = ReadHealth(engine.World, "EnemySkirmisherA");
            Vector2 origin = ReadPosition(engine.World, "Arcweaver");
            Vector2 endpoint = ReadPosition(engine.World, "EnemySkirmisherA");

            Entity actor = FindEntityByName(engine.World, "Arcweaver");
            CastAbilityWithVector(engine, actor, slot: 7, origin, endpoint);
            Tick(engine, 6, frameTimesMs);

            float healthAfter = ReadHealth(engine.World, "EnemySkirmisherA");
            Assert.That(
                healthAfter,
                Is.LessThan(healthBefore),
                BuildAbilityDiagnostics(engine, "Arcweaver"));
        }

        [Test]
        public void InteractionShowcase_ArcweaverRuneBurst_AfterArcDash_ChordInput_DamagesTarget()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            var backend = GetInputBackend(engine);

            LoadMap(engine, HubMapId, frameTimesMs);
            SelectNamedEntity(engine, backend, "Arcweaver", frameTimesMs);
            AssertSelectedEntity(engine, "Arcweaver");
            PressButton(engine, backend, "<Keyboard>/f2", frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(LolModeId));

            Vector2 beforeDash = ReadPosition(engine.World, "Arcweaver");
            SetMouseWorld(engine, backend, beforeDash + new Vector2(260f, 0f), frameTimesMs);
            DoubleTapKey(engine, backend, "<Keyboard>/z", frameTimesMs);
            Tick(engine, 12, frameTimesMs);

            float healthBefore = ReadHealth(engine.World, "EnemySkirmisherA");
            Vector2 origin = ReadPosition(engine.World, "Arcweaver");
            Vector2 endpoint = ReadPosition(engine.World, "EnemySkirmisherA");

            PressChord(engine, backend, "<Keyboard>/x", "<Keyboard>/c", frameTimesMs);
            LeftClickWorld(engine, backend, origin, frameTimesMs);
            LeftClickWorld(engine, backend, endpoint, frameTimesMs);
            Tick(engine, 6, frameTimesMs);

            float healthAfter = ReadHealth(engine.World, "EnemySkirmisherA");
            Assert.That(
                healthAfter,
                Is.LessThan(healthBefore),
                $"{BuildInputActionDiagnostics(engine, "RuneBurst")} || {BuildAbilityDiagnostics(engine, "Arcweaver")}");
        }

        [Test]
        public void InteractionShowcase_ArcweaverRuneBurst_AfterBlinkDash_DirectVectorCast_DamagesTarget()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            var backend = GetInputBackend(engine);

            LoadMap(engine, HubMapId, frameTimesMs);
            SelectNamedEntity(engine, backend, "Arcweaver", frameTimesMs);
            AssertSelectedEntity(engine, "Arcweaver");
            PressButton(engine, backend, "<Keyboard>/f2", frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(LolModeId));

            ExecuteArcweaverBlinkDashPrelude(engine, backend, frameTimesMs);

            float healthBefore = ReadHealth(engine.World, "EnemySkirmisherA");
            Vector2 origin = ReadPosition(engine.World, "Arcweaver");
            Vector2 endpoint = ReadPosition(engine.World, "EnemySkirmisherA");

            Entity actor = FindEntityByName(engine.World, "Arcweaver");
            CastAbilityWithVector(engine, actor, slot: 7, origin, endpoint);
            Tick(engine, 6, frameTimesMs);

            float healthAfter = ReadHealth(engine.World, "EnemySkirmisherA");
            Assert.That(
                healthAfter,
                Is.LessThan(healthBefore),
                BuildAbilityDiagnostics(engine, "Arcweaver"));
        }

        [Test]
        public void InteractionShowcase_ArcweaverRuneBurst_AfterBlinkDash_ChordInput_DamagesTarget()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            var backend = GetInputBackend(engine);

            LoadMap(engine, HubMapId, frameTimesMs);
            SelectNamedEntity(engine, backend, "Arcweaver", frameTimesMs);
            AssertSelectedEntity(engine, "Arcweaver");
            PressButton(engine, backend, "<Keyboard>/f2", frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(LolModeId));

            ExecuteArcweaverBlinkDashPrelude(engine, backend, frameTimesMs);

            float healthBefore = ReadHealth(engine.World, "EnemySkirmisherA");
            Vector2 origin = ReadPosition(engine.World, "Arcweaver");
            Vector2 endpoint = ReadPosition(engine.World, "EnemySkirmisherA");

            PressChord(engine, backend, "<Keyboard>/x", "<Keyboard>/c", frameTimesMs);
            LeftClickWorld(engine, backend, origin, frameTimesMs);
            LeftClickWorld(engine, backend, endpoint, frameTimesMs);
            Tick(engine, 6, frameTimesMs);

            float healthAfter = ReadHealth(engine.World, "EnemySkirmisherA");
            Assert.That(
                healthAfter,
                Is.LessThan(healthBefore),
                $"{BuildInputActionDiagnostics(engine, "RuneBurst")} || {BuildAbilityDiagnostics(engine, "Arcweaver")}");
        }

        [Test]
        public void InteractionShowcase_SingleClickReselect_AfterRtsPrelude_CollapsesSelectionToPrimary()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            var backend = GetInputBackend(engine);

            LoadMap(engine, HubMapId, frameTimesMs);
            SelectNamedEntity(engine, backend, "Arcweaver", frameTimesMs);
            AssertSelectedEntity(engine, "Arcweaver");
            DragSelectNamed(engine, backend, frameTimesMs, "Arcweaver", "Vanguard", "Commander");
            AssertSelectionCount(engine, 3);

            RightClickWorld(engine, backend, new Vector2(1500f, 900f), frameTimesMs);
            HoldModifier(engine, backend, "<Keyboard>/leftShift", true, frameTimesMs);
            RightClickWorld(engine, backend, new Vector2(1650f, 900f), frameTimesMs);
            HoldModifier(engine, backend, "<Keyboard>/leftShift", false, frameTimesMs);
            Tick(engine, 2, frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/s", frameTimesMs);
            Tick(engine, 12, frameTimesMs);

            SelectNamedEntity(engine, backend, "Arcweaver", frameTimesMs);
            Assert.That(
                GetSelectionCount(engine),
                Is.EqualTo(1),
                BuildSelectionStateDiagnostics(engine));
            Assert.That(GetSelectedNames(engine), Is.EquivalentTo(new[] { "Arcweaver" }));
        }

        [Test]
        public void InteractionShowcase_ArcweaverRuneBurst_AfterQBlinkGuardDash_ChordInput_DamagesTarget()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            var backend = GetInputBackend(engine);

            LoadMap(engine, HubMapId, frameTimesMs);
            SelectNamedEntity(engine, backend, "Arcweaver", frameTimesMs);
            AssertSelectedEntity(engine, "Arcweaver");
            PressButton(engine, backend, "<Keyboard>/f2", frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(LolModeId));

            SetMouseWorld(engine, backend, GetEntityScreen(engine, "EnemySkirmisherA"), frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/q", frameTimesMs);
            Tick(engine, 4, frameTimesMs);

            Vector2 beforeBlink = ReadPosition(engine.World, "Arcweaver");
            SetMouseWorld(engine, backend, beforeBlink + new Vector2(400f, 0f), frameTimesMs);
            backend.SetButton("<Keyboard>/w", true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton("<Keyboard>/w", false);
            Tick(engine, 12, frameTimesMs);

            PressButton(engine, backend, "<Keyboard>/f", frameTimesMs);
            Tick(engine, 14, frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/f", frameTimesMs);
            Tick(engine, 2, frameTimesMs);

            Vector2 beforeDash = ReadPosition(engine.World, "Arcweaver");
            SetMouseWorld(engine, backend, beforeDash + new Vector2(260f, 0f), frameTimesMs);
            DoubleTapKey(engine, backend, "<Keyboard>/z", frameTimesMs);
            Tick(engine, 12, frameTimesMs);

            float healthBefore = ReadHealth(engine.World, "EnemySkirmisherA");
            Vector2 origin = ReadPosition(engine.World, "Arcweaver");
            Vector2 endpoint = ReadPosition(engine.World, "EnemySkirmisherA");

            PressChord(engine, backend, "<Keyboard>/x", "<Keyboard>/c", frameTimesMs);
            LeftClickWorld(engine, backend, origin, frameTimesMs);
            LeftClickWorld(engine, backend, endpoint, frameTimesMs);
            Tick(engine, 6, frameTimesMs);

            float healthAfter = ReadHealth(engine.World, "EnemySkirmisherA");
            Assert.That(
                healthAfter,
                Is.LessThan(healthBefore),
                $"{BuildInputActionDiagnostics(engine, "RuneBurst")} || {BuildAbilityDiagnostics(engine, "Arcweaver")} || {BuildRuneBurstResolverDiagnostics(engine, FindEntityByName(engine.World, "Arcweaver"), origin, endpoint)} || selected={string.Join(",", GetSelectedNames(engine))}");
        }

        [Test]
        public void InteractionShowcase_ArcweaverRuneBurst_AfterRtsPrelude_ChordInput_DamagesTarget()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            var backend = GetInputBackend(engine);

            LoadMap(engine, HubMapId, frameTimesMs);
            SelectNamedEntity(engine, backend, "Arcweaver", frameTimesMs);
            AssertSelectedEntity(engine, "Arcweaver");
            PressButton(engine, backend, "<Keyboard>/f2", frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(LolModeId));

            DragSelectNamed(engine, backend, frameTimesMs, "Arcweaver", "Vanguard", "Commander");
            AssertSelectionCount(engine, 3);

            RightClickWorld(engine, backend, new Vector2(1500f, 900f), frameTimesMs);
            HoldModifier(engine, backend, "<Keyboard>/leftShift", true, frameTimesMs);
            RightClickWorld(engine, backend, new Vector2(1650f, 900f), frameTimesMs);
            HoldModifier(engine, backend, "<Keyboard>/leftShift", false, frameTimesMs);
            Tick(engine, 2, frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/s", frameTimesMs);
            Tick(engine, 12, frameTimesMs);

            SelectNamedEntity(engine, backend, "Arcweaver", frameTimesMs);
            Assert.That(GetSelectionCount(engine), Is.EqualTo(1), $"selected={string.Join(",", GetSelectedNames(engine))}");

            float healthBefore = ReadHealth(engine.World, "EnemySkirmisherA");
            Vector2 origin = ReadPosition(engine.World, "Arcweaver");
            Vector2 endpoint = ReadPosition(engine.World, "EnemySkirmisherA");

            PressChord(engine, backend, "<Keyboard>/x", "<Keyboard>/c", frameTimesMs);
            LeftClickWorld(engine, backend, origin, frameTimesMs);
            LeftClickWorld(engine, backend, endpoint, frameTimesMs);
            Tick(engine, 6, frameTimesMs);

            float healthAfter = ReadHealth(engine.World, "EnemySkirmisherA");
            Assert.That(
                healthAfter,
                Is.LessThan(healthBefore),
                $"{BuildInputActionDiagnostics(engine, "RuneBurst")} || {BuildAbilityDiagnostics(engine, "Arcweaver")} || selected={string.Join(",", GetSelectedNames(engine))}");
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

            var view = new StubViewController(1920f, 1080f);
            engine.SetService(CoreServiceKeys.ViewController, view);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, new WorldMappedScreenRayProvider());
            engine.SetService(CoreServiceKeys.ScreenProjector, new WorldMappedScreenProjector());

            var culling = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, view);
            engine.RegisterPresentationSystem(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);

            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs, int frames = 5)
        {
            engine.LoadMap(mapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null, $"{mapId} should create a live map session.");
            Tick(engine, frames, frameTimesMs);
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
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.GlobalContext[TestInputBackendKey] = backend;
        }

        private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
        {
            for (int i = 0; i < frames; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                engine.Tick(DeltaTime);
                frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
            }
        }

        private static void TickUntil(GameEngine engine, List<double> frameTimesMs, Func<bool> predicate, int maxFrames = 60)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                Tick(engine, 1, frameTimesMs);
            }

            Assert.That(predicate(), Is.True, $"Predicate was not satisfied within {maxFrames} frames.");
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
        {
            return engine.GlobalContext[TestInputBackendKey] as TestInputBackend
                ?? throw new InvalidOperationException("Test input backend is missing.");
        }

        private static void SelectNamedEntity(GameEngine engine, TestInputBackend backend, string name, List<double> frameTimesMs)
        {
            LeftClickWorld(engine, backend, GetEntityScreen(engine, name), frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => string.Equals(GetSelectedEntityName(engine), name, StringComparison.Ordinal) &&
                      GetSelectionCount(engine) == 1,
                maxFrames: 12);
        }

        private static void AssertSelectedEntity(GameEngine engine, string expectedName)
        {
            string selectedName = GetSelectedEntityName(engine);
            Assert.That(selectedName, Is.EqualTo(expectedName));

            Entity[] selection = GetSelectionSnapshot(engine);
            Assert.That(selection.Length, Is.EqualTo(1), BuildSelectionStateDiagnostics(engine));

            Entity primary = selection[0];
            Assert.That(primary, Is.Not.EqualTo(Entity.Null));
            Assert.That(engine.World.TryGet(primary, out Name primaryName), Is.True);
            Assert.That(primaryName.Value, Is.EqualTo(expectedName));
        }

        private static void PressButton(GameEngine engine, TestInputBackend backend, string path, List<double> frameTimesMs)
        {
            backend.SetButton(path, true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton(path, false);
            Tick(engine, 2, frameTimesMs);
        }

        private static void HoldButton(GameEngine engine, TestInputBackend backend, string path, int holdFrames, List<double> frameTimesMs)
        {
            backend.SetButton(path, true);
            Tick(engine, Math.Max(1, holdFrames), frameTimesMs);
        }

        private static void ReleaseButton(GameEngine engine, TestInputBackend backend, string path, List<double> frameTimesMs)
        {
            backend.SetButton(path, false);
            Tick(engine, 2, frameTimesMs);
        }

        private static void PressChord(GameEngine engine, TestInputBackend backend, string primaryPath, string secondaryPath, List<double> frameTimesMs)
        {
            backend.SetButton(primaryPath, true);
            backend.SetButton(secondaryPath, true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton(primaryPath, false);
            backend.SetButton(secondaryPath, false);
            Tick(engine, 2, frameTimesMs);
        }

        private static void DoubleTapKey(GameEngine engine, TestInputBackend backend, string path, List<double> frameTimesMs)
        {
            backend.SetButton(path, true);
            Tick(engine, 1, frameTimesMs);
            backend.SetButton(path, false);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton(path, true);
            Tick(engine, 1, frameTimesMs);
            backend.SetButton(path, false);
            Tick(engine, 2, frameTimesMs);
        }

        private static void ExecuteArcweaverBlinkDashPrelude(GameEngine engine, TestInputBackend backend, List<double> frameTimesMs)
        {
            Vector2 beforeBlink = ReadPosition(engine.World, "Arcweaver");
            SetMouseWorld(engine, backend, beforeBlink + new Vector2(400f, 0f), frameTimesMs);
            backend.SetButton("<Keyboard>/w", true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton("<Keyboard>/w", false);
            Tick(engine, 12, frameTimesMs);

            Vector2 beforeDash = ReadPosition(engine.World, "Arcweaver");
            SetMouseWorld(engine, backend, beforeDash + new Vector2(260f, 0f), frameTimesMs);
            DoubleTapKey(engine, backend, "<Keyboard>/z", frameTimesMs);
            Tick(engine, 12, frameTimesMs);
        }

        private static void LeftClickWorld(GameEngine engine, TestInputBackend backend, Vector2 screenPosition, List<double> frameTimesMs)
        {
            SetMouseWorld(engine, backend, screenPosition, frameTimesMs);
            backend.SetButton("<Mouse>/LeftButton", true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton("<Mouse>/LeftButton", false);
            Tick(engine, 2, frameTimesMs);
        }

        private static void RightClickWorld(GameEngine engine, TestInputBackend backend, Vector2 screenPosition, List<double> frameTimesMs)
        {
            SetMouseWorld(engine, backend, screenPosition, frameTimesMs);
            backend.SetButton("<Mouse>/RightButton", true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton("<Mouse>/RightButton", false);
            Tick(engine, 2, frameTimesMs);
        }

        private static void SetMouseWorld(GameEngine engine, TestInputBackend backend, Vector2 screenPosition, List<double> frameTimesMs)
        {
            backend.SetMousePosition(screenPosition);
            Tick(engine, 1, frameTimesMs);
        }

        private static void HoldModifier(GameEngine engine, TestInputBackend backend, string path, bool isDown, List<double> frameTimesMs)
        {
            backend.SetButton(path, isDown);
            Tick(engine, 1, frameTimesMs);
        }

        private static void DragSelectNamed(GameEngine engine, TestInputBackend backend, List<double> frameTimesMs, params string[] names)
        {
            Assert.That(names, Is.Not.Null.And.Not.Empty);

            var points = names.Select(name => GetEntityScreen(engine, name)).ToArray();
            float minX = points.Min(p => p.X) - 40f;
            float minY = points.Min(p => p.Y) - 40f;
            float maxX = points.Max(p => p.X) + 40f;
            float maxY = points.Max(p => p.Y) + 40f;

            backend.SetMousePosition(new Vector2(minX, minY));
            Tick(engine, 1, frameTimesMs);
            backend.SetButton("<Mouse>/LeftButton", true);
            Tick(engine, 2, frameTimesMs);
            backend.SetMousePosition(new Vector2(maxX, maxY));
            Tick(engine, 2, frameTimesMs);
            backend.SetButton("<Mouse>/LeftButton", false);
            Tick(engine, 2, frameTimesMs);

            TickUntil(engine, frameTimesMs, () => GetSelectionCount(engine) == names.Length, maxFrames: 12);
        }

        private static Entity FindEntityByName(World world, string name)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });
            return result;
        }

        private static Vector2 ReadPosition(World world, string name)
        {
            Entity entity = FindEntityByName(world, name);
            Assert.That(entity, Is.Not.EqualTo(Entity.Null), $"Entity '{name}' was not found.");
            Assert.That(world.TryGet(entity, out WorldPositionCm position), Is.True);
            var worldCm = position.ToWorldCmInt2();
            return new Vector2(worldCm.X, worldCm.Y);
        }

        private static float ReadHealth(World world, string name)
        {
            Entity entity = FindEntityByName(world, name);
            Assert.That(entity, Is.Not.EqualTo(Entity.Null), $"Entity '{name}' was not found.");
            return ReadHealth(world, entity);
        }

        private static float ReadHealth(World world, Entity entity)
        {
            int healthId = AttributeRegistry.GetId("Health");
            if (healthId < 0 || !world.TryGet(entity, out AttributeBuffer attributes))
            {
                return 0f;
            }

            return attributes.GetCurrent(healthId);
        }

        private static bool HasTag(World world, string name, int tagId)
        {
            Entity entity = FindEntityByName(world, name);
            return HasTag(world, entity, tagId);
        }

        private static bool HasTag(World world, Entity entity, int tagId)
        {
            if (entity == Entity.Null || !world.IsAlive(entity) || tagId <= 0 || !world.TryGet(entity, out GameplayTagContainer tags))
            {
                return false;
            }

            return tags.HasTag(tagId);
        }

        private static bool HasAnyTag(World world, string name, params string[] tagNames)
        {
            Entity entity = FindEntityByName(world, name);
            if (entity == Entity.Null || !world.IsAlive(entity) || !world.TryGet(entity, out GameplayTagContainer tags))
            {
                return false;
            }

            for (int i = 0; i < tagNames.Length; i++)
            {
                int tagId = TagRegistry.GetId(tagNames[i]);
                if (tagId > 0 && tags.HasTag(tagId))
                {
                    return true;
                }
            }

            return false;
        }

        private static ushort GetTagCount(World world, Entity entity, int tagId)
        {
            if (entity == Entity.Null || !world.IsAlive(entity) || tagId <= 0 || !world.TryGet(entity, out TagCountContainer counts))
            {
                return 0;
            }

            return counts.GetCount(tagId);
        }

        private static string BuildTagStateDiagnostics(GameEngine engine, Entity entity, params int[] tagIds)
        {
            var sb = new StringBuilder();
            var clock = engine.GetService(CoreServiceKeys.Clock);
            sb.Append("TagState");
            if (clock != null)
            {
                sb.Append(" | fixed=");
                sb.Append(clock.Now(ClockDomainId.FixedFrame));
                sb.Append(",step=");
                sb.Append(clock.Now(ClockDomainId.Step));
            }
            for (int i = 0; i < tagIds.Length; i++)
            {
                int tagId = tagIds[i];
                sb.Append(" | ");
                sb.Append(tagId);
                sb.Append(":bit=");
                sb.Append(HasTag(engine.World, entity, tagId));
                sb.Append(",count=");
                sb.Append(GetTagCount(engine.World, entity, tagId));
            }

            if (entity != Entity.Null && engine.World.IsAlive(entity) && engine.World.TryGet(entity, out TimedTagBuffer timed))
            {
                sb.Append(" | timed=[");
                for (int i = 0; i < timed.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(';');
                    }

                    int timedTagId;
                    int expireAt;
                    GasClockId clockId;
                    unsafe
                    {
                        timedTagId = timed.TagIds[i];
                        expireAt = timed.ExpireAt[i];
                        clockId = (GasClockId)timed.ClockIds[i];
                    }

                    sb.Append(timedTagId);
                    sb.Append('@');
                    sb.Append(expireAt);
                    sb.Append('/');
                    sb.Append(clockId);
                }
                sb.Append(']');
            }

            return sb.ToString();
        }

        private static string FindNearestEnemy(World world, string actorName)
        {
            Entity actor = FindEntityByName(world, actorName);
            Assert.That(actor, Is.Not.EqualTo(Entity.Null), $"Entity '{actorName}' was not found.");
            Assert.That(world.TryGet(actor, out Team actorTeam), Is.True);
            Vector2 actorPosition = ReadPosition(world, actorName);

            string bestName = string.Empty;
            float bestDistanceSq = float.MaxValue;
            var query = new QueryDescription().WithAll<Name, Team, WorldPositionCm>();
            world.Query(in query, (Entity entity, ref Name name, ref Team team, ref WorldPositionCm position) =>
            {
                if (entity == actor || team.Id == actorTeam.Id)
                {
                    return;
                }

                var pos = position.ToWorldCmInt2();
                float distanceSq = Vector2.DistanceSquared(actorPosition, new Vector2(pos.X, pos.Y));
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    bestName = name.Value;
                }
            });

            Assert.That(bestName, Is.Not.Empty, $"No hostile target found for '{actorName}'.");
            return bestName;
        }

        private static void AssertSelectionCount(GameEngine engine, int expectedCount)
        {
            Assert.That(GetSelectionCount(engine), Is.EqualTo(expectedCount));
        }

        private static int GetSelectionCount(GameEngine engine)
        {
            return GetSelectionSnapshot(engine).Length;
        }

        private static void AssertQueuedMoveOrders(GameEngine engine, int minimumQueuedOrders)
        {
            Entity[] selection = GetSelectionSnapshot(engine);
            var config = engine.GetService(CoreServiceKeys.GameConfig);
            int moveOrderTypeId = config.Constants.OrderTypeIds["moveTo"];
            int queuedMoveOrders = 0;

            for (int i = 0; i < selection.Length; i++)
            {
                Entity entity = selection[i];
                if (!engine.World.IsAlive(entity) || !engine.World.Has<OrderBuffer>(entity))
                {
                    continue;
                }

                ref var buffer = ref engine.World.Get<OrderBuffer>(entity);
                for (int q = 0; q < buffer.QueuedCount; q++)
                {
                    var queued = buffer.GetQueued(q).Order;
                    if (queued.OrderTypeId == moveOrderTypeId)
                    {
                        queuedMoveOrders++;
                    }
                }
            }

            Assert.That(queuedMoveOrders, Is.GreaterThanOrEqualTo(minimumQueuedOrders),
                $"Expected at least {minimumQueuedOrders} queued move orders across the current selection.");
        }

        private static void AssertMoveRuntimeActivated(GameEngine engine, params string[] actorNames)
        {
            int moveOrderTypeId = engine.GetService(CoreServiceKeys.GameConfig).Constants.OrderTypeIds["moveTo"];
            var diagnostics = new List<string>(actorNames.Length);
            int activatedCount = 0;

            for (int i = 0; i < actorNames.Length; i++)
            {
                Entity entity = FindEntityByName(engine.World, actorNames[i]);
                Assert.That(entity, Is.Not.EqualTo(Entity.Null), $"Entity '{actorNames[i]}' was not found.");

                bool hasOrderBuffer = engine.World.TryGet(entity, out OrderBuffer buffer);
                bool hasActiveMoveOrder = hasOrderBuffer &&
                                          buffer.HasActive &&
                                          buffer.ActiveOrder.Order.OrderTypeId == moveOrderTypeId;

                bool hasQueuedMoveOrder = false;
                if (hasOrderBuffer)
                {
                    for (int q = 0; q < buffer.QueuedCount; q++)
                    {
                        var queued = buffer.GetQueued(q).Order;
                        if (queued.OrderTypeId == moveOrderTypeId)
                        {
                            hasQueuedMoveOrder = true;
                            break;
                        }
                    }
                }

                Vector3 targetPos = default;
                bool hasTargetPos = engine.World.TryGet(entity, out BlackboardSpatialBuffer spatial) &&
                                    spatial.TryGetPoint(OrderBlackboardKeys.Generic_TargetPosition, out targetPos);

                if (hasActiveMoveOrder && hasTargetPos)
                {
                    activatedCount++;
                }

                diagnostics.Add(
                    $"{actorNames[i]}: activeMove={hasActiveMoveOrder}, queuedMove={hasQueuedMoveOrder}, " +
                    $"hasTargetPos={hasTargetPos}, " +
                    $"target=({(hasTargetPos ? targetPos.X : 0f):0.##},{(hasTargetPos ? targetPos.Z : 0f):0.##})");
            }

            Assert.That(
                activatedCount,
                Is.GreaterThanOrEqualTo(1),
                "Expected at least one selected hero to have an active moveTo runtime after RMB move. " +
                string.Join(" | ", diagnostics));
        }

        private static string GetActiveModeId(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(ViewModeManager.ActiveModeIdKey, out var modeIdObj) &&
                modeIdObj is string modeId)
            {
                return modeId;
            }

            if (engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) &&
                managerObj is ViewModeManager manager)
            {
                return manager.ActiveMode?.Id ?? string.Empty;
            }

            return string.Empty;
        }

        private static string BuildAbilityDiagnostics(GameEngine engine, string actorName)
        {
            Entity actor = FindEntityByName(engine.World, actorName);
            if (actor == Entity.Null || !engine.World.IsAlive(actor))
            {
                return $"Actor '{actorName}' was not found.";
            }

            var details = new List<string>();
            if (engine.World.TryGet(actor, out OrderBuffer orders))
            {
                details.Add(
                    orders.HasActive
                        ? $"activeOrder={orders.ActiveOrder.Order.OrderTypeId}"
                        : "activeOrder=<none>");
                details.Add(
                    orders.HasPending
                        ? $"pendingOrder={orders.PendingOrder.Order.OrderTypeId}"
                        : "pendingOrder=<none>");

                if (orders.HasQueued)
                {
                    details.Add($"queuedOrders={orders.QueuedCount}");
                }
            }
            else
            {
                details.Add("orderBuffer=<missing>");
            }

            if (engine.World.TryGet(actor, out BlackboardIntBuffer ints) &&
                ints.TryGet(OrderBlackboardKeys.Cast_SlotIndex, out int slotIndex))
            {
                details.Add($"castSlot={slotIndex}");
            }

            if (engine.World.TryGet(actor, out BlackboardSpatialBuffer spatial))
            {
                int pointCount = spatial.GetPointCount(OrderBlackboardKeys.Cast_TargetPosition);
                details.Add($"castPoints={pointCount}");
                if (pointCount > 0 && spatial.TryGetPointAt(OrderBlackboardKeys.Cast_TargetPosition, pointCount - 1, out var point))
                {
                    details.Add($"castTarget=({point.X:0.##},{point.Z:0.##})");
                }
            }

            if (engine.World.TryGet(actor, out AbilityExecInstance exec))
            {
                details.Add($"execSlot={exec.AbilitySlot}");
                details.Add($"execState={exec.State}");
                details.Add($"execHasTargetPos={exec.HasTargetPos != 0}");
                if (exec.HasTargetPos != 0)
                {
                    details.Add($"execTarget=({exec.TargetPosCm.X.ToFloat():0.##},{exec.TargetPosCm.Y.ToFloat():0.##})");
                }
            }
            else
            {
                details.Add("exec=<none>");
            }

            int displacementCount = 0;
            bool actorDisplacementFound = false;
            var displacementQuery = new QueryDescription().WithAll<DisplacementState>();
            engine.World.Query(in displacementQuery, (Entity _, ref DisplacementState state) =>
            {
                displacementCount++;
                if (state.TargetEntity == actor)
                {
                    actorDisplacementFound = true;
                }
            });

            details.Add($"displacements={displacementCount}");
            details.Add($"actorDisplacement={actorDisplacementFound}");
            details.Add($"activeMode={GetActiveModeId(engine)}");
            return string.Join(" | ", details);
        }

        private static string BuildInputActionDiagnostics(GameEngine engine, string actionId)
        {
            var details = new List<string>();

            if (engine.GetService(CoreServiceKeys.InputHandler) is PlayerInputHandler liveInput)
            {
                details.Add($"livePressed={liveInput.PressedThisFrame(actionId)}");
                details.Add($"liveDown={liveInput.IsDown(actionId)}");
                Vector2 pointer = liveInput.ReadAction<Vector2>("PointerPos");
                details.Add($"livePointer=({pointer.X:0.##},{pointer.Y:0.##})");
            }
            else
            {
                details.Add("liveInput=<missing>");
            }

            if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader authoritativeInput)
            {
                details.Add($"authPressed={authoritativeInput.PressedThisFrame(actionId)}");
                details.Add($"authDown={authoritativeInput.IsDown(actionId)}");
                Vector2 pointer = authoritativeInput.ReadAction<Vector2>("PointerPos");
                details.Add($"authPointer=({pointer.X:0.##},{pointer.Y:0.##})");
            }
            else
            {
                details.Add("authoritativeInput=<missing>");
            }

            if (engine.GetService(CoreServiceKeys.ActiveInputOrderMapping) is InputOrderMappingSystem mapping)
            {
                details.Add($"mappingMode={mapping.InteractionMode}");
                details.Add($"mappingAiming={mapping.IsAiming}");
                if (mapping.GetMapping(actionId) is InputOrderMapping actionMapping)
                {
                    details.Add($"selectionType={actionMapping.SelectionType}");
                    details.Add($"orderTypeKey={actionMapping.OrderTypeKey}");
                }
                else
                {
                    details.Add("mapping=<missing>");
                }
            }
            else
            {
                details.Add("activeMapping=<missing>");
            }

            bool uiCaptured = engine.GlobalContext.TryGetValue(CoreServiceKeys.UiCaptured.Name, out var uiCapturedObj) &&
                              uiCapturedObj is bool captured &&
                              captured;
            details.Add($"uiCaptured={uiCaptured}");
            details.Add($"activeMode={GetActiveModeId(engine)}");
            return string.Join(" | ", details);
        }

        private static string BuildSelectionStateDiagnostics(GameEngine engine)
        {
            var details = new List<string>();
            details.Add($"selectionCount={GetSelectionCount(engine)}");
            details.Add($"selected={string.Join(",", GetSelectedNames(engine))}");
            details.Add($"primary={GetSelectedEntityName(engine)}");

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.HoveredEntity.Name, out var hoveredObj) &&
                hoveredObj is Entity hovered &&
                engine.World.IsAlive(hovered) &&
                engine.World.TryGet(hovered, out Name hoveredName))
            {
                details.Add($"hovered={hoveredName.Value}");
            }
            else
            {
                details.Add("hovered=<none>");
            }

            Entity localPlayer = GetLocalPlayer(engine);
            if (engine.World.Has<SelectionDragState>(localPlayer))
            {
                ref var drag = ref engine.World.Get<SelectionDragState>(localPlayer);
                details.Add($"dragActive={drag.Active}");
                details.Add($"dragStart=({drag.StartScreen.X:0.##},{drag.StartScreen.Y:0.##})");
                details.Add($"dragCurrent=({drag.CurrentScreen.X:0.##},{drag.CurrentScreen.Y:0.##})");
            }

            if (engine.GetService(CoreServiceKeys.ActiveInputOrderMapping) is InputOrderMappingSystem mapping)
            {
                details.Add($"mappingAiming={mapping.IsAiming}");
            }

            return string.Join(" | ", details);
        }

        private static string BuildRuneBurstResolverDiagnostics(GameEngine engine, Entity actor, Vector2 originWorldCm, Vector2 endpointWorldCm)
        {
            if (actor == Entity.Null || !engine.World.IsAlive(actor))
            {
                return "resolver=actor-missing";
            }

            int headingDeg = ComputeHeadingDeg(originWorldCm, endpointWorldCm);
            var details = new List<string>
            {
                $"origin=({originWorldCm.X:0.##},{originWorldCm.Y:0.##})",
                $"endpoint=({endpointWorldCm.X:0.##},{endpointWorldCm.Y:0.##})",
                $"distance={Vector2.Distance(originWorldCm, endpointWorldCm):0.##}",
                $"heading={headingDeg}"
            };

            var lineBuffer = new Entity[64];
            var lineResult = engine.SpatialQueries.QueryLine(
                new WorldCmInt2((int)originWorldCm.X, (int)originWorldCm.Y),
                headingDeg,
                lengthCm: 780,
                halfWidthCm: 95,
                lineBuffer);
            details.Add($"lineCount={lineResult.Count}");
            details.Add($"lineHits={string.Join(",", ReadEntityNames(engine.World, lineBuffer, lineResult.Count))}");

            int runeBurstTemplateId = EffectTemplateIdRegistry.GetId("Effect.Interaction.RuneBurst");
            var templates = engine.GetService(CoreServiceKeys.EffectTemplateRegistry);
            if (runeBurstTemplateId <= 0 || templates == null || !templates.TryGetRef(runeBurstTemplateId, out int templateIndex))
            {
                details.Add("resolver=<template-missing>");
                return string.Join(" | ", details);
            }

            bool hadExec = engine.World.TryGet(actor, out AbilityExecInstance previousExec);
            var probeExec = new AbilityExecInstance
            {
                TargetOriginPosCm = Fix64Vec2.FromFloat(originWorldCm.X, originWorldCm.Y),
                HasTargetOriginPos = 1,
                TargetPosCm = Fix64Vec2.FromFloat(endpointWorldCm.X, endpointWorldCm.Y),
                HasTargetPos = 1
            };

            if (hadExec)
            {
                engine.World.Set(actor, probeExec);
            }
            else
            {
                engine.World.Add(actor, probeExec);
            }

            try
            {
                var resolverBuffer = new Entity[64];
                var ctx = new EffectContext
                {
                    RootId = 1,
                    Source = actor,
                    Target = Entity.Null,
                    TargetContext = Entity.Null
                };

                ref readonly var template = ref templates.GetRef(templateIndex);
                int resolverCount = TargetResolverFanOutHelper.ResolveTargets(
                    engine.World,
                    in ctx,
                    in template.TargetQuery,
                    engine.SpatialQueries,
                    resolverBuffer);

                details.Add($"resolverCount={resolverCount}");
                details.Add($"resolverHits={string.Join(",", ReadEntityNames(engine.World, resolverBuffer, resolverCount))}");
            }
            finally
            {
                if (hadExec)
                {
                    engine.World.Set(actor, previousExec);
                }
                else if (engine.World.Has<AbilityExecInstance>(actor))
                {
                    engine.World.Remove<AbilityExecInstance>(actor);
                }
            }

            return string.Join(" | ", details);
        }

        private static Vector2 GetEntityScreen(GameEngine engine, string name)
        {
            Entity entity = FindEntityByName(engine.World, name);
            Assert.That(entity, Is.Not.EqualTo(Entity.Null), $"Entity '{name}' was not found.");

            var projector = engine.GetService(CoreServiceKeys.ScreenProjector)
                ?? throw new InvalidOperationException("ScreenProjector was not installed.");
            ref var position = ref engine.World.Get<WorldPositionCm>(entity);
            return projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(position.Value, yMeters: 0f));
        }

        private static List<string> ExtractUiText(UIRoot root)
        {
            if (root.Scene?.Root == null)
            {
                return new List<string>();
            }

            var lines = new List<string>();
            CollectUiText(root.Scene.Root, lines);
            return lines;
        }

        private static void CollectUiText(UiNode node, List<string> lines)
        {
            if (!string.IsNullOrWhiteSpace(node.TextContent))
            {
                lines.Add(node.TextContent.Trim());
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                CollectUiText(node.Children[i], lines);
            }
        }

        private static int CountOverlays(GroundOverlayBuffer overlays, GroundOverlayShape shape)
        {
            int count = 0;
            foreach (ref readonly var item in overlays.GetSpan())
            {
                if (item.Shape == shape)
                {
                    count++;
                }
            }

            return count;
        }

        private static string BuildOverlayDiagnostics(GroundOverlayBuffer overlays)
        {
            int circles = 0;
            int cones = 0;
            int lines = 0;
            int rings = 0;
            foreach (ref readonly var item in overlays.GetSpan())
            {
                switch (item.Shape)
                {
                    case GroundOverlayShape.Circle:
                        circles++;
                        break;
                    case GroundOverlayShape.Cone:
                        cones++;
                        break;
                    case GroundOverlayShape.Line:
                        lines++;
                        break;
                    case GroundOverlayShape.Ring:
                        rings++;
                        break;
                }
            }

            return $"overlays=count:{overlays.Count},circle:{circles},cone:{cones},line:{lines},ring:{rings}";
        }

        private static StressState ReadStressState(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(StressTelemetryKey, out var telemetry) || telemetry == null)
            {
                return new StressState(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0f, 0f);
            }

            return new StressState(
                ReadProperty<bool>(telemetry, "IsActive"),
                ReadProperty<int>(telemetry, "DesiredPerSide"),
                ReadProperty<int>(telemetry, "RequestedRed"),
                ReadProperty<int>(telemetry, "RequestedBlue"),
                ReadProperty<int>(telemetry, "LiveRed"),
                ReadProperty<int>(telemetry, "LiveBlue"),
                ReadProperty<int>(telemetry, "ProjectileCount"),
                ReadProperty<int>(telemetry, "PeakProjectileCount"),
                ReadProperty<int>(telemetry, "OrdersIssued"),
                ReadProperty<int>(telemetry, "WavesDispatched"),
                ReadProperty<int>(telemetry, "QueueDepth"),
                ReadProperty<float>(telemetry, "RedAnchorHealth"),
                ReadProperty<float>(telemetry, "BlueAnchorHealth"));
        }

        private static T ReadProperty<T>(object source, string propertyName)
        {
            var property = source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Property '{propertyName}' was not found on {source.GetType().FullName}.");
            object? value = property.GetValue(source);
            if (value is T typed)
            {
                return typed;
            }

            if (value == null)
            {
                return default!;
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }

        private static void CaptureSnapshot(
            GameEngine engine,
            UIRoot uiRoot,
            GroundOverlayBuffer overlays,
            List<AcceptanceSnapshot> snapshots,
            IReadOnlyList<double> frameTimesMs,
            string step)
        {
            var selectedNames = GetSelectedNames(engine);
            var overlayCounts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["circle"] = CountOverlays(overlays, GroundOverlayShape.Circle),
                ["cone"] = CountOverlays(overlays, GroundOverlayShape.Cone),
                ["line"] = CountOverlays(overlays, GroundOverlayShape.Line),
                ["ring"] = CountOverlays(overlays, GroundOverlayShape.Ring)
            };

            var entityStates = new List<EntityState>(TrackedNames.Length);
            for (int i = 0; i < TrackedNames.Length; i++)
            {
                entityStates.Add(BuildEntityState(engine, TrackedNames[i], selectedNames));
            }

            snapshots.Add(new AcceptanceSnapshot(
                Step: step,
                MapId: engine.CurrentMapSession?.MapId.Value ?? string.Empty,
                ActiveModeId: GetActiveModeId(engine),
                SelectionCount: selectedNames.Count,
                SelectedNames: selectedNames,
                UiText: ExtractUiText(uiRoot).Take(8).ToArray(),
                OverlayCounts: overlayCounts,
                Entities: entityStates,
                Stress: ReadStressState(engine),
                TickMs: frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d));
        }

        private static EntityState BuildEntityState(GameEngine engine, string name, IReadOnlyCollection<string> selectedNames)
        {
            Entity entity = FindEntityByName(engine.World, name);
            if (entity == Entity.Null || !engine.World.IsAlive(entity))
            {
                return new EntityState(name, false, 0f, 0f, 0f, false, Array.Empty<string>());
            }

            var position = ReadPosition(engine.World, name);
            float health = ReadHealth(engine.World, entity);
            var tags = new List<string>();
            for (int i = 0; i < TrackedTagNames.Length; i++)
            {
                int tagId = TagRegistry.GetId(TrackedTagNames[i]);
                if (tagId > 0 && HasTag(engine.World, entity, tagId))
                {
                    tags.Add(TrackedTagNames[i]);
                }
            }

            return new EntityState(
                Name: name,
                Alive: true,
                X: position.X,
                Y: position.Y,
                Health: health,
                Selected: selectedNames.Contains(name),
                Tags: tags);
        }

        private static string BuildTraceJsonl(IReadOnlyList<AcceptanceSnapshot> snapshots)
        {
            var lines = new List<string>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                lines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"interaction-showcase-{i + 1:000}",
                    step = snapshot.Step,
                    map_id = snapshot.MapId,
                    active_mode_id = snapshot.ActiveModeId,
                    selection_count = snapshot.SelectionCount,
                    selected_names = snapshot.SelectedNames,
                    ui_head = snapshot.UiText.Take(4).ToArray(),
                    overlay_counts = snapshot.OverlayCounts,
                    tracked_entities = snapshot.Entities,
                    stress = snapshot.Stress,
                    tick_ms = Math.Round(snapshot.TickMs, 4),
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
            double medianTickMs = Median(frameTimesMs);
            double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
            var finalSnapshot = snapshots[^1];
            var finalStress = finalSnapshot.Stress;

            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: interaction-showcase");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: exercise WoW target-first, LoL smart-cast, SC2 aim-cast, indicator-release, RTS multi-select orders, vector casting, toggle/double-tap/chord inputs, context-scored action routing, and high-density fireball pressure in one production-path showcase.");
            sb.AppendLine("- Gameplay domain: Ludots interaction architecture, GAS execution, input/order mapping, selection fan-out, view modes, HUD, overlays, and ECS stress throughput.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: none");
            sb.AppendLine("- Maps: `interaction_showcase_hub`, `interaction_showcase_stress`");
            sb.AppendLine($"- Mods: `{string.Join("`, `", AcceptanceMods)}`");
            sb.AppendLine("- Clock profile: fixed `1/60s`, headless `GameEngine.Tick()` loop.");
            sb.AppendLine("- Input source: real `InputConfigPipelineLoader` + `PlayerInputHandler` driven by a deterministic test backend.");
            sb.AppendLine("- View mapping: `WorldMappedScreenRayProvider` and `WorldMappedScreenProjector` to drive click/select/cast flows through the shared Core input pipeline.");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Boot the real engine with the interaction showcase mod and load the hub map.");
            sb.AppendLine("2. Select Arcweaver, switch reference modes, cast unit/point/toggle/double-tap/vector abilities, and validate health/tag/position deltas.");
            sb.AppendLine("3. Drag-select the full hero squad, issue move, queued move, and stop through the shared local order source.");
            sb.AppendLine("4. Select Vanguard and validate indicator-release plus SC2 aim-cast cancel and confirm branches.");
            sb.AppendLine("5. Select Commander, switch to action mode, and validate context-scored skill resolution on `Space`.");
            sb.AppendLine("6. Load the stress map and wait until 3200 mirrored fireball casters saturate the battlefield.");
            sb.AppendLine();
            sb.AppendLine("## Expected Outcomes");
            sb.AppendLine("- Primary success condition: all interaction modes operate through the shared engine stack without bespoke test runtime paths.");
            sb.AppendLine("- Failure branch condition: aim-cast cancel must clear overlays and apply zero damage before the confirm branch is re-run.");
            sb.AppendLine("- Key metrics: selection count, active mode, tracked unit positions/health/tags, stress live counts, queue depth, and peak projectile count.");
            sb.AppendLine();
            sb.AppendLine("## Evidence Artifacts");
            sb.AppendLine("- `artifacts/acceptance/interaction-showcase/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/interaction-showcase/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/interaction-showcase/path.mmd`");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            for (int i = 0; i < timeline.Count; i++)
            {
                sb.AppendLine($"- {timeline[i]}");
            }

            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine("- verdict: the interaction showcase stayed on the production input/order/GAS/UI path from hero micro to multi-thousand-agent stress throughput.");
            sb.AppendLine($"- reason: final stress state reached red={finalStress.LiveRed}, blue={finalStress.LiveBlue}, waves={finalStress.WavesDispatched}, peak_projectiles={finalStress.PeakProjectileCount}, with active mode `{finalSnapshot.ActiveModeId}` before stress handoff.");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- snapshots captured: `{snapshots.Count}`");
            sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
            sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
            sb.AppendLine($"- final requested per side: `{finalStress.RequestedRed}` red / `{finalStress.RequestedBlue}` blue");
            sb.AppendLine($"- final live per side: `{finalStress.LiveRed}` red / `{finalStress.LiveBlue}` blue");
            sb.AppendLine($"- peak projectile count: `{finalStress.PeakProjectileCount}`");
            sb.AppendLine($"- final queue depth: `{finalStress.QueueDepth}`");
            sb.AppendLine("- reusable wiring: `ConfigPipeline`, `PlayerInputHandler`, `ViewModeManager`, `EntityClickSelectSystem`, `InputOrderMappingSystem`, `OrderBuffer`, `GroundOverlayBuffer`, `ReactivePage<TState>`");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[InputCollection: boot engine -> hub HUD mounted] --> B[Selection: Arcweaver selected -> LoL smart-cast Q damages target]",
                "    B --> C[Orders: drag-select trio -> RMB move -> Shift queue -> S stop]",
                "    C --> D[AbilityActivation: Arcweaver point blink/toggle/double-tap/vector succeed]",
                "    D --> E[Indicator: Vanguard hold R -> ring overlay visible]",
                "    E --> F{AimCast E confirm?}",
                "    F -- RMB cancel --> G[Guard branch: cancel aim -> overlay cleared -> damage unchanged]",
                "    F -- LMB confirm --> H[AbilityActivation: cleave cone hits target]",
                "    H --> I[ContextScored: Commander Space -> concrete cooldown tag set]",
                "    I --> J[Stress: load stress map -> enqueue mirrored caster waves]",
                "    J --> K[EffectProcessing: thousands of fireballs -> peak projectiles > 0]",
                "    G --> H"
            }) + Environment.NewLine;
        }

        private static IReadOnlyList<string> GetSelectedNames(GameEngine engine)
        {
            Entity[] selection = GetSelectionSnapshot(engine);
            var names = new List<string>(selection.Length);
            for (int i = 0; i < selection.Length; i++)
            {
                Entity entity = selection[i];
                if (!engine.World.IsAlive(entity))
                {
                    continue;
                }

                if (engine.World.TryGet(entity, out Name name))
                {
                    names.Add(name.Value);
                }
                else
                {
                    names.Add($"Entity#{entity.Id}");
                }
            }

            return names;
        }

        private static string GetSelectedEntityName(GameEngine engine)
        {
            if (SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected) &&
                engine.World.TryGet(selected, out Name name))
            {
                return name.Value;
            }

            return string.Empty;
        }

        private static Entity[] GetSelectionSnapshot(GameEngine engine)
        {
            return SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext);
        }

        private static Entity GetLocalPlayer(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
                   localObj is Entity local &&
                   engine.World.IsAlive(local)
                ? local
                : throw new InvalidOperationException("LocalPlayerEntity is missing.");
        }

        private static void CastAbilityAtWorldPoint(GameEngine engine, Entity actor, int slot, Vector2 targetWorldCm)
        {
            var orders = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
                ?? throw new InvalidOperationException("OrderQueue service is missing.");

            var args = new OrderArgs
            {
                I0 = slot,
                Spatial = new OrderSpatial
                {
                    Kind = OrderSpatialKind.WorldCm,
                    Mode = OrderCollectionMode.Single,
                    WorldCm = new Vector3(targetWorldCm.X, 0f, targetWorldCm.Y)
                }
            };

            bool enqueued = orders.TryEnqueue(new Order
            {
                OrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["castAbility"],
                PlayerId = 1,
                Actor = actor,
                Args = args,
                SubmitMode = OrderSubmitMode.Immediate
            });

            Assert.That(enqueued, Is.True, "Direct point cast order should enqueue.");
        }

        private static void CastAbilityWithVector(GameEngine engine, Entity actor, int slot, Vector2 originWorldCm, Vector2 endpointWorldCm)
        {
            var orders = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
                ?? throw new InvalidOperationException("OrderQueue service is missing.");

            var spatial = new OrderSpatial
            {
                Kind = OrderSpatialKind.WorldCm,
                Mode = OrderCollectionMode.List,
                WorldCm = new Vector3(originWorldCm.X, 0f, originWorldCm.Y)
            };
            spatial.AddPointWorldCm((int)originWorldCm.X, 0, (int)originWorldCm.Y);
            spatial.AddPointWorldCm((int)endpointWorldCm.X, 0, (int)endpointWorldCm.Y);

            bool enqueued = orders.TryEnqueue(new Order
            {
                OrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["castAbility"],
                PlayerId = 1,
                Actor = actor,
                Args = new OrderArgs
                {
                    I0 = slot,
                    Spatial = spatial
                },
                SubmitMode = OrderSubmitMode.Immediate
            });

            Assert.That(enqueued, Is.True, "Direct vector cast order should enqueue.");
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

        private static int ComputeHeadingDeg(Vector2 originWorldCm, Vector2 endpointWorldCm)
        {
            int dx = (int)(endpointWorldCm.X - originWorldCm.X);
            int dy = (int)(endpointWorldCm.Y - originWorldCm.Y);
            if (dx == 0 && dy == 0)
            {
                return 0;
            }

            int deg = (int)MathF.Round(MathF.Atan2(dy, dx) * 180f / MathF.PI);
            if (deg < 0)
            {
                deg += 360;
            }

            return deg;
        }

        private static IReadOnlyList<string> ReadEntityNames(World world, IReadOnlyList<Entity> entities, int count)
        {
            var names = new List<string>(Math.Min(count, entities.Count));
            for (int i = 0; i < count && i < entities.Count; i++)
            {
                Entity entity = entities[i];
                if (!world.IsAlive(entity))
                {
                    continue;
                }

                if (world.TryGet(entity, out Name name))
                {
                    names.Add(name.Value);
                }
                else
                {
                    names.Add($"Entity#{entity.Id}");
                }
            }

            return names;
        }

        private sealed record AcceptanceSnapshot(
            string Step,
            string MapId,
            string ActiveModeId,
            int SelectionCount,
            IReadOnlyList<string> SelectedNames,
            IReadOnlyList<string> UiText,
            IReadOnlyDictionary<string, int> OverlayCounts,
            IReadOnlyList<EntityState> Entities,
            StressState Stress,
            double TickMs);

        private sealed record EntityState(
            string Name,
            bool Alive,
            float X,
            float Y,
            float Health,
            bool Selected,
            IReadOnlyList<string> Tags);

        private sealed record StressState(
            bool IsActive,
            int DesiredPerSide,
            int RequestedRed,
            int RequestedBlue,
            int LiveRed,
            int LiveBlue,
            int ProjectileCount,
            int PeakProjectileCount,
            int OrdersIssued,
            int WavesDispatched,
            int QueueDepth,
            float RedAnchorHealth,
            float BlueAnchorHealth);

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
            private Vector2 _mousePosition;
            private float _mouseWheel;

            public void SetButton(string path, bool isDown)
            {
                _buttons[path] = isDown;
            }

            public void SetMousePosition(Vector2 position)
            {
                _mousePosition = position;
            }

            public void SetMouseWheel(float wheel)
            {
                _mouseWheel = wheel;
            }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out var isDown) && isDown;
            public Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => _mouseWheel;
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

        private sealed class WorldMappedScreenRayProvider : IScreenRayProvider
        {
            public ScreenRay GetRay(Vector2 screenPosition)
            {
                return new ScreenRay(
                    new Vector3(screenPosition.X / 100f, 10f, screenPosition.Y / 100f),
                    -Vector3.UnitY);
            }
        }

        private sealed class WorldMappedScreenProjector : IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 worldPosition)
            {
                return new Vector2(worldPosition.X * 100f, worldPosition.Z * 100f);
            }
        }
    }
}
