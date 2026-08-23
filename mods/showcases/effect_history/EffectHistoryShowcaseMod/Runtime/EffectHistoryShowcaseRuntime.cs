using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using EffectHistoryShowcaseMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityHistory;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace EffectHistoryShowcaseMod.Runtime;

public sealed class EffectHistoryShowcaseRuntime
{
    private World? _world;
    private Entity _viewer;
    private Entity _target;
    private Entity _replacement;
    private int _tick;
    private bool _started;
    private EffectTargetResolutionMode _mode = EffectTargetResolutionMode.LastKnown;
    private EffectTargetRef _pendingTarget;
    private int _pendingDueTick;
    private bool _hasPending;
    private int _delayTicks = 3;
    private int _knowledgeTtlTicks = 12;
    private Fix64Vec2 _lastKnownPosition = Fix64Vec2.FromInt(900, 200);
    private string _actionMessage = string.Empty;
    private EntitySnapshotCapture? _snapshotCapture;
    private readonly EffectHistoryPanelController _panelController;

    public EffectHistoryShowcaseRuntime() => _panelController = new(this);
    public EntitySnapshotStore EntitySnapshots { get; private set; } = new(16);
    public KnowledgeSnapshotStore KnowledgeSnapshots { get; private set; } = new(16);
    public EffectExecutionRecordStore ExecutionRecords { get; private set; } = new(32);
    public EffectTargetResolveResult LastResult { get; private set; } = EffectTargetResolveResult.MissingValue;
    public EffectTargetResolutionMode Mode => _mode;
    public int DelayTicks => _delayTicks;
    public int KnowledgeTtlTicks => _knowledgeTtlTicks;

    public Task HandleMapLoadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        _world = context.GetWorld();
        Reset();
        _snapshotCapture?.Dispose();
        _snapshotCapture = new EntitySnapshotCapture(_world, EntitySnapshots, new RuntimeSnapshotReader(this));
        engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
        {
            VirtualCameraId = "EffectHistory.Camera.Showcase",
            TargetCm = Vector2.Zero,
            DistanceCm = 3600f,
            Pitch = 62f,
            Yaw = 180f
        });
        if (engine?.GetService(CoreServiceKeys.InputHandler) is PlayerInputHandler input)
            input.PushContext("EffectHistoryShowcase.Controls");
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine?.GetService(CoreServiceKeys.InputHandler) is PlayerInputHandler input)
            input.PopContext("EffectHistoryShowcase.Controls");
        _snapshotCapture?.Dispose();
        _snapshotCapture = null;
        _panelController.Clear(); _world = null; Reset(); return Task.CompletedTask;
    }

    public void ProcessInput(GameEngine engine, IInputActionReader input)
    {
        if (!Active(engine)) return;
        if (input.PressedThisFrame(EffectHistoryShowcaseIds.ModeLiveActionId)) _mode = EffectTargetResolutionMode.Live;
        if (input.PressedThisFrame(EffectHistoryShowcaseIds.ModeKnownActionId)) _mode = EffectTargetResolutionMode.LastKnown;
        if (input.PressedThisFrame(EffectHistoryShowcaseIds.ModePointActionId)) _mode = EffectTargetResolutionMode.Point;
        if (input.PressedThisFrame(EffectHistoryShowcaseIds.ModeCellActionId)) _mode = EffectTargetResolutionMode.Cell;
        if (input.PressedThisFrame(EffectHistoryShowcaseIds.DelayDownActionId)) _delayTicks = Math.Max(0, _delayTicks - 1);
        if (input.PressedThisFrame(EffectHistoryShowcaseIds.DelayUpActionId)) _delayTicks = Math.Min(30, _delayTicks + 1);
        if (input.PressedThisFrame(EffectHistoryShowcaseIds.TtlDownActionId)) _knowledgeTtlTicks = Math.Max(1, _knowledgeTtlTicks - 1);
        if (input.PressedThisFrame(EffectHistoryShowcaseIds.TtlUpActionId)) _knowledgeTtlTicks = Math.Min(60, _knowledgeTtlTicks + 1);
        if (input.PressedThisFrame(EffectHistoryShowcaseIds.SubmitActionId)) Submit();
        if (input.PressedThisFrame(EffectHistoryShowcaseIds.HideActionId)) Hide();
        if (input.PressedThisFrame(EffectHistoryShowcaseIds.RemoveActionId)) Remove();
        if (input.PressedThisFrame(EffectHistoryShowcaseIds.ReuseActionId)) Reuse();
    }

    public void Advance(GameEngine engine)
    {
        if (_world == null || !Active(engine)) return;
        if (!_started) { BuildEntities(); _started = true; }
        _tick++;
        if (_hasPending && _tick >= _pendingDueTick) { ExecutePending(); _hasPending = false; }
        RefreshMarkers(engine);
    }

    public void RefreshPanel(GameEngine engine) { if (Active(engine)) _panelController.MountOrRefresh(engine); }

    internal EffectHistoryPanelState BuildPanelState()
    {
        var history = new List<string>(Math.Min(ExecutionRecords.Count, 8));
        for (int i = Math.Max(0, ExecutionRecords.Count - 8); i < ExecutionRecords.Count; i++)
            if (ExecutionRecords.TryGet(i, out EffectExecutionRecord record))
                history.Add($"tick {record.ExecutedTick}: {record.Target.Mode} target {record.Target.Target} -> {record.Result} (RootId {record.RootId})");
        return new EffectHistoryPanelState(
            "Effect History",
            "Choose a policy, submit a delayed effect, then hide or remove the target. The panel shows the formal resolver result.",
            "1 Live  2 LastKnown  3 Point  4 Cell  D/F delay  T/G TTL  Enter submit  H expire  R remove  U reuse",
            $"Policy: {_mode} | delay={_delayTicks} ticks | TTL={_knowledgeTtlTicks} ticks | tick {_tick} | {(_hasPending ? $"pending until {_pendingDueTick}" : "idle")}",
            $"Target {_target.Id}:{_target.WorldId}:{_target.Version} | alive={Alive(_target)}",
            $"Replacement {(_replacement == Entity.Null ? "none" : $"{_replacement.Id}:{_replacement.WorldId}:{_replacement.Version}")} | alive={Alive(_replacement)}",
            $"Knowledge revision=1 | expiry={KnowledgeExpiry()} | viewer={_viewer.Id}:{_viewer.Version}",
            $"Result: {LastResult}{(_actionMessage.Length == 0 ? string.Empty : $" | {_actionMessage}")}", history);
    }

    private void Submit()
    {
        if (_world == null || _target == Entity.Null) { LastResult = EffectTargetResolveResult.MissingIdentity; _actionMessage = "Submit rejected: target identity is unavailable"; return; }
        _actionMessage = string.Empty;
        EntityRef viewer = EntityRef.From(_viewer), target = EntityRef.From(_target);
        Fix64Vec2 point = CurrentTargetPosition();
        _lastKnownPosition = point;
        var snapshot = new KnowledgeSnapshot { Viewer = viewer, Target = target, Presence = KnowledgePresence.Known, PositionAccess = KnowledgePositionAccess.LastKnown, Position = point, HasPosition = 1, ObservedTick = _tick, ExpiryTick = _tick + _knowledgeTtlTicks, Revision = 1 };
        KnowledgeSnapshots.Upsert(in snapshot);
        _pendingTarget = new EffectTargetRef(in target, in viewer, _mode, _tick, 1, _tick + _knowledgeTtlTicks, point, 7);
        _pendingDueTick = _tick + _delayTicks; _hasPending = true;
    }

    private void Hide()
    {
        if (_world == null || _target == Entity.Null) { _actionMessage = "Expiry rejected: target identity is unavailable"; return; }
        _actionMessage = string.Empty;
        EntityRef viewer = EntityRef.From(_viewer), target = EntityRef.From(_target);
        var snapshot = new KnowledgeSnapshot { Viewer = viewer, Target = target, Presence = KnowledgePresence.HiddenWithSource, PositionAccess = KnowledgePositionAccess.LastKnown, Position = _lastKnownPosition, HasPosition = 1, ObservedTick = _tick, ExpiryTick = _tick, Revision = 1 };
        KnowledgeSnapshots.Upsert(in snapshot); LastResult = EffectTargetResolveResult.Stale;
    }

    private void Remove()
    {
        if (_world == null || !_world.IsAlive(_target)) { _actionMessage = "Remove rejected: target is already absent"; return; }
        _actionMessage = string.Empty;
        _world.Destroy(_target);
        LastResult = EffectTargetResolveResult.Stale;
    }

    private void Reuse()
    {
        if (_world == null) { _actionMessage = "Reuse rejected: showcase world is unavailable"; return; }
        if (_world.IsAlive(_target)) { _actionMessage = "Reuse rejected: remove the original identity first"; return; }
        if (Alive(_replacement)) { _actionMessage = "Reuse ignored: replacement identity already exists"; return; }
        _actionMessage = string.Empty;
        Entity replacement = _world.Create();
        _world.Add(replacement, new Name { Value = "Replacement identity" });
        _world.Add(replacement, new MapEntity { MapId = new MapId(EffectHistoryShowcaseIds.MapId) });
        _world.Add(replacement, new WorldPositionCm { Value = Fix64Vec2.FromInt(900, 200) });
        _replacement = replacement;
        LastResult = EffectTargetResolveResult.Stale;
    }

    private void ExecutePending()
    {
        EffectTargetResolveOutput output = EffectTargetResolver.Resolve(_world!, in _pendingTarget, _tick, EntitySnapshots, KnowledgeSnapshots);
        LastResult = output.Result;
        KnowledgeIdMask256 empty = default;
        var context = new EffectContext { RootId = 1087, Source = _viewer, Target = _target, HasTargetRef = 1, TargetRef = _pendingTarget };
        var record = EffectExecutionRecordFactory.Create(in context, 1, in _pendingTarget, _tick, output.Result, _delayTicks, _knowledgeTtlTicks, 0, in empty, in empty);
        if (!ExecutionRecords.TryAdd(in record, out _)) LastResult = EffectTargetResolveResult.CapacityRejected;
    }

    private void BuildEntities()
    {
        _viewer = _world!.Create(); _target = _world.Create();
        AddEntity(_viewer, "Effect source", -900, 200); AddEntity(_target, "Target snapshot", 900, 200);
    }

    private void AddEntity(Entity entity, string name, int x, int y)
    {
        _world!.Add(entity, new Name { Value = name });
        _world.Add(entity, new MapEntity { MapId = new MapId(EffectHistoryShowcaseIds.MapId) });
        _world.Add(entity, new WorldPositionCm { Value = Fix64Vec2.FromInt(x, y) });
    }

    private void RefreshMarkers(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.MinimapMarkerBuffer) is not MinimapMarkerBuffer markers) return;
        markers.BeginFrame(); Vector4 source = new(0.2f, 0.8f, 1f, 1f), target = new(1f, 0.7f, 0.2f, 1f), ghost = new(0.7f, 0.5f, 1f, 0.9f);
        markers.TryAdd(10870, _viewer, -900, 200, in source, 10f);
        if (Alive(_target)) markers.TryAdd(10871, _target, 900, 200, in target, 10f);
        else markers.TryAdd(10872, Entity.Null, _lastKnownPosition.X.ToFloat(), _lastKnownPosition.Y.ToFloat(), in ghost, 10f);
    }

    internal void EmitPrimitives(PrimitiveDrawBuffer primitives, int sphereMeshId, int cubeMeshId)
    {
        bool targetAlive = Alive(_target);
        float tx = targetAlive ? 9f : _lastKnownPosition.X.ToFloat() / 100f;
        float ty = targetAlive ? 2f : _lastKnownPosition.Y.ToFloat() / 100f;
        Vector4 targetColor = targetAlive ? new Vector4(1f, 0.74f, 0.24f, 1f) : new Vector4(0.68f, 0.5f, 1f, 1f);
        AddPrimitive(primitives, sphereMeshId, 10870, new Vector3(-9f, 0.32f, 2f), new Vector3(0.72f), new Vector4(0.2f, 0.82f, 1f, 1f));
        AddPrimitive(primitives, sphereMeshId, 10871, new Vector3(tx, 0.32f, ty), new Vector3(0.72f), targetColor);
        float midpoint = (-9f + tx) * 0.5f;
        float length = MathF.Abs(tx + 9f);
        AddPrimitive(primitives, cubeMeshId, 10872, new Vector3(midpoint, 0.05f, 2f), new Vector3(MathF.Max(0.08f, length * 0.5f), 0.04f, _hasPending ? 0.12f : 0.05f), _hasPending ? new Vector4(1f, 0.84f, 0.35f, 1f) : new Vector4(0.25f, 0.35f, 0.45f, 1f));
        if (_mode == EffectTargetResolutionMode.Point || _mode == EffectTargetResolutionMode.Cell)
            AddPrimitive(primitives, cubeMeshId, 10873, new Vector3(_lastKnownPosition.X.ToFloat() / 100f, 0.08f, _lastKnownPosition.Y.ToFloat() / 100f), new Vector3(0.9f, 0.08f, 0.9f), new Vector4(0.35f, 0.9f, 0.65f, 1f));
    }

    internal void DrawOverlay(ScreenOverlayBuffer overlay)
    {
        overlay.AddRect(40, 180, 720, 280, new Vector4(0.03f, 0.06f, 0.09f, 0.92f), new Vector4(0.18f, 0.29f, 0.38f, 1f), stableId: 10880, dirtySerial: _tick);
        overlay.AddText(64, 204, "TARGET RESOLUTION LAB", 18, new Vector4(0.86f, 0.93f, 1f, 1f), stableId: 10881, dirtySerial: _tick);
        overlay.AddText(64, 236, "Cyan = source    Amber = live    Violet = last-known    Green = point/cell", 12, new Vector4(0.63f, 0.75f, 0.84f, 1f), stableId: 10882, dirtySerial: _tick);
        int sourceX = 130;
        int targetX = 650;
        int markerY = 330;
        Vector4 sourceColor = new(0.2f, 0.82f, 1f, 1f);
        Vector4 targetColor = Alive(_target) ? new Vector4(1f, 0.74f, 0.24f, 1f) : new Vector4(0.68f, 0.5f, 1f, 1f);
        Vector4 linkColor = _hasPending ? new Vector4(1f, 0.84f, 0.35f, 1f) : new Vector4(0.28f, 0.38f, 0.48f, 1f);
        overlay.AddLine(sourceX + 18, markerY + 18, targetX - 18, markerY + 18, _hasPending ? 5 : 2, linkColor, stableId: 10883, dirtySerial: _tick);
        overlay.AddRect(sourceX, markerY, 36, 36, sourceColor, sourceColor, stableId: 10884, dirtySerial: _tick);
        overlay.AddRect(targetX - 36, markerY, 36, 36, targetColor, targetColor, stableId: 10885, dirtySerial: _tick);
        overlay.AddText(sourceX - 18, markerY + 52, "Effect source", 12, sourceColor, stableId: 10886, dirtySerial: _tick);
        overlay.AddText(targetX - 86, markerY + 52, Alive(_target) ? "Target identity" : "Last-known ghost", 12, targetColor, stableId: 10887, dirtySerial: _tick);
        if (_mode == EffectTargetResolutionMode.Point || _mode == EffectTargetResolutionMode.Cell)
            overlay.AddRect(targetX - 50, markerY - 14, 64, 64, new Vector4(0f, 0f, 0f, 0f), new Vector4(0.35f, 0.9f, 0.65f, 1f), stableId: 10888, dirtySerial: _tick);
        overlay.AddText(64, 410, _hasPending ? $"Pending effect -> execute at tick {_pendingDueTick}" : $"Policy {_mode} ready for submission", 13, linkColor, stableId: 10889, dirtySerial: _tick);
    }

    private static void AddPrimitive(PrimitiveDrawBuffer primitives, int meshAssetId, int stableId, Vector3 position, Vector3 scale, Vector4 color)
    {
        primitives.TryAdd(new PrimitiveDrawItem
        {
            MeshAssetId = meshAssetId,
            StableId = stableId,
            Position = position,
            Scale = scale,
            Color = color,
            RenderPath = VisualRenderPath.StaticMesh,
            Mobility = VisualMobility.Static,
            Flags = VisualRuntimeFlags.Visible,
            Visibility = VisualVisibility.Visible
        });
    }

    private Fix64Vec2 CurrentTargetPosition()
    {
        if (_world != null && Alive(_target) && _world.TryGet(_target, out WorldPositionCm position))
            return position.Value;
        return _lastKnownPosition;
    }

    private int KnowledgeExpiry() => KnowledgeSnapshots.TryGetExpired(EntityRef.From(_viewer), EntityRef.From(_target), out KnowledgeSnapshot snapshot) ? snapshot.ExpiryTick : 0;
    private bool Alive(Entity entity) => _world != null && entity != Entity.Null && _world.IsAlive(entity);
    private bool Active(GameEngine engine) => engine.CurrentMapSession?.MapId.Value == EffectHistoryShowcaseIds.MapId;
    private void Reset()
    {
        _viewer = Entity.Null;
        _target = Entity.Null;
        _replacement = Entity.Null;
        _tick = 0;
        _started = false;
        _hasPending = false;
        _mode = EffectTargetResolutionMode.LastKnown;
        _delayTicks = 3;
        _knowledgeTtlTicks = 12;
        _lastKnownPosition = Fix64Vec2.FromInt(900, 200);
        _actionMessage = string.Empty;
        LastResult = EffectTargetResolveResult.MissingValue;
        EntitySnapshots = new EntitySnapshotStore(16);
        KnowledgeSnapshots = new KnowledgeSnapshotStore(16);
        ExecutionRecords = new EffectExecutionRecordStore(32);
    }

    private sealed class RuntimeSnapshotReader : IEntitySnapshotReader
    {
        private readonly EffectHistoryShowcaseRuntime _runtime;
        public RuntimeSnapshotReader(EffectHistoryShowcaseRuntime runtime) => _runtime = runtime;

        public bool TryCapture(World world, in Entity entity, int tick, out EntitySnapshot snapshot)
        {
            if (entity != _runtime._target && entity != _runtime._viewer)
            {
                snapshot = default;
                return false;
            }

            snapshot = new EntitySnapshot { Identity = EntityRef.From(entity), CapturedTick = _runtime._tick, State = EntitySnapshotState.Live };
            if (world.TryGet(entity, out WorldPositionCm position))
            {
                snapshot.Position = position.Value;
                snapshot.HasPosition = 1;
            }
            return true;
        }
    }
}
