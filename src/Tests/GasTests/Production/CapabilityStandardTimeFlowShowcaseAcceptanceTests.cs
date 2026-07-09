using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using CapabilityStandardTimeFlowShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
public sealed class CapabilityStandardTimeFlowShowcaseAcceptanceTests
{
    private const string BindingName = "capability_standard_time_flow_showcase";
    private const string PresetId = "capability_standard_time_flow_showcase_raylib";
    private const string ShowcaseModId = "CapabilityStandardTimeFlowShowcaseMod";
    private const string MapId = "capability_standard_time_flow_showcase";
    private const string ShowcaseConfigPath = "CapabilityStandardTimeFlowShowcaseConfig.json";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        ShowcaseModId
    };

    [Test]
    public void RootMod_UsesFormalLauncherAndDoesNotAuthorFormationOrActionInput()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        string modDir = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            ShowcaseModId);

        AssertLauncherBinding(repoRoot);
        AssertLauncherPreset(repoRoot);
        Assert.That(File.Exists(Path.Combine(modDir, "mod.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, $"{ShowcaseModId}.csproj")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, "assets", "game.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modDir, "assets", "Maps", $"{MapId}.json")), Is.True);
        AssertShowcaseCatalog(modDir);
        AssertGameJson(modDir);
        AssertNoFormationOrActionAuthoring(modDir);

        using var engine = CreateEngine(repoRoot);
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        var runtime = RequireRuntime(engine);

        Assert.That(runtime.IsActive, Is.True);
        Assert.That(runtime.SuppressHostDiagnosticUi, Is.False,
            "TimeFlow showcase owns a visible token-stack panel, so it must not suppress the Raylib Skia UI layer.");
        CapabilityStandardTimeFlowShowcasePanelState state = runtime.CapturePanelState(engine);
        Assert.That(state.SimulationScalePermille, Is.EqualTo(TimeFlowService.DefaultScalePermille));
        Assert.That(state.GasPolicyScalePermille, Is.EqualTo(TimeFlowService.DefaultScalePermille));

        UIRoot uiRoot = RequireUiRoot(engine);
        TickFrames(engine, 1);
        Assert.That(uiRoot.Scene, Is.Not.Null, "TimeFlow showcase must publish a visible UI scene.");
        Assert.That(
            uiRoot.Scene!.FindByElementId("capability-standard-timeflow-panel"),
            Is.Not.Null,
            "TimeFlow showcase token-stack panel must be mounted into the host UI scene.");
        Assert.That(
            uiRoot.Scene.FindByElementId("capability-standard-timeflow-reset-camera"),
            Is.Not.Null,
            "TimeFlow showcase must expose a local reset camera button.");
        Assert.That(uiRoot.Scene.FindByElementId("capability-standard-timeflow-settings-toggle"), Is.Not.Null);
        Assert.That(uiRoot.Scene.FindByElementId("capability-standard-timeflow-menu-toggle"), Is.Not.Null);
        Assert.That(uiRoot.Scene.FindByElementId("capability-standard-timeflow-skill-button"), Is.Not.Null);

        engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
        {
            VirtualCameraId = "Camera.Profile.Tactical",
            TargetCm = new Vector2(-50000f, -50000f),
            Yaw = 25f,
            Pitch = 45f,
            DistanceCm = 14000f,
            FovYDeg = 35f
        });
        runtime.ResetCamera();
        var camera = engine.GameSession.Camera.State;
        Assert.Multiple(() =>
        {
            Assert.That(camera.TargetCm.X, Is.EqualTo(1800f).Within(0.01f));
            Assert.That(camera.TargetCm.Y, Is.EqualTo(1000f).Within(0.01f));
            Assert.That(camera.Yaw, Is.EqualTo(180f).Within(0.01f));
            Assert.That(camera.Pitch, Is.EqualTo(62f).Within(0.01f));
            Assert.That(camera.DistanceCm, Is.EqualTo(9500f).Within(0.01f));
            Assert.That(camera.FovYDeg, Is.EqualTo(50f).Within(0.01f));
        });
    }

    [Test]
    public void PlayerHud_OpensClosesInterfacesAndCastsSkill()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        using var engine = CreateEngine(repoRoot);
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        CapabilityStandardTimeFlowShowcaseRuntime runtime = RequireRuntime(engine);
        UIRoot uiRoot = RequireUiRoot(engine);
        TickFrames(engine, 1);

        ClickElement(uiRoot, "capability-standard-timeflow-settings-toggle");
        TickFrames(engine, 1);
        CapabilityStandardTimeFlowShowcasePanelState settings = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(settings.SimulationPaused, Is.True);
            Assert.That(settings.SettingsPauseActive, Is.True);
            Assert.That(uiRoot.Scene!.FindByElementId("capability-standard-timeflow-close-settings"), Is.Not.Null);
        });

        ClickElement(uiRoot, "capability-standard-timeflow-close-settings");
        TickFrames(engine, 1);
        CapabilityStandardTimeFlowShowcasePanelState afterSettings = runtime.CapturePanelState(engine);
        Assert.That(afterSettings.SimulationPaused, Is.False);

        ClickElement(uiRoot, "capability-standard-timeflow-menu-toggle");
        TickFrames(engine, 1);
        CapabilityStandardTimeFlowShowcasePanelState menu = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(menu.SimulationPaused, Is.True);
            Assert.That(menu.MenuPauseActive, Is.True);
            Assert.That(uiRoot.Scene!.FindByElementId("capability-standard-timeflow-close-menu"), Is.Not.Null);
        });

        ClickElement(uiRoot, "capability-standard-timeflow-close-menu");
        TickFrames(engine, 1);
        CapabilityStandardTimeFlowShowcasePanelState afterMenu = runtime.CapturePanelState(engine);
        Assert.That(afterMenu.SimulationPaused, Is.False);

        ClickElement(uiRoot, "capability-standard-timeflow-skill-button");
        TickFrames(engine, 1);
        CapabilityStandardTimeFlowShowcasePanelState aiming = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(aiming.SimulationPaused, Is.True);
            Assert.That(aiming.SkillIndicatorPauseActive, Is.True);
            Assert.That(uiRoot.Scene!.FindByElementId("capability-standard-timeflow-guide-toggle"), Is.Not.Null);
            Assert.That(uiRoot.Scene.FindByElementId("capability-standard-timeflow-cast-skill"), Is.Not.Null);
        });

        ClickElement(uiRoot, "capability-standard-timeflow-menu-toggle");
        TickFrames(engine, 1);
        CapabilityStandardTimeFlowShowcasePanelState aimingWithMenu = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(aimingWithMenu.SimulationPaused, Is.True);
            Assert.That(aimingWithMenu.ActivePauseTokenCount, Is.EqualTo(2));
            Assert.That(aimingWithMenu.SkillIndicatorPauseActive, Is.True);
            Assert.That(aimingWithMenu.MenuPauseActive, Is.True);
            Assert.That(uiRoot.Scene!.FindByElementId("capability-standard-timeflow-close-menu"), Is.Not.Null);
        });

        ClickElement(uiRoot, "capability-standard-timeflow-close-menu");
        TickFrames(engine, 1);
        CapabilityStandardTimeFlowShowcasePanelState aimingAfterMenu = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(aimingAfterMenu.SimulationPaused, Is.True);
            Assert.That(aimingAfterMenu.ActivePauseTokenCount, Is.EqualTo(1));
            Assert.That(aimingAfterMenu.SkillIndicatorPauseActive, Is.True);
            Assert.That(aimingAfterMenu.MenuPauseActive, Is.False);
        });

        ClickElement(uiRoot, "capability-standard-timeflow-cast-skill");
        TickFrames(engine, 1);
        CapabilityStandardTimeFlowShowcasePanelState directCast = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(directCast.SimulationPaused, Is.False);
            Assert.That(directCast.ActivePauseTokenCount, Is.EqualTo(0));
            Assert.That(directCast.SkillIndicatorPauseActive, Is.False);
            Assert.That(directCast.HeroSkillCastCount, Is.EqualTo(1));
        });

        ClickElement(uiRoot, "capability-standard-timeflow-skill-button");
        TickFrames(engine, 1);
        ClickElement(uiRoot, "capability-standard-timeflow-guide-toggle");
        TickFrames(engine, 1);
        CapabilityStandardTimeFlowShowcasePanelState guidedAim = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(guidedAim.ActivePauseTokenCount, Is.EqualTo(2));
            Assert.That(guidedAim.SkillIndicatorPauseActive, Is.True);
            Assert.That(guidedAim.SystemGuidePauseActive, Is.True);
            Assert.That(uiRoot.Scene!.FindByElementId("capability-standard-timeflow-close-guide"), Is.Not.Null);
            Assert.That(uiRoot.Scene.FindByElementId("capability-standard-timeflow-cast-anyway"), Is.Not.Null);
        });

        ClickElement(uiRoot, "capability-standard-timeflow-cast-anyway");
        TickFrames(engine, 1);
        CapabilityStandardTimeFlowShowcasePanelState cast = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(cast.SimulationPaused, Is.False);
            Assert.That(cast.ActivePauseTokenCount, Is.EqualTo(0));
            Assert.That(cast.SkillIndicatorPauseActive, Is.False);
            Assert.That(cast.SystemGuidePauseActive, Is.False);
            Assert.That(cast.HeroSkillCastCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void PlayerSkillCast_RunsHeroLocalBurstWhileMassNavPhysicsAreHeldAndSystemPauseStopsHero()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        using var engine = CreateEngine(repoRoot);
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        CapabilityStandardTimeFlowShowcaseRuntime runtime = RequireRuntime(engine);

        runtime.ReleaseAllPauseTokens();
        runtime.ReleaseSimulationScaleLayerOne();
        runtime.ReleaseSimulationScaleLayerTwo();
        runtime.ReleaseGasScale();
        runtime.ResetProbes();
        TickFrames(engine, 10);

        runtime.ShowSkillAimMoment();
        CapabilityStandardTimeFlowShowcasePanelState aimStart = runtime.CapturePanelState(engine);
        TickFrames(engine, 12);
        CapabilityStandardTimeFlowShowcasePanelState aimEnd = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(aimEnd.SkillIndicatorPauseActive, Is.True);
            Assert.That(aimEnd.SimulationPaused, Is.True);
            Assert.That(aimEnd.NavPositionXCm, Is.EqualTo(aimStart.NavPositionXCm).Within(0.001f));
            Assert.That(aimEnd.PhysicsPositionXCm, Is.EqualTo(aimStart.PhysicsPositionXCm).Within(0.001f));
            Assert.That(aimEnd.GasStep, Is.EqualTo(aimStart.GasStep));
        });

        runtime.CastHeroSkill();
        TickFrames(engine, 1);
        CapabilityStandardTimeFlowShowcasePanelState burstStart = runtime.CapturePanelState(engine);
        TickFrames(engine, 18);
        CapabilityStandardTimeFlowShowcasePanelState burstRun = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(burstRun.SimulationPaused, Is.False);
            Assert.That(burstRun.HeroLocalBurstActive, Is.True);
            Assert.That(burstRun.HeroLocalBurstTick, Is.GreaterThan(burstStart.HeroLocalBurstTick));
            Assert.That(burstRun.HeroComboHitCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(burstRun.NavPositionXCm, Is.EqualTo(burstStart.NavPositionXCm).Within(0.001f));
            Assert.That(burstRun.NavigationStepCount, Is.EqualTo(burstStart.NavigationStepCount));
            Assert.That(burstRun.PhysicsPositionXCm, Is.EqualTo(burstStart.PhysicsPositionXCm).Within(0.001f));
            Assert.That(burstRun.GasStep, Is.GreaterThan(burstStart.GasStep));
        });

        runtime.OpenSettingsPause();
        TickFrames(engine, 1);
        CapabilityStandardTimeFlowShowcasePanelState systemPauseStart = runtime.CapturePanelState(engine);
        TickFrames(engine, 18);
        CapabilityStandardTimeFlowShowcasePanelState systemPauseEnd = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(systemPauseEnd.HeroLocalBurstActive, Is.True);
            Assert.That(systemPauseEnd.HeroLocalBurstPausedBySystem, Is.True);
            Assert.That(systemPauseEnd.HeroLocalBurstTick, Is.EqualTo(systemPauseStart.HeroLocalBurstTick));
            Assert.That(systemPauseEnd.HeroComboHitCount, Is.EqualTo(systemPauseStart.HeroComboHitCount));
            Assert.That(systemPauseEnd.GasStep, Is.EqualTo(systemPauseStart.GasStep));
            Assert.That(systemPauseEnd.NavPositionXCm, Is.EqualTo(systemPauseStart.NavPositionXCm).Within(0.001f));
            Assert.That(systemPauseEnd.PhysicsPositionXCm, Is.EqualTo(systemPauseStart.PhysicsPositionXCm).Within(0.001f));
        });

        runtime.CloseSettingsPause();
        TickFrames(engine, 18);
        CapabilityStandardTimeFlowShowcasePanelState resumed = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(resumed.HeroLocalBurstActive, Is.True);
            Assert.That(resumed.HeroLocalBurstPausedBySystem, Is.False);
            Assert.That(resumed.HeroLocalBurstTick, Is.GreaterThan(systemPauseEnd.HeroLocalBurstTick));
            Assert.That(resumed.HeroComboHitCount, Is.GreaterThanOrEqualTo(systemPauseEnd.HeroComboHitCount));
        });
    }

    [Test]
    public void Shortcuts_DriveSkillPauseStackLocalBurstInterfacesAndSpeed()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        var input = new CapabilityStandardShowcaseTestHarness.TestInputBackend();
        using var engine = CreateEngine(repoRoot, input);
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        CapabilityStandardTimeFlowShowcaseRuntime runtime = RequireRuntime(engine);
        TimeFlowShortcutConfig shortcuts = runtime.ActiveConfig.Shortcuts;

        PressShortcut(engine, input, shortcuts.Skill);
        CapabilityStandardTimeFlowShowcasePanelState aiming = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(aiming.SkillIndicatorPauseActive, Is.True);
            Assert.That(aiming.SimulationPaused, Is.True);
        });

        PressShortcut(engine, input, shortcuts.Guide);
        CapabilityStandardTimeFlowShowcasePanelState guide = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(guide.SystemGuidePauseActive, Is.True);
            Assert.That(guide.ActivePauseTokenCount, Is.EqualTo(2));
        });

        PressShortcut(engine, input, shortcuts.Skill);
        TickFrames(engine, 8);
        CapabilityStandardTimeFlowShowcasePanelState burst = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(burst.HeroLocalBurstActive, Is.True);
            Assert.That(burst.SkillIndicatorPauseActive, Is.False);
            Assert.That(burst.SystemGuidePauseActive, Is.False);
            Assert.That(burst.ActivePauseTokenCount, Is.EqualTo(0));
        });

        PressShortcut(engine, input, shortcuts.Settings);
        CapabilityStandardTimeFlowShowcasePanelState settings = runtime.CapturePanelState(engine);
        TickFrames(engine, 8);
        CapabilityStandardTimeFlowShowcasePanelState heldBySettings = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(heldBySettings.SettingsPauseActive, Is.True);
            Assert.That(heldBySettings.HeroLocalBurstPausedBySystem, Is.True);
            Assert.That(heldBySettings.HeroLocalBurstTick, Is.EqualTo(settings.HeroLocalBurstTick));
        });

        PressShortcut(engine, input, shortcuts.CloseTop);
        TickFrames(engine, 8);
        CapabilityStandardTimeFlowShowcasePanelState resumed = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(resumed.SettingsPauseActive, Is.False);
            Assert.That(resumed.HeroLocalBurstPausedBySystem, Is.False);
            Assert.That(resumed.HeroLocalBurstTick, Is.GreaterThan(heldBySettings.HeroLocalBurstTick));
        });

        PressShortcut(engine, input, shortcuts.Menu);
        CapabilityStandardTimeFlowShowcasePanelState menu = runtime.CapturePanelState(engine);
        Assert.That(menu.MenuPauseActive, Is.True);

        PressShortcut(engine, input, shortcuts.CloseTop);
        CapabilityStandardTimeFlowShowcasePanelState afterMenu = runtime.CapturePanelState(engine);
        Assert.That(afterMenu.MenuPauseActive, Is.False);

        PressShortcut(engine, input, shortcuts.FastSpeed);
        CapabilityStandardTimeFlowShowcasePanelState fast = runtime.CapturePanelState(engine);
        Assert.That(fast.SimulationScaleLayerOnePermille, Is.EqualTo(2000));

        PressShortcut(engine, input, shortcuts.SlowSpeed);
        CapabilityStandardTimeFlowShowcasePanelState slow = runtime.CapturePanelState(engine);
        Assert.That(slow.SimulationScaleLayerOnePermille, Is.EqualTo(500));
    }

    [Test]
    public void SharedClock_PauseAndScaleDriveNavigationPhysicsAndGasProbes()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        using var engine = CreateEngine(repoRoot);
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        CapabilityStandardTimeFlowShowcaseRuntime runtime = RequireRuntime(engine);

        runtime.ReleaseAllPauseTokens();
        runtime.ReleaseSimulationScaleLayerOne();
        runtime.ReleaseSimulationScaleLayerTwo();
        runtime.ReleaseGasScale();
        runtime.ResetProbes();

        CapabilityStandardTimeFlowShowcasePanelState baselineStart = runtime.CapturePanelState(engine);
        TickFrames(engine, 30);
        CapabilityStandardTimeFlowShowcasePanelState baselineEnd = runtime.CapturePanelState(engine);

        float baselineNavDelta = baselineEnd.NavPositionXCm - baselineStart.NavPositionXCm;
        float baselinePhysicsDelta = baselineEnd.PhysicsPositionXCm - baselineStart.PhysicsPositionXCm;
        int baselineGasDelta = baselineEnd.GasStep - baselineStart.GasStep;

        Assert.Multiple(() =>
        {
            Assert.That(baselineNavDelta, Is.GreaterThan(20f));
            Assert.That(baselinePhysicsDelta, Is.GreaterThan(20f));
            Assert.That(baselineGasDelta, Is.GreaterThan(0));
        });

        runtime.ResetProbes();
        runtime.ApplySimulationScale(requestIndex: 2);
        CapabilityStandardTimeFlowShowcasePanelState fastStart = runtime.CapturePanelState(engine);
        TickFrames(engine, 30);
        CapabilityStandardTimeFlowShowcasePanelState fastEnd = runtime.CapturePanelState(engine);

        float fastNavDelta = fastEnd.NavPositionXCm - fastStart.NavPositionXCm;
        float fastPhysicsDelta = fastEnd.PhysicsPositionXCm - fastStart.PhysicsPositionXCm;
        int fastGasDelta = fastEnd.GasStep - fastStart.GasStep;

        Assert.Multiple(() =>
        {
            Assert.That(fastEnd.SimulationScalePermille, Is.EqualTo(2000));
            Assert.That(fastEnd.SimulationScaleLayerOnePermille, Is.EqualTo(2000));
            Assert.That(fastEnd.ActiveScaleTokenCount, Is.EqualTo(1));
            Assert.That(fastEnd.GasEffectiveScalePermille, Is.EqualTo(2000));
            Assert.That(fastEnd.GasPolicyScalePermille, Is.EqualTo(1000),
                "GAS policy runs inside the simulation pacemaker, so parent simulation scale must not be applied twice.");
            Assert.That(fastNavDelta, Is.GreaterThan(baselineNavDelta * 1.5f));
            Assert.That(fastPhysicsDelta, Is.GreaterThan(baselinePhysicsDelta * 1.5f));
            Assert.That(fastGasDelta, Is.GreaterThan(baselineGasDelta * 1.5f));
            Assert.That(fastGasDelta, Is.LessThan(baselineGasDelta * 3),
                "A 2x simulation token must not become 4x GAS steps through double parent composition.");
        });

        runtime.ApplyGasScale(requestIndex: 2);
        TickFrames(engine, 1);
        CapabilityStandardTimeFlowShowcasePanelState gasScaled = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(gasScaled.GasEffectiveScalePermille, Is.EqualTo(4000));
            Assert.That(gasScaled.GasPolicyScalePermille, Is.EqualTo(2000));
        });

        runtime.AcquireSimulationPause();
        TickFrames(engine, 1);
        CapabilityStandardTimeFlowShowcasePanelState pauseStart = runtime.CapturePanelState(engine);
        TickFrames(engine, 30);
        CapabilityStandardTimeFlowShowcasePanelState pauseEnd = runtime.CapturePanelState(engine);

        Assert.Multiple(() =>
        {
            Assert.That(pauseEnd.SimulationPaused, Is.True);
            Assert.That(pauseEnd.SimulationScalePermille, Is.EqualTo(0));
            Assert.That(pauseEnd.NavPositionXCm, Is.EqualTo(pauseStart.NavPositionXCm).Within(0.001f));
            Assert.That(pauseEnd.PhysicsPositionXCm, Is.EqualTo(pauseStart.PhysicsPositionXCm).Within(0.001f));
            Assert.That(pauseEnd.GasStep, Is.EqualTo(pauseStart.GasStep));
        });
    }

    [Test]
    public void TokenStack_ShowcasesSettingsMenuSkillIndicatorAndNestedSystemGuide()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        using var engine = CreateEngine(repoRoot);
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        CapabilityStandardTimeFlowShowcaseRuntime runtime = RequireRuntime(engine);

        runtime.ReleaseAllPauseTokens();
        runtime.ReleaseSimulationScaleLayerOne();
        runtime.ReleaseSimulationScaleLayerTwo();
        runtime.ReleaseGasScale();
        runtime.ResetProbes();

        CapabilityStandardTimeFlowShowcasePanelState runningStart = runtime.CapturePanelState(engine);
        TickFrames(engine, 10);
        CapabilityStandardTimeFlowShowcasePanelState runningEnd = runtime.CapturePanelState(engine);
        Assert.That(runningEnd.NavPositionXCm, Is.GreaterThan(runningStart.NavPositionXCm));

        runtime.BeginSkillIndicatorPause();
        CapabilityStandardTimeFlowShowcasePanelState skillPauseStart = runtime.CapturePanelState(engine);
        TickFrames(engine, 10);
        CapabilityStandardTimeFlowShowcasePanelState skillPauseEnd = runtime.CapturePanelState(engine);

        Assert.Multiple(() =>
        {
            Assert.That(skillPauseEnd.SimulationPaused, Is.True);
            Assert.That(skillPauseEnd.ActivePauseTokenCount, Is.EqualTo(1));
            Assert.That(skillPauseEnd.SkillIndicatorPauseActive, Is.True);
            Assert.That(skillPauseEnd.NavPositionXCm, Is.EqualTo(skillPauseStart.NavPositionXCm).Within(0.001f));
            Assert.That(skillPauseEnd.PhysicsPositionXCm, Is.EqualTo(skillPauseStart.PhysicsPositionXCm).Within(0.001f));
            Assert.That(skillPauseEnd.GasStep, Is.EqualTo(skillPauseStart.GasStep));
        });

        runtime.ShowSystemGuideDuringSkillIndicator();
        CapabilityStandardTimeFlowShowcasePanelState guideStack = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(guideStack.ActivePauseTokenCount, Is.EqualTo(2));
            Assert.That(guideStack.SkillIndicatorPauseActive, Is.True);
            Assert.That(guideStack.SystemGuidePauseActive, Is.True);
            Assert.That(guideStack.PauseTokenStackSummary, Does.Contain("skill target indicator pause"));
            Assert.That(guideStack.PauseTokenStackSummary, Does.Contain("system guide pause while skill indicator is open"));
        });

        runtime.EndSkillIndicatorPause();
        CapabilityStandardTimeFlowShowcasePanelState guideOnlyStart = runtime.CapturePanelState(engine);
        TickFrames(engine, 10);
        CapabilityStandardTimeFlowShowcasePanelState guideOnlyEnd = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(guideOnlyEnd.SimulationPaused, Is.True);
            Assert.That(guideOnlyEnd.ActivePauseTokenCount, Is.EqualTo(1));
            Assert.That(guideOnlyEnd.SkillIndicatorPauseActive, Is.False);
            Assert.That(guideOnlyEnd.SystemGuidePauseActive, Is.True);
            Assert.That(guideOnlyEnd.NavPositionXCm, Is.EqualTo(guideOnlyStart.NavPositionXCm).Within(0.001f));
            Assert.That(guideOnlyEnd.GasStep, Is.EqualTo(guideOnlyStart.GasStep));
        });

        runtime.DismissSystemGuidePause();
        TickFrames(engine, 10);
        CapabilityStandardTimeFlowShowcasePanelState resumedAfterGuide = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(resumedAfterGuide.SimulationPaused, Is.False);
            Assert.That(resumedAfterGuide.ActivePauseTokenCount, Is.EqualTo(0));
            Assert.That(resumedAfterGuide.NavPositionXCm, Is.GreaterThan(guideOnlyEnd.NavPositionXCm));
            Assert.That(resumedAfterGuide.GasStep, Is.GreaterThan(guideOnlyEnd.GasStep));
        });

        runtime.OpenSettingsPause();
        runtime.OpenMenuPause();
        CapabilityStandardTimeFlowShowcasePanelState settingsAndMenu = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(settingsAndMenu.ActivePauseTokenCount, Is.EqualTo(2));
            Assert.That(settingsAndMenu.SettingsPauseActive, Is.True);
            Assert.That(settingsAndMenu.MenuPauseActive, Is.True);
        });

        runtime.CloseSettingsPause();
        CapabilityStandardTimeFlowShowcasePanelState menuOnly = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(menuOnly.SimulationPaused, Is.True);
            Assert.That(menuOnly.ActivePauseTokenCount, Is.EqualTo(1));
            Assert.That(menuOnly.SettingsPauseActive, Is.False);
            Assert.That(menuOnly.MenuPauseActive, Is.True);
        });

        runtime.CloseMenuPause();
        CapabilityStandardTimeFlowShowcasePanelState resumedAfterMenu = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(resumedAfterMenu.SimulationPaused, Is.False);
            Assert.That(resumedAfterMenu.ActivePauseTokenCount, Is.EqualTo(0));
        });

        runtime.BeginSkillIndicatorPause();
        runtime.OpenMenuPause();
        CapabilityStandardTimeFlowShowcasePanelState skillAndMenu = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(skillAndMenu.SimulationPaused, Is.True);
            Assert.That(skillAndMenu.ActivePauseTokenCount, Is.EqualTo(2));
            Assert.That(skillAndMenu.SkillIndicatorPauseActive, Is.True);
            Assert.That(skillAndMenu.MenuPauseActive, Is.True);
        });

        runtime.CloseMenuPause();
        CapabilityStandardTimeFlowShowcasePanelState skillOnly = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(skillOnly.SimulationPaused, Is.True);
            Assert.That(skillOnly.ActivePauseTokenCount, Is.EqualTo(1));
            Assert.That(skillOnly.SkillIndicatorPauseActive, Is.True);
            Assert.That(skillOnly.MenuPauseActive, Is.False);
        });
    }

    [Test]
    public void ShowcaseFlowButtons_PresentOneReadablePauseMomentAtATime()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        using var engine = CreateEngine(repoRoot);
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        CapabilityStandardTimeFlowShowcaseRuntime runtime = RequireRuntime(engine);

        runtime.ShowSettingsMoment();
        CapabilityStandardTimeFlowShowcasePanelState settings = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(settings.SimulationPaused, Is.True);
            Assert.That(settings.ActivePauseTokenCount, Is.EqualTo(1));
            Assert.That(settings.SettingsPauseActive, Is.True);
            Assert.That(settings.MenuPauseActive, Is.False);
            Assert.That(settings.SkillIndicatorPauseActive, Is.False);
            Assert.That(settings.SystemGuidePauseActive, Is.False);
        });

        runtime.ShowMenuMoment();
        CapabilityStandardTimeFlowShowcasePanelState menu = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(menu.SimulationPaused, Is.True);
            Assert.That(menu.ActivePauseTokenCount, Is.EqualTo(1));
            Assert.That(menu.SettingsPauseActive, Is.False);
            Assert.That(menu.MenuPauseActive, Is.True);
            Assert.That(menu.SkillIndicatorPauseActive, Is.False);
            Assert.That(menu.SystemGuidePauseActive, Is.False);
        });

        runtime.ShowSkillAimMoment();
        CapabilityStandardTimeFlowShowcasePanelState skill = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(skill.SimulationPaused, Is.True);
            Assert.That(skill.ActivePauseTokenCount, Is.EqualTo(1));
            Assert.That(skill.SettingsPauseActive, Is.False);
            Assert.That(skill.MenuPauseActive, Is.False);
            Assert.That(skill.SkillIndicatorPauseActive, Is.True);
            Assert.That(skill.SystemGuidePauseActive, Is.False);
        });

        runtime.ShowGuideDuringSkillMoment();
        CapabilityStandardTimeFlowShowcasePanelState guide = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(guide.SimulationPaused, Is.True);
            Assert.That(guide.ActivePauseTokenCount, Is.EqualTo(2));
            Assert.That(guide.SettingsPauseActive, Is.False);
            Assert.That(guide.MenuPauseActive, Is.False);
            Assert.That(guide.SkillIndicatorPauseActive, Is.True);
            Assert.That(guide.SystemGuidePauseActive, Is.True);
        });

        runtime.ShowRunningMoment();
        CapabilityStandardTimeFlowShowcasePanelState running = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(running.SimulationPaused, Is.False);
            Assert.That(running.ActivePauseTokenCount, Is.EqualTo(0));
            Assert.That(running.SettingsPauseActive, Is.False);
            Assert.That(running.MenuPauseActive, Is.False);
            Assert.That(running.SkillIndicatorPauseActive, Is.False);
            Assert.That(running.SystemGuidePauseActive, Is.False);
        });
    }

    [Test]
    public void TokenStack_ComposesAndReleasesSimulationScaleLayers()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        using var engine = CreateEngine(repoRoot);
        engine.LoadEntryMap(engine.MergedConfig.StartupMapId);
        CapabilityStandardTimeFlowShowcaseRuntime runtime = RequireRuntime(engine);

        runtime.ReleaseAllPauseTokens();
        runtime.ReleaseSimulationScaleLayerOne();
        runtime.ReleaseSimulationScaleLayerTwo();
        runtime.ReleaseGasScale();

        runtime.ApplySimulationScaleLayerOne(requestIndex: 2);
        runtime.ApplySimulationScaleLayerTwo(requestIndex: 0);
        CapabilityStandardTimeFlowShowcasePanelState composed = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(composed.ActiveScaleTokenCount, Is.EqualTo(2));
            Assert.That(composed.SimulationScaleLayerOnePermille, Is.EqualTo(2000));
            Assert.That(composed.SimulationScaleLayerTwoPermille, Is.EqualTo(500));
            Assert.That(composed.SimulationScalePermille, Is.EqualTo(1000));
            Assert.That(composed.ScaleTokenStackSummary, Does.Contain("simulation scale layer one"));
            Assert.That(composed.ScaleTokenStackSummary, Does.Contain("simulation scale layer two"));
        });

        runtime.ReleaseSimulationScaleLayerTwo();
        CapabilityStandardTimeFlowShowcasePanelState layerOneOnly = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(layerOneOnly.ActiveScaleTokenCount, Is.EqualTo(1));
            Assert.That(layerOneOnly.SimulationScalePermille, Is.EqualTo(2000));
            Assert.That(layerOneOnly.SimulationScaleLayerTwoPermille, Is.EqualTo(0));
        });

        runtime.ReleaseSimulationScaleLayerOne();
        CapabilityStandardTimeFlowShowcasePanelState clear = runtime.CapturePanelState(engine);
        Assert.Multiple(() =>
        {
            Assert.That(clear.ActiveScaleTokenCount, Is.EqualTo(0));
            Assert.That(clear.SimulationScalePermille, Is.EqualTo(TimeFlowService.DefaultScalePermille));
        });
    }

    private static GameEngine CreateEngine(string repoRoot, IInputBackend? inputBackend = null)
    {
        GameEngine engine = CapabilityStandardShowcaseTestHarness.CreateEngine(repoRoot, AcceptanceMods, inputBackend);
        AcceptanceUiHostInstaller.Install(engine);
        return engine;
    }

    private static CapabilityStandardTimeFlowShowcaseRuntime RequireRuntime(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.BenchmarkSceneController) as CapabilityStandardTimeFlowShowcaseRuntime
            ?? throw new InvalidOperationException("Capability-standard TimeFlow showcase runtime missing.");
    }

    private static UIRoot RequireUiRoot(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("TimeFlow showcase UI root missing.");
    }

    private static void TickFrames(GameEngine engine, int frames)
    {
        var frameTimes = new List<double>(frames);
        CapabilityStandardShowcaseTestHarness.TickMeasured(engine, frames, frameTimes);
    }

    private static void PressShortcut(
        GameEngine engine,
        CapabilityStandardShowcaseTestHarness.TestInputBackend input,
        string devicePath)
    {
        input.SetButton(devicePath, true);
        TickFrames(engine, 1);
        input.SetButton(devicePath, false);
        TickFrames(engine, 1);
    }

    private static void ClickElement(UIRoot root, string elementId)
    {
        UiScene scene = root.Scene ?? throw new InvalidOperationException("UI scene is not mounted.");
        scene.Layout(root.Width, root.Height);
        UiNode node = scene.FindByElementId(elementId)
            ?? throw new InvalidOperationException($"UI element '{elementId}' was not found.");
        Assert.That(node.ActionHandles.Count, Is.GreaterThan(0), $"UI element '{elementId}' must be clickable.");

        float x = node.LayoutRect.X + (node.LayoutRect.Width * 0.5f);
        float y = node.LayoutRect.Y + (node.LayoutRect.Height * 0.5f);
        UiNode? hitNode = scene.HitTest(x, y);
        Assert.That(
            hitNode?.ElementId,
            Is.EqualTo(elementId),
            $"Pointer click for '{elementId}' hit '{hitNode?.ElementId ?? hitNode?.TagName ?? "<none>"}' instead.");
        bool downHandled = root.HandleInput(new PointerEvent
        {
            PointerId = 0,
            Action = PointerAction.Down,
            Button = PointerButton.Left,
            X = x,
            Y = y
        });
        bool upHandled = root.HandleInput(new PointerEvent
        {
            PointerId = 0,
            Action = PointerAction.Up,
            Button = PointerButton.Left,
            X = x,
            Y = y
        });

        Assert.That(downHandled || upHandled, Is.True, $"UI element '{elementId}' did not handle pointer click.");
    }

    private static void AssertLauncherBinding(string repoRoot)
    {
        string launcherConfig = Path.Combine(repoRoot, "launcher.config.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(launcherConfig));
        foreach (JsonElement binding in document.RootElement.GetProperty("bindings").EnumerateArray())
        {
            if (!string.Equals(binding.GetProperty("name").GetString(), BindingName, StringComparison.Ordinal))
            {
                continue;
            }

            JsonElement target = binding.GetProperty("target");
            Assert.That(target.GetProperty("type").GetString(), Is.EqualTo("path"));
            Assert.That(
                target.GetProperty("value").GetString(),
                Is.EqualTo("mods/showcases/capability_standard/CapabilityStandardTimeFlowShowcaseMod"));
            Assert.That(target.GetProperty("projectPath").GetString(), Is.EqualTo("CapabilityStandardTimeFlowShowcaseMod.csproj"));
            return;
        }

        Assert.Fail($"Launcher binding '{BindingName}' is missing.");
    }

    private static void AssertLauncherPreset(string repoRoot)
    {
        string launcherPresets = Path.Combine(repoRoot, "launcher.presets.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(launcherPresets));
        foreach (JsonElement preset in document.RootElement.GetProperty("presets").EnumerateArray())
        {
            if (!string.Equals(preset.GetProperty("id").GetString(), PresetId, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.That(preset.GetProperty("adapterId").GetString(), Is.EqualTo("raylib"));
            JsonElement selectors = preset.GetProperty("selectors");
            Assert.That(selectors.GetArrayLength(), Is.EqualTo(1));
            Assert.That(selectors[0].GetString(), Is.EqualTo($"${BindingName}"));
            return;
        }

        Assert.Fail($"Launcher preset '{PresetId}' is missing.");
    }

    private static void AssertShowcaseCatalog(string modDir)
    {
        string catalogPath = Path.Combine(modDir, "assets", "Configs", "config_catalog.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(catalogPath));
        AssertCatalogEntry(document.RootElement, ShowcaseConfigPath, "Replace");
        AssertCatalogEntry(document.RootElement, "Physics2D/clock.json", "DeepObject");
    }

    private static void AssertCatalogEntry(JsonElement catalog, string path, string policy)
    {
        foreach (JsonElement entry in catalog.EnumerateArray())
        {
            if (!string.Equals(entry.GetProperty("Path").GetString(), path, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.That(entry.GetProperty("Policy").GetString(), Is.EqualTo(policy));
            return;
        }

        Assert.Fail($"Catalog entry '{path}' is missing.");
    }

    private static void AssertGameJson(string modDir)
    {
        string gamePath = Path.Combine(modDir, "assets", "game.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(gamePath));
        JsonElement root = document.RootElement;
        Assert.That(root.GetProperty("startupMapId").GetString(), Is.EqualTo(MapId));
        Assert.That(root.GetProperty("physics2D").GetProperty("enabled").GetBoolean(), Is.True);
    }

    private static void AssertNoFormationOrActionAuthoring(string modDir)
    {
        string config = File.ReadAllText(Path.Combine(modDir, "assets", ShowcaseConfigPath));
        Assert.That(config.IndexOf("formation", StringComparison.OrdinalIgnoreCase), Is.EqualTo(-1));

        foreach (string file in Directory.EnumerateFiles(modDir, "*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(file);
            if (!string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(modDir, file).Replace('\\', '/');
            Assert.That(relative.Contains("Input/", StringComparison.OrdinalIgnoreCase), Is.False);
            string text = File.ReadAllText(file);
            Assert.That(text.Contains("TimeFlowInputActions", StringComparison.Ordinal), Is.False);
            Assert.That(text.Contains("default_input", StringComparison.OrdinalIgnoreCase), Is.False);
            Assert.That(text.Contains("MassNavigationFormationRuntime", StringComparison.Ordinal), Is.False);
            Assert.That(text.Contains("MassNavigationFormationAnchor", StringComparison.Ordinal), Is.False);
            Assert.That(text.Contains("MassNavigationFormationFollower", StringComparison.Ordinal), Is.False);
        }
    }
}
