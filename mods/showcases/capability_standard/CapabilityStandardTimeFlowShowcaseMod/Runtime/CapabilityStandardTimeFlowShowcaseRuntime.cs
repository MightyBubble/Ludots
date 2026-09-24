using System;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Core.Client;

namespace CapabilityStandardTimeFlowShowcaseMod.Runtime;

internal sealed class CapabilityStandardTimeFlowShowcaseRuntime : IBenchmarkSceneController, IInputFrameConsumer
{
    private const string TokenOwner = "CapabilityStandardTimeFlowShowcaseMod";
    private const string SettingsPauseOwner = TokenOwner + ".Settings";
    private const string MenuPauseOwner = TokenOwner + ".Menu";
    private const string SkillIndicatorPauseOwner = TokenOwner + ".SkillIndicator";
    private const string SystemGuidePauseOwner = TokenOwner + ".SystemGuide";

    private readonly QueryDescription _ownedEntityQuery = new QueryDescription().WithAll<CapabilityStandardTimeFlowShowcaseEntityTag>();
    private readonly QueryDescription _physicsProbeQuery = new QueryDescription().WithAll<CapabilityStandardTimeFlowShowcasePhysicsProbeTag, Position2D, Velocity2D>();

    private CapabilityStandardTimeFlowShowcaseConfig? _config;
    private GameEngine? _activeEngine;
    private Entity _navigationProbeEntity = Entity.Null;
    private Entity _physicsProbeEntity = Entity.Null;
    private MassNavigationFlowSolverState? _navigationFlow;
    private MassNavigationFlowStepper? _navigationStepper;
    private int _physicsShapeIndex = -1;
    private TimeFlowToken _settingsPauseToken;
    private TimeFlowToken _menuPauseToken;
    private TimeFlowToken _skillIndicatorPauseToken;
    private TimeFlowToken _systemGuidePauseToken;
    private TimeFlowToken _simulationScaleLayerOneToken;
    private TimeFlowToken _simulationScaleLayerTwoToken;
    private TimeFlowToken _gasScaleToken;
    private int _simulationScaleLayerOnePermille;
    private int _simulationScaleLayerTwoPermille;
    private int _gasScalePermille;
    private bool _shortcutSettingsDown;
    private bool _shortcutMenuDown;
    private bool _shortcutSkillDown;
    private bool _shortcutGuideDown;
    private bool _shortcutResetCameraDown;
    private bool _shortcutSlowSpeedDown;
    private bool _shortcutNormalSpeedDown;
    private bool _shortcutFastSpeedDown;
    private bool _shortcutCloseTopDown;
    private string _lastEvent = "TimeFlow showcase ready.";
    private int _navigationStepCount;
    private int _heroSkillCastCount;
    private int _lastHeroSkillCastGasStep = -1;
    private bool _heroLocalBurstActive;
    private float _heroLocalClockSeconds;
    private int _heroLocalBurstTick;
    private int _heroComboHitCount;
    private float _heroBurstStartXCm;
    private float _heroBurstStartYCm;
    private float _heroLocalPositionXCm;
    private float _heroLocalPositionYCm;
    private float _heroBurstEnemyXCm;
    private float _heroBurstEnemyYCm;
    private bool _physicsFrozenForHeroBurst;
    private Fix64Vec2 _heroBurstPhysicsPosition;
    private Fix64Vec2 _heroBurstPhysicsVelocity;
    private Fix64 _heroBurstPhysicsAngularVelocity;

    public bool IsActive => _activeEngine != null && IsShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value);
    public bool SupportsScatterControl => false;
    public bool IsCleanPerformanceScene => false;
    public bool SuppressHostDiagnosticUi => false;
    public bool SuppressHostDebugGuides => false;
    public int ScatterMin => 0;
    public int ScatterMax => 0;
    public int ScatterTarget => 0;
    public int ScatterAppliedTotal => 0;
    public void SetScatterTargetFromRatio(float ratio) => ThrowScatterUnsupported();
    public void ApplyScatterTarget() => ThrowScatterUnsupported();
    public void ApplyScatterLayout(int total) => ThrowScatterUnsupported();

    public void Consume(GameEngine engine, PlayerInputHandler input, float deltaTime)
    {
        HandleShortcuts(engine);
    }

    public CapabilityStandardTimeFlowShowcaseConfig ActiveConfig => _config
        ?? throw new InvalidOperationException("TimeFlow showcase config has not been loaded.");

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        CapabilityStandardTimeFlowShowcaseConfig config = EnsureConfig(engine);
        if (!IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            Disable(engine);
            return Task.CompletedTask;
        }

        _activeEngine = engine;
        EnsurePhysicsProbe(engine, config);
        EnsureNavigationProbe(engine, config);
        _lastEvent = "TimeFlow showcase active. UI controls request and release TimeFlow tokens.";
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        string? mapId = context.TryGet(CoreServiceKeys.MapId, out var contextMapId)
            ? contextMapId.Value
            : engine?.CurrentMapSession?.MapId.Value;
        if (engine != null && IsShowcaseMap(mapId))
        {
            Disable(engine);
        }

        return Task.CompletedTask;
    }

    public CapabilityStandardTimeFlowShowcasePanelState CapturePanelState(GameEngine engine)
    {
        if (!IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            return CapabilityStandardTimeFlowShowcasePanelState.Empty;
        }

        TimeFlowService timeFlow = RequireTimeFlow(engine);
        GasClocks gasClocks = engine.GetService(CoreServiceKeys.GasClocks)
            ?? throw new InvalidOperationException("TimeFlow showcase requires GasClocks.");
        GasClockStepPolicy gasPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy)
            ?? throw new InvalidOperationException("TimeFlow showcase requires GasClockStepPolicy.");
        ReadNavigationProbe(out float navX, out float navY);
        ReadPhysicsProbe(engine.World, out float physicsX, out float physicsY, out float physicsVx, out float physicsVy);
        ResolveHeroLocalPosition(navX, navY, out float heroLocalX, out float heroLocalY);
        int skillCastAgeSteps = _lastHeroSkillCastGasStep < 0
            ? int.MaxValue
            : Math.Max(0, gasClocks.StepNow - _lastHeroSkillCastGasStep);

        TimeFlowSnapshot snapshot = timeFlow.CaptureSnapshot();
        return new CapabilityStandardTimeFlowShowcasePanelState(
            Title: "TimeFlow Capability",
            LastEvent: _lastEvent,
            SimulationScalePermille: timeFlow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation),
            GasEffectiveScalePermille: timeFlow.GetEffectiveScalePermille(TimeFlowDomainIds.Gas),
            GasPolicyScalePermille: gasPolicy.ScalePermille,
            SimulationPaused: timeFlow.IsPaused(TimeFlowDomainIds.Simulation),
            GasPaused: timeFlow.IsPaused(TimeFlowDomainIds.Gas),
            ActiveTokenCount: snapshot.ActiveTokens.Count,
            ActivePauseTokenCount: CountTokens(snapshot, "Pause"),
            ActiveScaleTokenCount: CountTokens(snapshot, "Scale"),
            ActiveTokenSummary: BuildTokenSummary(snapshot, kind: null),
            PauseTokenStackSummary: BuildTokenSummary(snapshot, "Pause"),
            ScaleTokenStackSummary: BuildTokenSummary(snapshot, "Scale"),
            SettingsPauseActive: _settingsPauseToken.IsValid,
            MenuPauseActive: _menuPauseToken.IsValid,
            SkillIndicatorPauseActive: _skillIndicatorPauseToken.IsValid,
            SystemGuidePauseActive: _systemGuidePauseToken.IsValid,
            SimulationScaleLayerOnePermille: _simulationScaleLayerOnePermille,
            SimulationScaleLayerTwoPermille: _simulationScaleLayerTwoPermille,
            GasScaleTokenPermille: _gasScalePermille,
            HeroSkillCastCount: _heroSkillCastCount,
            HeroSkillCastAgeSteps: skillCastAgeSteps,
            HeroLocalBurstActive: _heroLocalBurstActive,
            HeroLocalBurstPausedBySystem: _heroLocalBurstActive && timeFlow.IsPaused(TimeFlowDomainIds.Simulation),
            HeroLocalBurstTick: _heroLocalBurstTick,
            HeroComboHitCount: _heroComboHitCount,
            HeroLocalClockSeconds: _heroLocalClockSeconds,
            HeroLocalPositionXCm: heroLocalX,
            HeroLocalPositionYCm: heroLocalY,
            EnemyPositionXCm: ActiveConfig.NavigationProbe.TargetXCm,
            EnemyPositionYCm: ActiveConfig.NavigationProbe.TargetYCm,
            NavigationStepCount: _navigationStepCount,
            NavPositionXCm: navX,
            NavPositionYCm: navY,
            NavTargetXCm: ActiveConfig.NavigationProbe.TargetXCm,
            NavTargetYCm: ActiveConfig.NavigationProbe.TargetYCm,
            PhysicsPositionXCm: physicsX,
            PhysicsPositionYCm: physicsY,
            PhysicsVelocityXCm: physicsVx,
            PhysicsVelocityYCm: physicsVy,
            GasFixedFrame: gasClocks.FixedFrameNow,
            GasStep: gasClocks.StepNow);
    }

    public void AdvanceSimulation(GameEngine engine, float dt)
    {
        if (!IsActive)
        {
            return;
        }

        if (_heroLocalBurstActive)
        {
            if (dt > 0f)
            {
                AdvanceHeroLocalBurst(engine, dt);
            }

            return;
        }

        if (dt <= 0f)
        {
            return;
        }

        MassNavigationFlowSolverState flow = _navigationFlow
            ?? throw new InvalidOperationException("TimeFlow showcase navigation probe is not initialized.");
        MassNavigationFlowStepper stepper = _navigationStepper
            ?? throw new InvalidOperationException("TimeFlow showcase navigation stepper is not initialized.");

        stepper.Step(
            engine.World,
            dt,
            runHardResolve: false,
            hardResolveCandidateThresholdAgents: 2);
        _navigationStepCount++;
        SyncNavigationEntity(engine.World);
    }

    public void HandleShortcuts(GameEngine engine)
    {
        if (!IsActive)
        {
            return;
        }

        if (!engine.TryGetService(CoreServiceKeys.InputBackend, out IInputBackend input))
        {
            throw new InvalidOperationException("TimeFlow showcase shortcuts require InputBackend.");
        }

        TimeFlowShortcutConfig shortcuts = ActiveConfig.Shortcuts;
        if (ConsumeShortcut(input, shortcuts.CloseTop, ref _shortcutCloseTopDown))
        {
            CloseTopmostPlayerLayer();
        }

        if (ConsumeShortcut(input, shortcuts.Settings, ref _shortcutSettingsDown))
        {
            if (_settingsPauseToken.IsValid)
            {
                CloseSettingsPause();
            }
            else
            {
                OpenSettingsPause();
            }
        }

        if (ConsumeShortcut(input, shortcuts.Menu, ref _shortcutMenuDown))
        {
            if (_menuPauseToken.IsValid)
            {
                CloseMenuPause();
            }
            else
            {
                OpenMenuPause();
            }
        }

        if (ConsumeShortcut(input, shortcuts.Skill, ref _shortcutSkillDown))
        {
            TriggerSkillShortcut();
        }

        if (ConsumeShortcut(input, shortcuts.Guide, ref _shortcutGuideDown))
        {
            ToggleGuideShortcut();
        }

        if (ConsumeShortcut(input, shortcuts.ResetCamera, ref _shortcutResetCameraDown))
        {
            ResetCamera();
        }

        if (ConsumeShortcut(input, shortcuts.SlowSpeed, ref _shortcutSlowSpeedDown))
        {
            ApplySimulationScaleLayerOne(0);
        }

        if (ConsumeShortcut(input, shortcuts.NormalSpeed, ref _shortcutNormalSpeedDown))
        {
            ApplySimulationScaleLayerOne(1);
        }

        if (ConsumeShortcut(input, shortcuts.FastSpeed, ref _shortcutFastSpeedDown))
        {
            ApplySimulationScaleLayerOne(2);
        }
    }

    public void OpenSettingsPause()
    {
        GameEngine engine = RequireActiveEngine();
        AcquirePauseIfMissing(
            engine,
            ref _settingsPauseToken,
            SettingsPauseOwner,
            "system settings page pause",
            "System settings pause token entered the stack.");
    }

    public void CloseSettingsPause()
    {
        GameEngine engine = RequireActiveEngine();
        ReleaseTokenIfActive(engine, ref _settingsPauseToken, "System settings pause token left the stack.");
    }

    public void OpenMenuPause()
    {
        GameEngine engine = RequireActiveEngine();
        AcquirePauseIfMissing(
            engine,
            ref _menuPauseToken,
            MenuPauseOwner,
            "interface menu pause",
            "Menu pause token entered the stack.");
    }

    public void CloseMenuPause()
    {
        GameEngine engine = RequireActiveEngine();
        ReleaseTokenIfActive(engine, ref _menuPauseToken, "Menu pause token left the stack.");
    }

    public void BeginSkillIndicatorPause()
    {
        GameEngine engine = RequireActiveEngine();
        StopHeroLocalBurst(engine, restorePhysicsVelocity: true);
        AcquirePauseIfMissing(
            engine,
            ref _skillIndicatorPauseToken,
            SkillIndicatorPauseOwner,
            "skill target indicator pause",
            "Skill indicator pause token entered the stack.");
    }

    public void EndSkillIndicatorPause()
    {
        GameEngine engine = RequireActiveEngine();
        ReleaseTokenIfActive(engine, ref _skillIndicatorPauseToken, "Skill indicator pause token left the stack.");
    }

    public void ShowSystemGuideDuringSkillIndicator()
    {
        GameEngine engine = RequireActiveEngine();
        AcquirePauseIfMissing(
            engine,
            ref _skillIndicatorPauseToken,
            SkillIndicatorPauseOwner,
            "skill target indicator pause",
            string.Empty);
        AcquirePauseIfMissing(
            engine,
            ref _systemGuidePauseToken,
            SystemGuidePauseOwner,
            "system guide pause while skill indicator is open",
            string.Empty);
        _lastEvent = "Skill indicator pause is still active, and a system guide pause stacked above it.";
    }

    public void DismissSystemGuidePause()
    {
        GameEngine engine = RequireActiveEngine();
        ReleaseTokenIfActive(engine, ref _systemGuidePauseToken, "System guide pause token left the stack.");
    }

    public void ReleaseAllPauseTokens()
    {
        GameEngine engine = RequireActiveEngine();
        ReleaseTokenIfActive(engine, ref _systemGuidePauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _skillIndicatorPauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _menuPauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _settingsPauseToken, string.Empty);
        _lastEvent = "All showcase pause tokens left the stack.";
    }

    public void ShowRunningMoment()
    {
        GameEngine engine = RequireActiveEngine();
        ReleaseTokenIfActive(engine, ref _systemGuidePauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _skillIndicatorPauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _menuPauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _settingsPauseToken, string.Empty);
        _lastEvent = "World running. Speed requests stay active until cleared.";
    }

    public void ShowSettingsMoment()
    {
        GameEngine engine = RequireActiveEngine();
        ReleaseTokenIfActive(engine, ref _systemGuidePauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _skillIndicatorPauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _menuPauseToken, string.Empty);
        AcquirePauseIfMissing(
            engine,
            ref _settingsPauseToken,
            SettingsPauseOwner,
            "system settings page pause",
            "Settings opened. A system pause request is now blocking world time.");
    }

    public void ShowMenuMoment()
    {
        GameEngine engine = RequireActiveEngine();
        ReleaseTokenIfActive(engine, ref _systemGuidePauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _skillIndicatorPauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _settingsPauseToken, string.Empty);
        AcquirePauseIfMissing(
            engine,
            ref _menuPauseToken,
            MenuPauseOwner,
            "interface menu pause",
            "Battle menu opened. A UI pause request is now blocking world time.");
    }

    public void ShowSkillAimMoment()
    {
        GameEngine engine = RequireActiveEngine();
        StopHeroLocalBurst(engine, restorePhysicsVelocity: true);
        ReleaseTokenIfActive(engine, ref _systemGuidePauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _menuPauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _settingsPauseToken, string.Empty);
        AcquirePauseIfMissing(
            engine,
            ref _skillIndicatorPauseToken,
            SkillIndicatorPauseOwner,
            "skill target indicator pause",
            "Skill indicator opened. Navigation, physics, and skill clocks pause together.");
    }

    public void ShowGuideDuringSkillMoment()
    {
        GameEngine engine = RequireActiveEngine();
        ReleaseTokenIfActive(engine, ref _menuPauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _settingsPauseToken, string.Empty);
        AcquirePauseIfMissing(
            engine,
            ref _skillIndicatorPauseToken,
            SkillIndicatorPauseOwner,
            "skill target indicator pause",
            string.Empty);
        AcquirePauseIfMissing(
            engine,
            ref _systemGuidePauseToken,
            SystemGuidePauseOwner,
            "system guide pause while skill indicator is open",
            string.Empty);
        _lastEvent = "System guide opened while aiming. Two pause requests now stack together.";
    }

    public void CastHeroSkill()
    {
        GameEngine engine = RequireActiveEngine();
        GasClocks gasClocks = engine.GetService(CoreServiceKeys.GasClocks)
            ?? throw new InvalidOperationException("TimeFlow showcase hero skill cast requires GasClocks.");
        ReleaseTokenIfActive(engine, ref _systemGuidePauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _skillIndicatorPauseToken, string.Empty);
        _heroSkillCastCount++;
        _lastHeroSkillCastGasStep = gasClocks.StepNow;
        StartHeroLocalBurst(engine);
        _lastEvent = "Hero cast Time Rift. Aim pause cleared; the hero local clock keeps attacking while the world actors hold.";
    }

    public void CancelHeroSkillAim()
    {
        GameEngine engine = RequireActiveEngine();
        ReleaseTokenIfActive(engine, ref _systemGuidePauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _skillIndicatorPauseToken, string.Empty);
        _lastEvent = "Hero canceled aiming. Aim pause cleared.";
    }

    public void AcquireSimulationPause()
    {
        OpenSettingsPause();
    }

    public void ReleaseSimulationPause()
    {
        CloseSettingsPause();
    }

    public void ApplySimulationScale(int requestIndex)
    {
        ApplySimulationScaleLayerOne(requestIndex);
    }

    public void ApplySimulationScaleLayerOne(int requestIndex)
    {
        CapabilityStandardTimeFlowShowcaseConfig config = ActiveConfig;
        if ((uint)requestIndex >= (uint)config.SimulationScaleRequests.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(requestIndex));
        }

        ApplyScale(
            TimeFlowDomainIds.Simulation,
            config.SimulationScaleRequests[requestIndex],
            ref _simulationScaleLayerOneToken,
            ref _simulationScaleLayerOnePermille,
            "simulation scale layer one");
    }

    public void ApplySimulationScaleLayerTwo(int requestIndex)
    {
        CapabilityStandardTimeFlowShowcaseConfig config = ActiveConfig;
        if ((uint)requestIndex >= (uint)config.SimulationScaleRequests.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(requestIndex));
        }

        ApplyScale(
            TimeFlowDomainIds.Simulation,
            config.SimulationScaleRequests[requestIndex],
            ref _simulationScaleLayerTwoToken,
            ref _simulationScaleLayerTwoPermille,
            "simulation scale layer two");
    }

    public void ReleaseSimulationScale()
    {
        ReleaseSimulationScaleLayerOne();
    }

    public void ReleaseSimulationScaleLayerOne()
    {
        GameEngine engine = RequireActiveEngine();
        ReleaseTokenIfActive(engine, ref _simulationScaleLayerOneToken, ref _simulationScaleLayerOnePermille, "Simulation scale layer one token left the stack.");
    }

    public void ReleaseSimulationScaleLayerTwo()
    {
        GameEngine engine = RequireActiveEngine();
        ReleaseTokenIfActive(engine, ref _simulationScaleLayerTwoToken, ref _simulationScaleLayerTwoPermille, "Simulation scale layer two token left the stack.");
    }

    public void ApplyGasScale(int requestIndex)
    {
        CapabilityStandardTimeFlowShowcaseConfig config = ActiveConfig;
        if ((uint)requestIndex >= (uint)config.GasScaleRequests.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(requestIndex));
        }

        ApplyScale(
            TimeFlowDomainIds.Gas,
            config.GasScaleRequests[requestIndex],
            ref _gasScaleToken,
            ref _gasScalePermille,
            "GAS");
    }

    public void ReleaseGasScale()
    {
        GameEngine engine = RequireActiveEngine();
        ReleaseTokenIfActive(engine, ref _gasScaleToken, ref _gasScalePermille, "GAS scale token left the stack.");
    }

    public void ResetProbes()
    {
        GameEngine engine = RequireActiveEngine();
        StopHeroLocalBurst(engine, restorePhysicsVelocity: false);
        engine.World.Destroy(in _ownedEntityQuery);
        _navigationFlow = null;
        _navigationStepper = null;
        _navigationProbeEntity = Entity.Null;
        _physicsProbeEntity = Entity.Null;
        _navigationStepCount = 0;
        _heroLocalClockSeconds = 0f;
        _heroLocalBurstTick = 0;
        _heroComboHitCount = 0;
        EnsurePhysicsProbe(engine, ActiveConfig);
        EnsureNavigationProbe(engine, ActiveConfig);
        _lastEvent = "Reset nav and Physics2D probes.";
    }

    public void ResetCamera()
    {
        GameEngine engine = RequireActiveEngine();
        var session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("TimeFlow showcase camera reset requires an active map session.");
        var cameraConfig = session.MapConfig?.DefaultCamera
            ?? throw new InvalidOperationException("TimeFlow showcase camera reset requires map DefaultCamera.");
        if (string.IsNullOrWhiteSpace(cameraConfig.VirtualCameraId))
        {
            throw new InvalidOperationException("TimeFlow showcase camera reset requires explicit DefaultCamera.VirtualCameraId.");
        }

        if (cameraConfig.TargetXCm is not float targetX ||
            cameraConfig.TargetYCm is not float targetY ||
            cameraConfig.Yaw is not float yaw ||
            cameraConfig.Pitch is not float pitch ||
            cameraConfig.DistanceCm is not float distanceCm ||
            cameraConfig.FovYDeg is not float fovYDeg)
        {
            throw new InvalidOperationException("TimeFlow showcase camera reset requires explicit target, yaw, pitch, distance, and fov.");
        }

        var registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry)
            ?? throw new InvalidOperationException("TimeFlow showcase camera reset requires VirtualCameraRegistry.");
        if (!registry.TryGet(cameraConfig.VirtualCameraId, out var definition) || definition == null)
        {
            throw new InvalidOperationException($"TimeFlow showcase camera '{cameraConfig.VirtualCameraId}' is not registered.");
        }

        ClientLocalSeatAccess.ResolveAuthorityCamera(engine).ResetVirtualCameras();
        ClientLocalSeatAccess.ResolveAuthorityCamera(engine).ActivateVirtualCamera(
            cameraConfig.VirtualCameraId,
            blendDurationSeconds: 0f,
            followTarget: CameraFollowTargetFactory.Build(
                engine.World,
                engine.GlobalContext,
                definition.FollowTargetKind,
                Entity.Null,
                definition.FollowCollectionKey),
            snapToFollowTargetWhenAvailable: definition.SnapToFollowTargetWhenAvailable,
            resetRuntimeState: true);
        ClientLocalSeatAccess.ResolveAuthorityCamera(engine).ApplyPose(new CameraPoseRequest
        {
            VirtualCameraId = cameraConfig.VirtualCameraId,
            TargetCm = new Vector2(targetX, targetY),
            Yaw = yaw,
            Pitch = pitch,
            DistanceCm = distanceCm,
            FovYDeg = fovYDeg
        });
        ClientLocalSeatAccess.ResolveAuthorityCamera(engine).SynchronizeActiveVirtualCameraBoundsAndHeight();
        _lastEvent = "Camera reset to the TimeFlow showcase view.";
    }

    private void StartHeroLocalBurst(GameEngine engine)
    {
        StopHeroLocalBurst(engine, restorePhysicsVelocity: true);
        ReadNavigationProbe(out _heroBurstStartXCm, out _heroBurstStartYCm);
        _heroBurstEnemyXCm = ActiveConfig.NavigationProbe.TargetXCm;
        _heroBurstEnemyYCm = ActiveConfig.NavigationProbe.TargetYCm;
        _heroLocalPositionXCm = _heroBurstStartXCm;
        _heroLocalPositionYCm = _heroBurstStartYCm;
        _heroLocalClockSeconds = 0f;
        _heroLocalBurstTick = 0;
        _heroComboHitCount = 0;
        _heroLocalBurstActive = true;
        CapturePhysicsProbeForHeroBurst(engine.World);
    }

    private void AdvanceHeroLocalBurst(GameEngine engine, float dt)
    {
        TimeFlowSkillDemoConfig skillDemo = ActiveConfig.SkillDemo;
        _heroLocalClockSeconds = Math.Min(
            skillDemo.LocalBurstDurationSeconds,
            _heroLocalClockSeconds + dt);
        _heroLocalBurstTick++;

        int maxHits = Math.Max(1, (int)MathF.Ceiling(skillDemo.LocalBurstDurationSeconds / skillDemo.LocalHitIntervalSeconds));
        int visibleHits = Math.Min(maxHits, 1 + (int)MathF.Floor(_heroLocalClockSeconds / skillDemo.LocalHitIntervalSeconds));
        _heroComboHitCount = Math.Max(_heroComboHitCount, visibleHits);

        UpdateHeroLocalPosition();
        ApplyHeroBurstPhysicsFreeze(engine.World);

        if (_heroLocalClockSeconds >= skillDemo.LocalBurstDurationSeconds)
        {
            StopHeroLocalBurst(engine, restorePhysicsVelocity: true);
            _lastEvent = "Hero local burst finished. MassNav and Physics2D probes resumed on the shared clock.";
        }
    }

    private void StopHeroLocalBurst(GameEngine engine, bool restorePhysicsVelocity)
    {
        if (!_heroLocalBurstActive && !_physicsFrozenForHeroBurst)
        {
            return;
        }

        _heroLocalBurstActive = false;
        if (restorePhysicsVelocity)
        {
            RestorePhysicsProbeAfterHeroBurst(engine.World);
        }
        else
        {
            _physicsFrozenForHeroBurst = false;
        }
    }

    private void CapturePhysicsProbeForHeroBurst(World world)
    {
        if (_physicsProbeEntity == Entity.Null || !world.IsAlive(_physicsProbeEntity))
        {
            throw new InvalidOperationException("TimeFlow showcase hero burst requires the Physics2D probe entity.");
        }

        Position2D position = world.Get<Position2D>(_physicsProbeEntity);
        Velocity2D velocity = world.Get<Velocity2D>(_physicsProbeEntity);
        _heroBurstPhysicsPosition = position.Value;
        _heroBurstPhysicsVelocity = velocity.Linear;
        _heroBurstPhysicsAngularVelocity = velocity.Angular;
        _physicsFrozenForHeroBurst = true;
        ApplyHeroBurstPhysicsFreeze(world);
    }

    private void ApplyHeroBurstPhysicsFreeze(World world)
    {
        if (!_physicsFrozenForHeroBurst ||
            _physicsProbeEntity == Entity.Null ||
            !world.IsAlive(_physicsProbeEntity))
        {
            return;
        }

        world.Set(_physicsProbeEntity, new Position2D { Value = _heroBurstPhysicsPosition });
        world.Set(_physicsProbeEntity, new PreviousPosition2D { Value = _heroBurstPhysicsPosition });
        world.Set(_physicsProbeEntity, new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero });
        if (world.Has<WorldPositionCm>(_physicsProbeEntity))
        {
            world.Set(_physicsProbeEntity, new WorldPositionCm { Value = _heroBurstPhysicsPosition });
        }

        if (world.Has<PreviousWorldPositionCm>(_physicsProbeEntity))
        {
            world.Set(_physicsProbeEntity, new PreviousWorldPositionCm { Value = _heroBurstPhysicsPosition });
        }

        if (world.Has<VisualTransform>(_physicsProbeEntity))
        {
            VisualTransform visual = world.Get<VisualTransform>(_physicsProbeEntity);
            Vector2 position = _heroBurstPhysicsPosition.ToVector2();
            visual.Position = new Vector3(position.X * 0.01f, 0f, position.Y * 0.01f);
            world.Set(_physicsProbeEntity, visual);
        }
    }

    private void RestorePhysicsProbeAfterHeroBurst(World world)
    {
        if (!_physicsFrozenForHeroBurst)
        {
            return;
        }

        if (_physicsProbeEntity != Entity.Null && world.IsAlive(_physicsProbeEntity))
        {
            world.Set(_physicsProbeEntity, new Velocity2D
            {
                Linear = _heroBurstPhysicsVelocity,
                Angular = _heroBurstPhysicsAngularVelocity
            });
        }

        _physicsFrozenForHeroBurst = false;
    }

    private void UpdateHeroLocalPosition()
    {
        TimeFlowSkillDemoConfig skillDemo = ActiveConfig.SkillDemo;
        float progress01 = Math.Clamp(_heroLocalClockSeconds / skillDemo.LocalBurstDurationSeconds, 0f, 1f);
        if (progress01 < 0.22f)
        {
            float leap01 = progress01 / 0.22f;
            _heroLocalPositionXCm = Lerp(_heroBurstStartXCm, _heroBurstEnemyXCm, leap01);
            _heroLocalPositionYCm = Lerp(_heroBurstStartYCm, _heroBurstEnemyYCm, leap01);
            return;
        }

        float angle = (_heroLocalClockSeconds * 8.5f) + (_heroComboHitCount * 0.9f);
        _heroLocalPositionXCm = _heroBurstEnemyXCm + (MathF.Cos(angle) * skillDemo.LocalHeroOrbitRadiusCm);
        _heroLocalPositionYCm = _heroBurstEnemyYCm + (MathF.Sin(angle) * skillDemo.LocalHeroOrbitRadiusCm);
    }

    private void ResolveHeroLocalPosition(float navX, float navY, out float heroX, out float heroY)
    {
        if (_heroLocalBurstActive)
        {
            heroX = _heroLocalPositionXCm;
            heroY = _heroLocalPositionYCm;
            return;
        }

        heroX = navX;
        heroY = navY;
    }

    private void CloseTopmostPlayerLayer()
    {
        if (_systemGuidePauseToken.IsValid)
        {
            DismissSystemGuidePause();
            return;
        }

        if (_menuPauseToken.IsValid)
        {
            CloseMenuPause();
            return;
        }

        if (_settingsPauseToken.IsValid)
        {
            CloseSettingsPause();
            return;
        }

        if (_skillIndicatorPauseToken.IsValid)
        {
            CancelHeroSkillAim();
            return;
        }

        _lastEvent = "No open player layer to close.";
    }

    private void TriggerSkillShortcut()
    {
        if (_settingsPauseToken.IsValid || _menuPauseToken.IsValid)
        {
            _lastEvent = "Close the open interface before casting.";
            return;
        }

        if (_skillIndicatorPauseToken.IsValid || _systemGuidePauseToken.IsValid)
        {
            CastHeroSkill();
            return;
        }

        ShowSkillAimMoment();
    }

    private void ToggleGuideShortcut()
    {
        if (!_skillIndicatorPauseToken.IsValid && !_systemGuidePauseToken.IsValid)
        {
            _lastEvent = "Open the skill indicator before showing the system guide.";
            return;
        }

        if (_systemGuidePauseToken.IsValid)
        {
            DismissSystemGuidePause();
            return;
        }

        ShowGuideDuringSkillMoment();
    }

    private static bool ConsumeShortcut(IInputBackend input, string path, ref bool wasDown)
    {
        bool isDown = input.GetButton(path);
        bool pressed = isDown && !wasDown;
        wasDown = isDown;
        return pressed;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * Math.Clamp(t, 0f, 1f));
    }

    private void AcquirePauseIfMissing(
        GameEngine engine,
        ref TimeFlowToken token,
        string owner,
        string reason,
        string eventText)
    {
        if (token.IsValid)
        {
            if (!string.IsNullOrWhiteSpace(eventText))
            {
                _lastEvent = "Pause token is already active.";
            }

            return;
        }

        token = RequireTimeFlow(engine).AcquirePauseToken(TimeFlowDomainIds.Simulation, owner, reason);
        if (!string.IsNullOrWhiteSpace(eventText))
        {
            _lastEvent = eventText;
        }
    }

    private void ApplyScale(
        string domainName,
        TimeFlowScaleRequestConfig request,
        ref TimeFlowToken token,
        ref int activeScalePermille,
        string label)
    {
        GameEngine engine = RequireActiveEngine();
        if (token.IsValid)
        {
            RequireTimeFlow(engine).ReleaseToken(token);
            token = TimeFlowToken.Invalid;
            activeScalePermille = 0;
        }

        token = RequireTimeFlow(engine).AcquireScaleToken(
            domainName,
            request.ScalePermille,
            TokenOwner,
            $"TimeFlow showcase {label} scale {request.ScalePermille}");
        activeScalePermille = request.ScalePermille;
        _lastEvent = $"{label} scale token {request.Label} entered the stack.";
    }

    private void EnsurePhysicsProbe(GameEngine engine, CapabilityStandardTimeFlowShowcaseConfig config)
    {
        if (_physicsProbeEntity != Entity.Null && engine.World.IsAlive(_physicsProbeEntity))
        {
            return;
        }

        ShapeDataStorage2D shapeStorage = engine.GetService(CoreServiceKeys.Physics2DShapeStorage) as ShapeDataStorage2D
            ?? throw new InvalidOperationException("TimeFlow showcase requires Physics2D shape storage.");
        if (_physicsShapeIndex < 0)
        {
            _physicsShapeIndex = shapeStorage.RegisterCircle(Fix64.FromInt(config.PhysicsProbe.RadiusCm));
        }

        Fix64Vec2 position = Fix64Vec2.FromInt(config.PhysicsProbe.StartXCm, config.PhysicsProbe.StartYCm);
        Fix64Vec2 velocity = Fix64Vec2.FromFloat(
            config.PhysicsProbe.VelocityXCmPerSecond,
            config.PhysicsProbe.VelocityYCmPerSecond);
        _physicsProbeEntity = engine.World.Create(
            new CapabilityStandardTimeFlowShowcaseEntityTag(),
            new CapabilityStandardTimeFlowShowcasePhysicsProbeTag(),
            new Position2D { Value = position },
            new PreviousPosition2D { Value = position },
            new Velocity2D { Linear = velocity, Angular = Fix64.Zero },
            Mass2D.FromFloat(config.PhysicsProbe.InverseMass, config.PhysicsProbe.InverseInertia),
            new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = _physicsShapeIndex },
            new PhysicsMaterial2D
            {
                Friction = Fix64.FromFloat(config.PhysicsProbe.Friction),
                Restitution = Fix64.FromFloat(config.PhysicsProbe.Restitution),
                BaseDamping = Fix64.FromFloat(config.PhysicsProbe.BaseDamping)
            },
            new WorldPositionCm { Value = position },
            new PreviousWorldPositionCm { Value = position },
            new VisualTransform
            {
                Position = new Vector3(config.PhysicsProbe.StartXCm * 0.01f, 0f, config.PhysicsProbe.StartYCm * 0.01f),
                Rotation = Quaternion.Identity,
                Scale = new Vector3(0.45f, 0.45f, 0.45f)
            },
            PresentationLocalBounds.Create(Vector3.Zero, new Vector3(0.35f, 0.35f, 0.35f)),
            new CullState { IsVisible = true, LOD = LODLevel.High, DistanceToCameraSq = 0f });
    }

    private void EnsureNavigationProbe(GameEngine engine, CapabilityStandardTimeFlowShowcaseConfig config)
    {
        if (_navigationFlow != null && _navigationStepper != null)
        {
            return;
        }

        var flow = new MassNavigationFlowSolverState(config.NavigationSolver);
        flow.ArrivalTuning.CopyFrom(config.NavigationArrival);
        flow.AvoidanceTuning.CopyFrom(config.NavigationAvoidance);
        config.NavigationSemantics.ApplyTo(flow.Semantics);
        flow.SetWorldOrigin(0f, 0f);
        flow.SetWorldBounds(0f, config.NavigationSolver.FieldWidthCm, 0f, config.NavigationSolver.FieldHeightCm);

        var seed = new MassNavigationAgentSeed(
            config.NavigationProbe.TeamId,
            config.NavigationProbe.StartXCm,
            config.NavigationProbe.StartYCm,
            heavy: false,
            config.NavigationProbe.NavMass,
            config.NavigationProbe.BodyRadiusCm,
            config.NavigationProbe.SpeedCmPerSecond,
            new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u));
        flow.ResetAuthoredAgents(new[] { seed });
        if (!flow.SetUnitTarget(
                0,
                config.NavigationProbe.TargetXCm,
                config.NavigationProbe.TargetYCm,
                config.NavigationSemantics.UnitTargetStopThresholdCm,
                resetRecovery: true))
        {
            throw new InvalidOperationException("TimeFlow showcase failed to initialize navigation probe target.");
        }

        _navigationFlow = flow;
        _navigationStepper = new MassNavigationFlowStepper(flow, config.NavigationRuntimeCapacity);
        _navigationProbeEntity = engine.World.Create(
            new CapabilityStandardTimeFlowShowcaseEntityTag(),
            new CapabilityStandardTimeFlowShowcaseNavigationProbeTag(),
            WorldPositionCm.FromCmFloat(config.NavigationProbe.StartXCm, config.NavigationProbe.StartYCm),
            new PreviousWorldPositionCm { Value = Fix64Vec2.FromFloat(config.NavigationProbe.StartXCm, config.NavigationProbe.StartYCm) },
            new VisualTransform
            {
                Position = new Vector3(config.NavigationProbe.StartXCm * 0.01f, 0f, config.NavigationProbe.StartYCm * 0.01f),
                Rotation = Quaternion.Identity,
                Scale = new Vector3(0.35f, 0.35f, 0.35f)
            },
            PresentationLocalBounds.Create(Vector3.Zero, new Vector3(0.30f, 0.30f, 0.30f)),
            new CullState { IsVisible = true, LOD = LODLevel.High, DistanceToCameraSq = 0f });
    }

    private void SyncNavigationEntity(World world)
    {
        if (_navigationFlow == null || _navigationProbeEntity == Entity.Null || !world.IsAlive(_navigationProbeEntity))
        {
            throw new InvalidOperationException("TimeFlow showcase navigation probe entity is not available for sync.");
        }

        float x = _navigationFlow.GetPositionX(0);
        float y = _navigationFlow.GetPositionY(0);
        var position = Fix64Vec2.FromFloat(x, y);
        world.Set(_navigationProbeEntity, new WorldPositionCm { Value = position });
        if (world.Has<VisualTransform>(_navigationProbeEntity))
        {
            VisualTransform visual = world.Get<VisualTransform>(_navigationProbeEntity);
            visual.Position = new Vector3(x * 0.01f, 0f, y * 0.01f);
            world.Set(_navigationProbeEntity, visual);
        }
    }

    private void ReadNavigationProbe(out float x, out float y)
    {
        MassNavigationFlowSolverState flow = _navigationFlow
            ?? throw new InvalidOperationException("TimeFlow showcase navigation probe is not initialized.");
        x = flow.GetPositionX(0);
        y = flow.GetPositionY(0);
    }

    private void ReadPhysicsProbe(
        World world,
        out float x,
        out float y,
        out float vx,
        out float vy)
    {
        bool found = false;
        float localX = 0f;
        float localY = 0f;
        float localVx = 0f;
        float localVy = 0f;
        world.Query(in _physicsProbeQuery, (ref Position2D position, ref Velocity2D velocity) =>
        {
            if (found)
            {
                return;
            }

            Vector2 pos = position.Value.ToVector2();
            Vector2 vel = velocity.Linear.ToVector2();
            localX = pos.X;
            localY = pos.Y;
            localVx = vel.X;
            localVy = vel.Y;
            found = true;
        });

        if (!found)
        {
            throw new InvalidOperationException("TimeFlow showcase physics probe is not initialized.");
        }

        x = localX;
        y = localY;
        vx = localVx;
        vy = localVy;
    }

    private CapabilityStandardTimeFlowShowcaseConfig EnsureConfig(GameEngine engine)
    {
        if (_config != null)
        {
            return _config;
        }

        if (engine.ConfigPipeline == null)
        {
            throw new InvalidOperationException("TimeFlow showcase requires ConfigPipeline before loading config.");
        }

        _config = new CapabilityStandardTimeFlowShowcaseConfigLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog,
            engine.ConfigConflictReport);
        return _config;
    }

    private GameEngine RequireActiveEngine()
    {
        return _activeEngine ?? throw new InvalidOperationException("TimeFlow showcase requires an active engine.");
    }

    private static TimeFlowService RequireTimeFlow(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.TimeFlow)
            ?? throw new InvalidOperationException("TimeFlow showcase requires TimeFlow service.");
    }

    private bool IsShowcaseMap(string? mapId)
    {
        return _config != null && string.Equals(mapId, _config.MapId, StringComparison.Ordinal);
    }

    private void Disable(GameEngine engine)
    {
        ReleaseTokenIfActive(engine, ref _systemGuidePauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _skillIndicatorPauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _menuPauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _settingsPauseToken, string.Empty);
        ReleaseTokenIfActive(engine, ref _simulationScaleLayerOneToken, ref _simulationScaleLayerOnePermille, string.Empty);
        ReleaseTokenIfActive(engine, ref _simulationScaleLayerTwoToken, ref _simulationScaleLayerTwoPermille, string.Empty);
        ReleaseTokenIfActive(engine, ref _gasScaleToken, ref _gasScalePermille, string.Empty);
        StopHeroLocalBurst(engine, restorePhysicsVelocity: false);
        engine.World.Destroy(in _ownedEntityQuery);
        _activeEngine = null;
        _navigationProbeEntity = Entity.Null;
        _physicsProbeEntity = Entity.Null;
        _navigationFlow = null;
        _navigationStepper = null;
        _navigationStepCount = 0;
        _heroSkillCastCount = 0;
        _lastHeroSkillCastGasStep = -1;
        _heroLocalClockSeconds = 0f;
        _heroLocalBurstTick = 0;
        _heroComboHitCount = 0;
    }

    private void ReleaseTokenIfActive(GameEngine engine, ref TimeFlowToken token, string action)
    {
        if (!token.IsValid)
        {
            if (!string.IsNullOrWhiteSpace(action))
            {
                _lastEvent = "No active token to release.";
            }

            return;
        }

        RequireTimeFlow(engine).ReleaseToken(token);
        token = TimeFlowToken.Invalid;
        if (!string.IsNullOrWhiteSpace(action))
        {
            _lastEvent = action;
        }
    }

    private void ReleaseTokenIfActive(GameEngine engine, ref TimeFlowToken token, ref int activeScalePermille, string eventText)
    {
        ReleaseTokenIfActive(engine, ref token, eventText);
        if (!token.IsValid)
        {
            activeScalePermille = 0;
        }
    }

    private static int CountTokens(TimeFlowSnapshot snapshot, string kind)
    {
        int count = 0;
        for (int i = 0; i < snapshot.ActiveTokens.Count; i++)
        {
            if (string.Equals(snapshot.ActiveTokens[i].Kind, kind, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static string BuildTokenSummary(TimeFlowSnapshot snapshot, string? kind)
    {
        if (snapshot.ActiveTokens.Count <= 0)
        {
            return "none";
        }

        string[] parts = new string[Math.Min(snapshot.ActiveTokens.Count, 6)];
        int written = 0;
        for (int i = 0; i < snapshot.ActiveTokens.Count && written < parts.Length; i++)
        {
            TimeFlowTokenSnapshot token = snapshot.ActiveTokens[i];
            if (kind != null && !string.Equals(token.Kind, kind, StringComparison.Ordinal))
            {
                continue;
            }

            string reason = string.IsNullOrWhiteSpace(token.Reason) ? token.Owner : token.Reason;
            parts[written] = token.Kind == "Pause"
                ? $"{written + 1}. {reason}"
                : $"{written + 1}. {reason} ({token.ScalePermille})";
            written++;
        }

        if (written <= 0)
        {
            return "none";
        }

        if (written < parts.Length)
        {
            Array.Resize(ref parts, written);
        }

        return string.Join(" | ", parts);
    }

    private static void ThrowScatterUnsupported()
    {
        throw new InvalidOperationException("TimeFlow showcase does not support scatter controls.");
    }
}
