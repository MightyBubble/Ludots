using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Client;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Navigation;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Scripting;
using NavGateShowcaseMod.Input;
using NavGateShowcaseMod.Runtime;

namespace NavGateShowcaseMod.Systems;

/// <summary>
/// 自动巡演时间线 + 全部运行时旋钮（G 门 / F 冻结 / N overlay / P O 障碍 / R 半径 / T 节奏）。
/// 时间线只负责"谁在何时改变世界"；路径与重烤全部走真实管线。
/// </summary>
public sealed class NavGateTimelineSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly NavGateState _state;
    private PlayerInputHandler? _input;
    private int _manualObstacleCount;

    public NavGateTimelineSystem(GameEngine engine, NavGateState state)
    {
        _engine = engine;
        _state = state;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float t)
    {
        if (_engine.CurrentMapSession == null || _engine.CurrentMapSession.MapId.Value != NavGateIds.MapId)
        {
            return;
        }

        HandleInput();
        AdvanceTimeline();
    }

    private void HandleInput()
    {
        if (_input == null)
        {
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) &&
                inputObj is PlayerInputHandler handler)
            {
                _input = handler;
            }

            return;
        }

        if (_input.PressedThisFrame(NavGateInputActions.ToggleGate))
        {
            ToggleGate(reason: "keypress");
        }

        if (_input.PressedThisFrame(NavGateInputActions.ToggleFreeze))
        {
            SetFrozen(!_state.Frozen);
        }

        if (_input.PressedThisFrame(NavGateInputActions.ToggleOverlay))
        {
            _state.OverlayEnabled = !_state.OverlayEnabled;
            ApplyOverlayEnabled();
        }

        if (_input.PressedThisFrame(NavGateInputActions.CycleRadius))
        {
            _state.ManualRadiusIndex = (_state.ManualRadiusIndex + 1) % NavGateIds.ManualObstacleRadiusCm.Length;
            Console.WriteLine($"[NavGate] 手动障碍半径 → {NavGateIds.ManualObstacleRadiusCm[_state.ManualRadiusIndex]}cm");
        }

        if (_input.PressedThisFrame(NavGateInputActions.CyclePace))
        {
            _state.PaceIndex = (_state.PaceIndex + 1) % NavGateIds.PaceMultipliers.Length;
            Console.WriteLine($"[NavGate] 巡演节奏 → {NavGateIds.PaceMultipliers[_state.PaceIndex]:0.0}x");
        }

        if (_input.PressedThisFrame(NavGateInputActions.SpawnObstacle))
        {
            var camera = ClientLocalSeatAccess.ResolveAuthorityCamera(_engine);
            (int x, int y) = ClampToTerrainExtent((int)camera.State.TargetCm.X, (int)camera.State.TargetCm.Y);
            int radius = NavGateIds.ManualObstacleRadiusCm[_state.ManualRadiusIndex];
            _engine.World.Create(
                WorldPositionCm.FromCm(x, y),
                new RuntimeNavMeshStructuralObstacle(),
                new ManifestationObstacleIntent2D
                {
                    Shape = ManifestationObstacleShape2D.Circle,
                    SinkNavigationObstacle = 1,
                    RadiusCm = radius,
                    NavRadiusCm = radius,
                });
            _manualObstacleCount++;
            Console.WriteLine($"[NavGate] 手动障碍 r={radius}cm at ({x},{y})");
        }

        if (_input.PressedThisFrame(NavGateInputActions.ClearObstacles))
        {
            var query = new QueryDescription().WithAll<RuntimeNavMeshStructuralObstacle>();
            var scratch = new System.Collections.Generic.List<Entity>(16);
            var span = new System.Span<Entity>(scratch.ToArray());
                _engine.World.GetEntities(in query, span);
                scratch.Clear();
                scratch.AddRange(span.ToArray());
            int removed = 0;
            for (int i = 0; i < scratch.Count; i++)
            {
                if (scratch[i] == _state.GateEntity)
                {
                    continue;
                }

                _engine.World.Destroy(scratch[i]);
                removed++;
            }

            _manualObstacleCount = 0;
            Console.WriteLine($"[NavGate] 清除手动障碍 {removed} 个");
        }
    }

    private void AdvanceTimeline()
    {
        _state.PhaseTicks++;

        switch (_state.Phase)
        {
            case NavGatePhase.Briefing:
                if (_state.PhaseTicks == 1)
                {
                    SpawnSquad();
                }

                if (_state.PhaseTicks >= NavGateIds.BriefingTicks)
                {
                    SetPhase(NavGatePhase.MarchToB, goalX: NavGateIds.CampBXcm, goalY: NavGateIds.CampBYcm);
                }

                break;

            case NavGatePhase.MarchToB:
                bool proximityTrigger = !_state.GateDropped &&
                    SquadNear(NavGateIds.GateXcm, NavGateIds.GateYcm, NavGateIds.GateTriggerDistanceCm);
                bool fallbackTrigger = !_state.GateDropped &&
                    _state.PhaseTicks >= NavGateIds.MarchGateFallbackTicks;
                bool fuseOpen = _state.GateCycleCount >= NavGateIds.MaxAutoGateCycles;

                if ((proximityTrigger || fallbackTrigger) && !fuseOpen)
                {
                    _state.GateCycleCount++;
                    if (!proximityTrigger)
                    {
                        Console.WriteLine("[NavGate] 首程兜底触发：小队未抵近触发圈，提前落门保证演示节奏");
                    }

                    if (_state.GateCycleCount == NavGateIds.MaxAutoGateCycles && !_state.GateFuseAnnounced)
                    {
                        _state.GateFuseAnnounced = true;
                        Console.WriteLine($"[NavGate] 稳定性熔断 NAV-R2：自动落门已满 {NavGateIds.MaxAutoGateCycles} 圈，后续巡演不再自动落门（重烤活锁风险，见 techdebt 报告）；手动 G 键仍可用");
                    }

                    DropGate(reason: "timeline");
                    SetPhase(NavGatePhase.Sealed, keepGoal: true);
                }
                else if (_state.ArrivedCount >= NavGateIds.SquadCount)
                {
                    SetPhase(NavGatePhase.ArrivedAtB, keepGoal: true);
                }
                else if (fuseOpen && fallbackTrigger && !_state.GateFuseAnnounced)
                {
                    _state.GateFuseAnnounced = true;
                    Console.WriteLine($"[NavGate] 稳定性熔断 NAV-R2：自动落门已满 {NavGateIds.MaxAutoGateCycles} 圈，后续巡演不再自动落门（重烤活锁风险，见 techdebt 报告）；手动 G 键仍可用");
                }

                break;

            case NavGatePhase.Sealed:
                if (_state.PhaseTicks >= NavGateIds.SealedCooldownTicks)
                {
                    SetPhase(NavGatePhase.MarchToB, keepGoal: true);
                }

                break;

            case NavGatePhase.ArrivedAtB:
                if (_state.PhaseTicks >= NavGateIds.ArrivedRestTicks)
                {
                    LiftGate(reason: "timeline");
                    SetPhase(NavGatePhase.ReturnToA, goalX: NavGateIds.CampAXcm, goalY: NavGateIds.CampAYcm);
                }

                break;

            case NavGatePhase.ReturnToA:
                if (_state.ArrivedCount >= NavGateIds.SquadCount)
                {
                    SetPhase(NavGatePhase.RestAtA, keepGoal: true);
                }

                break;

            case NavGatePhase.RestAtA:
                if (_state.PhaseTicks >= NavGateIds.ArrivedRestTicks)
                {
                    SetPhase(NavGatePhase.Briefing, goalX: NavGateIds.CampBXcm, goalY: NavGateIds.CampBYcm);
                }

                break;
        }
    }

    private void SetPhase(NavGatePhase phase, int? goalX = null, int? goalY = null, bool keepGoal = false)
    {
        _state.Phase = phase;
        _state.PhaseTicks = 0;
        if (!keepGoal)
        {
            _state.GoalXcm = goalX ?? _state.GoalXcm;
            _state.GoalYcm = goalY ?? _state.GoalYcm;
        }

        Console.WriteLine($"[NavGate] 阶段 → {_state.PhaseLabel}");
        if (phase == NavGatePhase.Briefing)
        {
            ResetSquad();
        }
    }

    private void SpawnSquad()
    {
        for (int i = 0; i < NavGateIds.SquadCount; i++)
        {
            double angle = (MathF.Tau * i) / NavGateIds.SquadCount;
            int x = NavGateIds.CampAXcm + (int)(NavGateIds.SquadRingRadiusCm * MathF.Cos((float)angle));
            int y = NavGateIds.CampAYcm + (int)(NavGateIds.SquadRingRadiusCm * MathF.Sin((float)angle));
            var entity = _engine.World.Create(WorldPositionCm.FromCm(x, y));
            _state.Agents.Add(new NavGateAgent { Entity = entity, World = _engine.World });
        }

        Console.WriteLine($"[NavGate] 小队 {NavGateIds.SquadCount} 人集结于 A 营");
    }

    private void ResetSquad()
    {
        for (int i = 0; i < _state.Agents.Count; i++)
        {
            if (_engine.World.IsAlive(_state.Agents[i].Entity))
            {
                _engine.World.Destroy(_state.Agents[i].Entity);
            }
        }

        _state.Agents.Clear();
    }

    private bool SquadNear(int xCm, int yCm, int radiusCm)
    {
        int inside = 0;
        for (int i = 0; i < _state.Agents.Count; i++)
        {
            var pos = _state.Agents[i].Position;
            int dx = (int)pos.Value.X - xCm;
            int dy = (int)pos.Value.Y - yCm;
            if ((dx * dx) + (dy * dy) <= radiusCm * radiusCm)
            {
                inside++;
            }
        }

        return inside >= Math.Max(1, _state.Agents.Count / 2);
    }

    public void DropGate(string reason)
    {
        if (_state.GateDropped)
        {
            return;
        }

        _state.GateEntity = _engine.World.Create(
            WorldPositionCm.FromCm(NavGateIds.GateXcm, NavGateIds.GateYcm),
            new RuntimeNavMeshStructuralObstacle(),
            new ManifestationObstacleIntent2D
            {
                Shape = ManifestationObstacleShape2D.Circle,
                SinkNavigationObstacle = 1,
                RadiusCm = NavGateIds.GateRadiusCm,
                NavRadiusCm = NavGateIds.GateRadiusCm,
            });
        _state.GateDropped = true;
        Console.WriteLine($"[NavGate] 城门落下（r={NavGateIds.GateRadiusCm}cm @ {NavGateIds.GateXcm},{NavGateIds.GateYcm}）reason={reason}——注意：若重烤未冻结，相邻瓦片将变橙并触发全队改道");
    }

    public void LiftGate(string reason)
    {
        if (!_state.GateDropped)
        {
            return;
        }

        if (_engine.World.IsAlive(_state.GateEntity))
        {
            _engine.World.Destroy(_state.GateEntity);
        }

        _state.GateDropped = false;
        Console.WriteLine($"[NavGate] 城门抬起 reason={reason}");
    }

    private void ToggleGate(string reason)
    {
        if (_state.GateDropped)
        {
            LiftGate(reason);
        }
        else
        {
            DropGate(reason);
        }
    }

    public void SetFrozen(bool frozen)
    {
        _state.Frozen = frozen;
        if (_engine.TryGetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue, out RuntimeIncrementalNavMeshRebuildQueue? queue) &&
            queue != null)
        {
            queue.ProcessingEnabled = !frozen;
        }

        Console.WriteLine($"[NavGate] 增量重烤 {(frozen ? "已冻结（消融：观察旧路径直穿城门）" : "已恢复")}");
    }

    private void ApplyOverlayEnabled()
    {
        if (!_engine.TryGetService(CoreServiceKeys.NavMeshPresentationState, out NavMeshPresentationState? state) || state == null)
        {
            return;
        }

        if (state.Enabled != _state.OverlayEnabled)
        {
            state.SetEnabled(_state.OverlayEnabled);
        }

        if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.RenderDebugState.Name, out var debugObj) &&
            debugObj is RenderDebugState renderDebugState)
        {
            renderDebugState.DrawNavMesh = _state.OverlayEnabled;
        }

        Console.WriteLine($"[NavGate] navmesh overlay → {(_state.OverlayEnabled ? "开" : "关")}");
    }

    private (int, int) ClampToTerrainExtent(int xCm, int yCm)
    {
        if (!_engine.TryGetService(CoreServiceKeys.LogicTerrain, out Ludots.Core.Navigation.Terrain.LogicTerrainField? terrain) ||
            terrain == null)
        {
            return (xCm, yCm);
        }

        terrain.GetWorldPositionMeters(0, 0, out float minWorldX, out float minWorldZ);
        terrain.GetWorldPositionMeters(terrain.WidthCells - 1, terrain.HeightCells - 1, out float maxWorldX, out float maxWorldZ);
        int minX = (int)MathF.Floor(MathF.Min(minWorldX, maxWorldX) * 100f);
        int maxX = (int)MathF.Ceiling(MathF.Max(minWorldX, maxWorldX) * 100f);
        int minY = (int)MathF.Floor(MathF.Min(minWorldZ, maxWorldZ) * 100f);
        int maxY = (int)MathF.Ceiling(MathF.Max(minWorldZ, maxWorldZ) * 100f);
        return (Math.Clamp(xCm, minX, maxX), Math.Clamp(yCm, minY, maxY));
    }
}
