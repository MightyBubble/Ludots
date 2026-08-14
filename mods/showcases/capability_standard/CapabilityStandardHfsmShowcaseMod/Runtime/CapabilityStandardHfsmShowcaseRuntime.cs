using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardHfsmShowcaseMod.Runtime;

public sealed class CapabilityStandardHfsmShowcaseRuntime : IInputFrameConsumer, IBenchmarkSceneController
{
    public const string RuntimeKey = "CapabilityStandardHfsmShowcase.Runtime";
    public const string StateAlive = "Alive";
    public const string StateHydrate = "Hydrate";
    public const string StateExercise = "Exercise";
    public const string StateGoDrink = "GoDrink";
    public const string StateDrinking = "Drinking";
    public const string StateGoTrack = "GoTrack";
    public const string StateRunning = "Running";
    public const string StateDead = "Dead";

    private const string ModId = "CapabilityStandardHfsmShowcaseMod";
    private const string ConfigUri = "assets/HfsmShowcase/showcase.json";
    private const int PanelX = 18;
    private const int PanelY = 18;
    private const int PanelW = 470;
    private const int PanelH = 330;

    private static readonly Vector4 PanelFill = new(0.035f, 0.045f, 0.052f, 0.76f);
    private static readonly Vector4 PanelBorder = new(0.58f, 0.72f, 0.50f, 0.96f);
    private static readonly Vector4 Text = new(0.86f, 0.90f, 0.84f, 1f);
    private static readonly Vector4 Muted = new(0.55f, 0.62f, 0.58f, 1f);
    private static readonly Vector4 Accent = new(0.95f, 0.74f, 0.30f, 1f);
    private static readonly Vector4 Water = new(0.22f, 0.68f, 0.94f, 1f);
    private static readonly Vector4 Health = new(0.95f, 0.26f, 0.24f, 1f);
    private static readonly Vector4 AliveFill = new(0.11f, 0.18f, 0.13f, 0.82f);
    private static readonly Vector4 ActiveFill = new(0.28f, 0.22f, 0.09f, 0.88f);
    private static readonly Vector4 DeadFill = new(0.30f, 0.08f, 0.08f, 0.88f);
    private static readonly Vector4 WorldLabelFill = new(0.02f, 0.025f, 0.025f, 0.86f);
    private static readonly Vector4 TrackLine = new(0.96f, 0.76f, 0.33f, 0.72f);

    private CapabilityStandardHfsmShowcaseConfig? _config;
    private GameEngine? _engine;
    private Entity _hero = Entity.Null;
    private Entity _waterStation = Entity.Null;
    private Entity _trackCenterEntity = Entity.Null;
    private string _state = StateGoDrink;
    private string _lastEvent = "HFSM showcase waiting for map.";
    private float _health;
    private float _water;
    private float _trackAngleRad;
    private int _lapCount;
    private int _transitionCount;
    private int _frame;
    private bool _fatalShortcutDown;
    private bool _thirstShortcutDown;
    private bool _resetShortcutDown;

    public bool IsActive { get; private set; }
    public bool SupportsScatterControl => false;
    public bool IsCleanPerformanceScene => false;
    public bool SuppressHostDiagnosticUi => IsActive;
    public bool SuppressHostDebugGuides => IsActive;
    public bool BrowserGraphDebugViewActive { get; set; }
    public int ScatterMin => 0;
    public int ScatterMax => 0;
    public int ScatterTarget => 0;
    public int ScatterAppliedTotal => 0;
    public CapabilityStandardHfsmShowcaseSnapshot Snapshot { get; private set; } = CapabilityStandardHfsmShowcaseSnapshot.Inactive;
    internal CapabilityStandardHfsmShowcaseConfig ActiveConfig => _config
        ?? throw new InvalidOperationException("HFSM showcase config has not been loaded.");

    internal CapabilityStandardHfsmShowcaseConfig EnsureConfigForGraphDebug(GameEngine engine)
    {
        return EnsureConfig(engine);
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        CapabilityStandardHfsmShowcaseConfig config = EnsureConfig(engine);
        string? mapId = engine.CurrentMapSession?.MapConfig?.Id;
        if (!string.Equals(mapId, config.MapId, StringComparison.Ordinal))
        {
            Disable();
            return Task.CompletedTask;
        }

        _engine = engine;
        BindMapEntities(engine, config);
        ResetStory();
        IsActive = true;
        engine.GlobalContext[RuntimeKey] = this;
        WriteSnapshot();
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        Disable();
        return Task.CompletedTask;
    }

    public void Consume(GameEngine engine, PlayerInputHandler input, float deltaTime)
    {
        if (!IsActive || BrowserGraphDebugViewActive)
        {
            return;
        }

        if (!engine.TryGetService(CoreServiceKeys.InputBackend, out IInputBackend backend))
        {
            throw new InvalidOperationException("HFSM showcase shortcuts require InputBackend.");
        }

        HfsmShortcutConfig shortcuts = ActiveConfig.Shortcuts;
        if (ConsumeShortcut(backend, shortcuts.FatalDamage, ref _fatalShortcutDown))
        {
            ApplyFatalDamage();
        }

        if (ConsumeShortcut(backend, shortcuts.Thirst, ref _thirstShortcutDown))
        {
            MakeThirsty();
        }

        if (ConsumeShortcut(backend, shortcuts.Reset, ref _resetShortcutDown))
        {
            ResetStory();
        }
    }

    public void Update(float dt)
    {
        if (!IsActive)
        {
            return;
        }

        if (!float.IsFinite(dt) || dt < 0f)
        {
            throw new InvalidOperationException($"HFSM showcase received invalid dt '{dt.ToString(CultureInfo.InvariantCulture)}'.");
        }

        _frame++;
        if (_health <= 0f && !string.Equals(_state, StateDead, StringComparison.Ordinal))
        {
            EnterState(StateDead, "Any State fired: health reached zero, so the actor dies in place.");
        }

        switch (_state)
        {
            case StateGoDrink:
                TickGoDrink(dt);
                break;
            case StateDrinking:
                TickDrinking(dt);
                break;
            case StateGoTrack:
                TickGoTrack(dt);
                break;
            case StateRunning:
                TickRunning(dt);
                break;
            case StateDead:
                break;
            default:
                throw new InvalidOperationException($"HFSM showcase entered unsupported state '{_state}'.");
        }

        WriteSnapshot();
    }

    public void RenderOverlay(GameEngine engine)
    {
        if (!IsActive || BrowserGraphDebugViewActive)
        {
            return;
        }

        if (engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay)
        {
            throw new InvalidOperationException("HFSM showcase requires ScreenOverlayBuffer.");
        }

        CapabilityStandardHfsmShowcaseConfig config = ActiveConfig;
        overlay.AddRect(PanelX, PanelY, PanelW, PanelH, PanelFill, PanelBorder, 91000, _frame);
        overlay.AddText(PanelX + 16, PanelY + 14, config.Title, 20, Text, 91001, _frame);
        overlay.AddText(PanelX + 16, PanelY + 42, $"Active path: {Snapshot.StatePath}", 13, Accent, 91002, _frame);
        overlay.AddText(PanelX + 16, PanelY + 64, Snapshot.PlayerStory, 13, Text, 91003, _frame);
        DrawBar(overlay, PanelX + 16, PanelY + 96, 198, "Water", Snapshot.Water, (int)MathF.Round(config.MaxWater), Water, 91010);
        DrawBar(overlay, PanelX + 240, PanelY + 96, 198, "Health", Snapshot.Health, config.StartHealth, Health, 91020);

        DrawStateLine(overlay, 138, StateGoDrink, "water low -> go drink", 91030);
        DrawStateLine(overlay, 168, StateDrinking, "at water -> drink", 91040);
        DrawStateLine(overlay, 198, StateGoTrack, "water full -> leave station", 91050);
        DrawStateLine(overlay, 228, StateRunning, "on track -> run laps", 91060);
        DrawStateLine(overlay, 258, StateDead, "Any State: health zero -> dead", 91070);

        overlay.AddText(PanelX + 16, PanelY + 292, $"K kill | T thirst | R reset | laps {Snapshot.LapCount}", 13, Accent, 91080, _frame);
        overlay.AddText(PanelX + 16, PanelY + 312, Snapshot.LastEvent, 12, Muted, 91081, _frame);
        RenderWorldOverlay(engine, overlay);
    }

    public void ApplyFatalDamage()
    {
        if (!IsActive)
        {
            return;
        }

        _health = 0f;
        EnterState(StateDead, "Player pressed kill: Any State overrides the current child state.");
        WriteSnapshot();
    }

    public void MakeThirsty()
    {
        if (!IsActive || string.Equals(_state, StateDead, StringComparison.Ordinal))
        {
            return;
        }

        _water = MathF.Min(_water, MathF.Max(0f, ActiveConfig.LowWaterThreshold - 8f));
        _lastEvent = "Player lowered water: HFSM will return to Hydrate.";
        WriteSnapshot();
    }

    public void ResetStory()
    {
        CapabilityStandardHfsmShowcaseConfig config = ActiveConfig;
        _health = config.StartHealth;
        _water = config.StartWater;
        _trackAngleRad = DegreesToRadians(config.TrackEntryAngleDeg);
        _lapCount = 0;
        _transitionCount = 0;
        _state = StateGoDrink;
        _lastEvent = "Water starts low, so the actor enters Hydrate > Go Drink.";
        SetHeroPose(new Vector2(config.StartPosition.X, config.StartPosition.Y), forceFacing: true, facingRad: 0f);
        WriteSnapshot();
    }

    public void SetScatterTargetFromRatio(float ratio) { }
    public void ApplyScatterTarget() { }
    public void ApplyScatterLayout(int total) { }

    private CapabilityStandardHfsmShowcaseConfig EnsureConfig(GameEngine engine)
    {
        if (_config != null)
        {
            return _config;
        }

        using Stream stream = engine.VFS.GetStream($"{ModId}:{ConfigUri}");
        using JsonDocument document = JsonDocument.Parse(stream);
        _config = CapabilityStandardHfsmShowcaseConfig.Load(document.RootElement);
        return _config;
    }

    private void Disable()
    {
        IsActive = false;
        _engine = null;
        _hero = Entity.Null;
        _waterStation = Entity.Null;
        _trackCenterEntity = Entity.Null;
        Snapshot = CapabilityStandardHfsmShowcaseSnapshot.Inactive;
    }

    private void BindMapEntities(GameEngine engine, CapabilityStandardHfsmShowcaseConfig config)
    {
        _hero = ResolveRequiredEntity(engine, config.HeroInstanceId, mustBeMovable: true);
        _waterStation = ResolveRequiredEntity(engine, config.WaterStationInstanceId, mustBeMovable: false);
        _trackCenterEntity = ResolveRequiredEntity(engine, config.TrackCenterInstanceId, mustBeMovable: false);
    }

    private Entity ResolveRequiredEntity(GameEngine engine, string instanceId, bool mustBeMovable)
    {
        if (engine.CurrentMapSession == null)
        {
            throw new InvalidOperationException("HFSM showcase requires a focused map session before binding entities.");
        }

        if (!engine.CurrentMapSession.EntityIndex.TryGet(instanceId, out Entity entity))
        {
            throw new InvalidOperationException($"HFSM showcase could not resolve map entity '{instanceId}'.");
        }

        World world = engine.World;
        if (!world.IsAlive(entity) ||
            !world.Has<WorldPositionCm>(entity) ||
            !world.Has<FacingDirection>(entity))
        {
            throw new InvalidOperationException($"HFSM showcase entity '{instanceId}' requires WorldPositionCm and FacingDirection.");
        }

        if (mustBeMovable && (!world.Has<PreviousWorldPositionCm>(entity) || world.Has<PresentationStaticTransform>(entity)))
        {
            throw new InvalidOperationException($"HFSM showcase actor '{instanceId}' must be a movable presentation entity.");
        }

        return entity;
    }

    private void TickGoDrink(float dt)
    {
        CapabilityStandardHfsmShowcaseConfig config = ActiveConfig;
        Vector2 current = ReadHeroPosition();
        Vector2 waterPoint = new(config.WaterPoint.X, config.WaterPoint.Y);
        Vector2 next = MoveTowards(current, waterPoint, config.MoveSpeedCmPerSecond * dt);
        SetHeroPose(next, forceFacing: false, facingRad: 0f);
        if (Vector2.Distance(next, waterPoint) <= config.DrinkRadiusCm)
        {
            EnterState(StateDrinking, "Actor reached the water station and starts drinking.");
        }
    }

    private void TickDrinking(float dt)
    {
        CapabilityStandardHfsmShowcaseConfig config = ActiveConfig;
        _water = MathF.Min(config.MaxWater, _water + (config.DrinkWaterPerSecond * dt));
        if (_water >= config.DrinkCompleteThreshold)
        {
            EnterState(StateGoTrack, "Water is full enough, so the actor leaves for the track.");
        }
    }

    private void TickGoTrack(float dt)
    {
        CapabilityStandardHfsmShowcaseConfig config = ActiveConfig;
        Vector2 current = ReadHeroPosition();
        Vector2 entry = TrackPoint(_trackAngleRad);
        Vector2 next = MoveTowards(current, entry, config.MoveSpeedCmPerSecond * dt);
        SetHeroPose(next, forceFacing: false, facingRad: 0f);
        if (Vector2.Distance(next, entry) <= config.DrinkRadiusCm)
        {
            EnterState(StateRunning, "Actor reached the track and starts running laps.");
        }
    }

    private void TickRunning(float dt)
    {
        CapabilityStandardHfsmShowcaseConfig config = ActiveConfig;
        _water = MathF.Max(0f, _water - (config.RunWaterDrainPerSecond * dt));
        if (_water <= config.LowWaterThreshold)
        {
            EnterState(StateGoDrink, "Running drained water below the threshold, so HFSM returns to Hydrate.");
            return;
        }

        float oldAngle = _trackAngleRad;
        _trackAngleRad += DegreesToRadians(config.TrackAngularSpeedDegPerSecond) * dt;
        while (_trackAngleRad >= MathF.PI * 2f)
        {
            _trackAngleRad -= MathF.PI * 2f;
        }

        if (oldAngle > _trackAngleRad)
        {
            _lapCount++;
        }

        SetHeroPose(TrackPoint(_trackAngleRad), forceFacing: false, facingRad: 0f);
    }

    private void EnterState(string stateId, string eventText)
    {
        if (!string.Equals(_state, stateId, StringComparison.Ordinal))
        {
            _state = stateId;
            _transitionCount++;
        }

        _lastEvent = eventText;
    }

    private Vector2 TrackPoint(float angleRad)
    {
        CapabilityStandardHfsmShowcaseConfig config = ActiveConfig;
        return new Vector2(
            config.TrackCenter.X + (MathF.Cos(angleRad) * config.TrackRadiusCm),
            config.TrackCenter.Y + (MathF.Sin(angleRad) * config.TrackRadiusCm));
    }

    private Vector2 ReadHeroPosition()
    {
        World world = RequireEngine().World;
        if (!world.IsAlive(_hero) || !world.Has<WorldPositionCm>(_hero))
        {
            throw new InvalidOperationException("HFSM showcase actor must stay alive with WorldPositionCm.");
        }

        return world.Get<WorldPositionCm>(_hero).Value.ToVector2();
    }

    private void SetHeroPose(Vector2 next, bool forceFacing, float facingRad)
    {
        GameEngine engine = RequireEngine();
        World world = engine.World;
        if (!world.IsAlive(_hero))
        {
            throw new InvalidOperationException("HFSM showcase tried to move a dead actor entity.");
        }

        ref WorldPositionCm current = ref world.Get<WorldPositionCm>(_hero);
        ref PreviousWorldPositionCm previous = ref world.Get<PreviousWorldPositionCm>(_hero);
        Vector2 old = current.Value.ToVector2();
        previous.Value = current.Value;
        current.Value = Fix64Vec2.FromFloat(next.X, next.Y);

        ref FacingDirection facing = ref world.Get<FacingDirection>(_hero);
        if (forceFacing)
        {
            facing.AngleRad = facingRad;
            return;
        }

        Vector2 delta = next - old;
        if (delta.LengthSquared() > 1f)
        {
            facing.AngleRad = WorldPlane2D.FacingRadFromDirection(delta.X, delta.Y);
        }
    }

    private void WriteSnapshot()
    {
        if (!IsActive || _config == null || _engine == null || _hero == Entity.Null || !_engine.World.IsAlive(_hero))
        {
            Snapshot = CapabilityStandardHfsmShowcaseSnapshot.Inactive;
            return;
        }

        WorldPositionCm position = _engine.World.Get<WorldPositionCm>(_hero);
        HfsmStateConfig state = _config.RequireState(_state);
        Snapshot = new CapabilityStandardHfsmShowcaseSnapshot(
            IsActive,
            _state,
            state.Label,
            _config.ComposePath(_state),
            state.PlayerStory,
            _lastEvent,
            (int)MathF.Round(_health),
            (int)MathF.Round(_water),
            _lapCount,
            _transitionCount,
            (int)MathF.Round(position.Value.X.ToFloat()),
            (int)MathF.Round(position.Value.Y.ToFloat()),
            _health <= 0f,
            string.Equals(_state, StateDead, StringComparison.Ordinal));
    }

    private void DrawStateLine(ScreenOverlayBuffer overlay, int yOffset, string stateId, string text, int stableId)
    {
        bool active = string.Equals(_state, stateId, StringComparison.Ordinal);
        Vector4 fill = stateId == StateDead && active ? DeadFill : active ? ActiveFill : AliveFill;
        overlay.AddRect(PanelX + 16, PanelY + yOffset, PanelW - 32, 24, fill, active ? Accent : Muted, stableId, _frame);
        overlay.AddText(PanelX + 26, PanelY + yOffset + 5, text, 12, active ? Accent : Text, stableId + 1, _frame);
    }

    private void DrawBar(ScreenOverlayBuffer overlay, int x, int y, int width, string label, int value, int max, Vector4 color, int stableId)
    {
        int safeMax = Math.Max(1, max);
        int clamped = Math.Clamp(value, 0, safeMax);
        int filled = Math.Max(2, width * clamped / safeMax);
        overlay.AddText(x, y - 17, $"{label} {clamped}/{safeMax}", 12, Text, stableId, _frame);
        overlay.AddRect(x, y, width, 12, new Vector4(0.08f, 0.10f, 0.10f, 0.9f), Muted, stableId + 1, _frame);
        overlay.AddRect(x, y, filled, 12, color, color, stableId + 2, _frame);
    }

    private void RenderWorldOverlay(GameEngine engine, ScreenOverlayBuffer overlay)
    {
        if (engine.GetService(CoreServiceKeys.ScreenProjector) is not IScreenProjector projector)
        {
            return;
        }

        if (TryProjectEntity(engine, projector, _waterStation, 1.0f, out Vector2 waterPoint))
        {
            DrawWorldTag(overlay, waterPoint + new Vector2(0f, -28f), "water station", Water, 91100);
        }

        DrawTrack(projector, overlay);

        if (TryProjectEntity(engine, projector, _hero, 1.2f, out Vector2 heroPoint))
        {
            DrawWorldTag(overlay, heroPoint + new Vector2(0f, -36f), Snapshot.StateLabel, Snapshot.Dead ? Health : Accent, 91200);
        }
    }

    private void DrawTrack(IScreenProjector projector, ScreenOverlayBuffer overlay)
    {
        const int segments = 24;
        Vector2 previous = default;
        bool hasPrevious = false;
        for (int i = 0; i <= segments; i++)
        {
            float angle = MathF.PI * 2f * i / segments;
            if (!TryProjectWorld(projector, TrackPoint(angle), 0.12f, out Vector2 current))
            {
                hasPrevious = false;
                continue;
            }

            if (hasPrevious)
            {
                overlay.AddLine(
                    (int)MathF.Round(previous.X),
                    (int)MathF.Round(previous.Y),
                    (int)MathF.Round(current.X),
                    (int)MathF.Round(current.Y),
                    2,
                    TrackLine,
                    91300 + i,
                    _frame);
            }

            previous = current;
            hasPrevious = true;
        }
    }

    private void DrawWorldTag(ScreenOverlayBuffer overlay, Vector2 anchor, string text, Vector4 border, int stableId)
    {
        int width = Math.Clamp((text.Length * 7) + 18, 82, 220);
        int x = (int)MathF.Round(anchor.X - (width * 0.5f));
        int y = (int)MathF.Round(anchor.Y - 12f);
        AvoidPanel(ref x, ref y, width, 24);
        overlay.AddRect(x, y, width, 24, WorldLabelFill, border, stableId, _frame);
        overlay.AddText(x + 9, y + 5, text, 12, Text, stableId + 1, _frame);
    }

    private static void AvoidPanel(ref int x, ref int y, int width, int height)
    {
        if (x < PanelX + PanelW &&
            x + width > PanelX &&
            y < PanelY + PanelH &&
            y + height > PanelY)
        {
            y = PanelY + PanelH + 10;
        }
    }

    private static bool TryProjectEntity(GameEngine engine, IScreenProjector projector, Entity entity, float yOffsetMeters, out Vector2 point)
    {
        point = default;
        if (entity == Entity.Null ||
            !engine.World.IsAlive(entity) ||
            !engine.World.Has<VisualTransform>(entity))
        {
            return false;
        }

        Vector3 world = engine.World.Get<VisualTransform>(entity).Position + new Vector3(0f, yOffsetMeters, 0f);
        point = projector.WorldToScreen(world);
        return float.IsFinite(point.X) && float.IsFinite(point.Y);
    }

    private static bool TryProjectWorld(IScreenProjector projector, Vector2 worldCm, float yOffsetMeters, out Vector2 point)
    {
        point = projector.WorldToScreen(WorldPlane2D.LogicCmToVisualMeters(worldCm.X, worldCm.Y, yOffsetMeters));
        return float.IsFinite(point.X) && float.IsFinite(point.Y);
    }

    private static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDistance)
    {
        Vector2 delta = target - current;
        float distance = delta.Length();
        if (distance <= maxDistance || distance <= 0.0001f)
        {
            return target;
        }

        return current + (delta * (maxDistance / distance));
    }

    private static bool ConsumeShortcut(IInputBackend input, string path, ref bool wasDown)
    {
        bool isDown = input.GetButton(path);
        bool pressed = isDown && !wasDown;
        wasDown = isDown;
        return pressed;
    }

    private GameEngine RequireEngine() =>
        _engine ?? throw new InvalidOperationException("HFSM showcase requires an active engine.");

    private static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180f;
}
