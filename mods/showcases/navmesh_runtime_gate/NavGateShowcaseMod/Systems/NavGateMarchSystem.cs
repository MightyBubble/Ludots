using System;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Scripting;
using NavGateShowcaseMod.Runtime;

namespace NavGateShowcaseMod.Systems;

/// <summary>
/// 小队行军：每代理周期性调用真实 NavQueryService.TryFindPath 取 navmesh 折线，
/// 沿拐点行进；store revision 变化（障碍重烤）触发的重查询标记为"改道路径"。
/// 无路径（被完全围死或重烤冻结导致目标不可达时）代理原地等待——不做穿墙兜底。
/// </summary>
public sealed class NavGateMarchSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly NavGateState _state;
    private NavQueryService? _query;
    private uint _queryRevision;

    public NavGateMarchSystem(GameEngine engine, NavGateState state)
    {
        _engine = engine;
        _state = state;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (_state.Agents.Count == 0 || dt <= 0f)
        {
            return;
        }

        var registry = _engine.GetService(CoreServiceKeys.NavQueryServices) as NavQueryServiceRegistry;
        if (registry == null || !registry.TryGetStore(0, 0, out NavTileStore? store) || store == null)
        {
            return;
        }

        _state.LastSeenStoreRevision = store.Revision;
        if (_engine.TryGetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue, out RuntimeIncrementalNavMeshRebuildQueue? queue) &&
            queue != null)
        {
            _state.PendingTiles = queue.PendingTileCount;
            _state.LastBatchElapsedMs = queue.LastBatchElapsedMs;
        }

        bool revisionChanged = store.Revision != _queryRevision;
        if (revisionChanged || _query == null)
        {
            _queryRevision = store.Revision;
            if (!registry.TryCreateQuery(0, 0, null, out NavQueryService? refreshed) || refreshed == null)
            {
                return;
            }

            _query = refreshed;
        }

        _state.ArrivedCount = 0;
        _state.TotalPathLenCm = 0;

        for (int i = 0; i < _state.Agents.Count; i++)
        {
            NavGateAgent agent = _state.Agents[i];
            if (!_engine.World.IsAlive(agent.Entity))
            {
                continue;
            }

            StepAgent(agent, dt, revisionChanged);
            if (agent.Arrived)
            {
                _state.ArrivedCount++;
            }

            _state.TotalPathLenCm += PathLengthCm(agent);
        }
    }

    private void StepAgent(NavGateAgent agent, float dt, bool revisionChanged)
    {
        var pos = agent.Position;
        int xCm = pos.Value.X.RoundToInt();
        int yCm = pos.Value.Y.RoundToInt();
        int dxGoal = xCm - _state.GoalXcm;
        int dyGoal = yCm - _state.GoalYcm;
        agent.Arrived = (dxGoal * dxGoal) + (dyGoal * dyGoal) <= NavGateIds.ArriveDistanceCm * NavGateIds.ArriveDistanceCm;

        agent.RepathCountdown--;
        bool needsRepath = !agent.HasPath && !agent.Arrived;
        if (revisionChanged && !agent.Arrived)
        {
            needsRepath = true;
            agent.DetourPath = true;
        }

        if (agent.RepathCountdown <= 0 && !agent.Arrived)
        {
            needsRepath = true;
        }

        if (needsRepath && !agent.Arrived)
        {
            Repath(agent, xCm, yCm, markDetourOnlyOnRevision: false);
            agent.RepathCountdown = NavGateIds.RepathIntervalTicks;
        }
        else if (!revisionChanged)
        {
            agent.DetourPath = agent.DetourPath && agent.HasPath;
        }

        if (agent.Arrived || !agent.HasPath)
        {
            return;
        }

        FollowWaypoints(agent, dt);
    }

    private void Repath(NavGateAgent agent, int xCm, int yCm, bool markDetourOnlyOnRevision)
    {
        NavPathResult result = _query!.TryFindPath(xCm, yCm, _state.GoalXcm, _state.GoalYcm);
        if (result.Status == NavPathStatus.Ok && result.PathXcm != null && result.PathXcm.Length >= 2)
        {
            agent.PathXcm = result.PathXcm;
            agent.PathZcm = result.PathZcm!;
            agent.PathCursor = AdvanceCursor(agent, xCm, yCm);
            agent.HasPath = true;
        }
        else
        {
            agent.HasPath = false;
            agent.PathXcm = System.Array.Empty<int>();
            agent.PathZcm = System.Array.Empty<int>();
        }
    }

    private static int AdvanceCursor(NavGateAgent agent, int xCm, int yCm)
    {
        int cursor = 0;
        while (cursor < agent.PathXcm.Length - 1)
        {
            int dx = agent.PathXcm[cursor] - xCm;
            int dy = agent.PathZcm[cursor] - yCm;
            if ((dx * dx) + (dy * dy) > NavGateIds.WaypointReachCm * NavGateIds.WaypointReachCm)
            {
                break;
            }

            cursor++;
        }

        return cursor;
    }

    private void FollowWaypoints(NavGateAgent agent, float dt)
    {
        var pos = agent.Position;
        int xCm = pos.Value.X.RoundToInt();
        int yCm = pos.Value.Y.RoundToInt();

        while (agent.PathCursor < agent.PathXcm.Length)
        {
            int dx = agent.PathXcm[agent.PathCursor] - xCm;
            int dy = agent.PathZcm[agent.PathCursor] - yCm;
            if ((dx * dx) + (dy * dy) > NavGateIds.WaypointReachCm * NavGateIds.WaypointReachCm)
            {
                break;
            }

            agent.PathCursor++;
        }

        if (agent.PathCursor >= agent.PathXcm.Length)
        {
            return;
        }

        int tx = agent.PathXcm[agent.PathCursor];
        int ty = agent.PathZcm[agent.PathCursor];
        float dxT = tx - xCm;
        float dyT = ty - yCm;
        float dist = MathF.Sqrt((dxT * dxT) + (dyT * dyT));
        if (dist < 1f)
        {
            return;
        }

        float step = NavGateIds.MarchSpeedCmPerSecond * _state.Pace * dt;
        step = MathF.Min(step, dist);
        float nx = xCm + ((dxT / dist) * step);
        float ny = yCm + ((dyT / dist) * step);
        _engine.World.Get<WorldPositionCm>(agent.Entity).Value = new Fix64Vec2(Fix64.FromFloat(nx), Fix64.FromFloat(ny));
    }

    private static int PathLengthCm(NavGateAgent agent)
    {
        if (!agent.HasPath || agent.PathCursor >= agent.PathXcm.Length)
        {
            return 0;
        }

        int length = 0;
        for (int i = agent.PathCursor; i < agent.PathXcm.Length - 1; i++)
        {
            int dx = agent.PathXcm[i + 1] - agent.PathXcm[i];
            int dy = agent.PathZcm[i + 1] - agent.PathZcm[i];
            length += (int)MathF.Sqrt((dx * dx) + (dy * dy));
        }

        return length;
    }
}
