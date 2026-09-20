using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NavGateShowcaseMod.Runtime;

namespace NavGateShowcaseMod.Systems;

/// <summary>
/// 解释层渲染：navmesh 之上的路径折线（绿=常规 / 黄=改道）、脏瓦片橙框、
/// 城门红环、小队青圈、营地白环。所有几何的 Y 取自 ContinuousHeightmap 采样，
/// 贴合起伏地形；每帧 transient 重投，无稳定 Id 记账。
/// </summary>
public sealed class NavGateRenderSystem : ISystem<float>
{
    private static readonly Vector4 Green = new(0.25f, 0.95f, 0.35f, 0.9f);
    private static readonly Vector4 Yellow = new(0.98f, 0.85f, 0.15f, 0.95f);
    private static readonly Vector4 Orange = new(1.0f, 0.55f, 0.1f, 0.95f);
    private static readonly Vector4 Red = new(1.0f, 0.2f, 0.15f, 0.9f);
    private static readonly Vector4 RedFill = new(1.0f, 0.25f, 0.2f, 0.28f);
    private static readonly Vector4 Cyan = new(0.2f, 0.95f, 0.95f, 0.9f);
    private static readonly Vector4 White = new(0.95f, 0.95f, 0.95f, 0.85f);

    private readonly GameEngine _engine;
    private readonly NavGateState _state;

    public NavGateRenderSystem(GameEngine engine, NavGateState state)
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

        var overlays = _engine.GetService(CoreServiceKeys.GroundOverlayBuffer) as GroundOverlayBuffer;
        if (overlays == null)
        {
            return;
        }

        IContinuousHeightmap? heightmap = _engine.GetService(CoreServiceKeys.ContinuousHeightmap) as IContinuousHeightmap;

        DrawCamps(overlays, heightmap);
        DrawGate(overlays, heightmap);
        DrawDirtyTiles(overlays, heightmap);
        DrawSquad(overlays, heightmap);
    }

    private void DrawCamps(GroundOverlayBuffer overlays, IContinuousHeightmap? heightmap)
    {
        AddRing(overlays, heightmap, NavGateIds.CampAXcm, NavGateIds.CampAYcm, 1400f, White, 3f);
        AddRing(overlays, heightmap, NavGateIds.CampBXcm, NavGateIds.CampBYcm, 1400f, White, 3f);

        // B 营环内圈 = 抵达进度仪表（内半径随 arrived 比例收缩）
        float arriveRatio = NavGateIds.SquadCount == 0 ? 0f : _state.ArrivedCount / (float)NavGateIds.SquadCount;
        AddRing(
            overlays,
            heightmap,
            NavGateIds.CampBXcm,
            NavGateIds.CampBYcm,
            900f,
            arriveRatio >= 1f ? Green : Cyan,
            6f,
            innerRatio: 1f - (0.85f * arriveRatio));
    }

    private void DrawGate(GroundOverlayBuffer overlays, IContinuousHeightmap? heightmap)
    {
        if (!_state.GateDropped)
        {
            return;
        }

        // 脉动：落下后前 2 秒醒目闪烁
        float pulse = _state.PhaseTicks < 120 && (_state.PhaseTicks / 20) % 2 == 0 ? 1f : 0.75f;
        Vector4 ring = new(Red.X, Red.Y, Red.Z, Red.W * pulse);
        overlays.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Ring,
            Center = SampleWorld(heightmap, NavGateIds.GateXcm, NavGateIds.GateYcm, liftMeters: 0.3f),
            Radius = NavGateIds.GateRadiusCm / 100f,
            InnerRadius = (NavGateIds.GateRadiusCm / 100f) - 1.4f,
            BorderColor = ring,
            FillColor = default,
            BorderWidth = 10f,
        });
        overlays.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Circle,
            Center = SampleWorld(heightmap, NavGateIds.GateXcm, NavGateIds.GateYcm, liftMeters: 0.05f),
            Radius = NavGateIds.GateRadiusCm / 100f,
            FillColor = RedFill,
            BorderColor = ring,
        });
    }

    private void DrawDirtyTiles(GroundOverlayBuffer overlays, IContinuousHeightmap? heightmap)
    {
        if (!_engine.TryGetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue, out RuntimeIncrementalNavMeshRebuildQueue? queue) ||
            queue == null)
        {
            return;
        }

        var pending = queue.PendingTilesSnapshot();
        if (pending.Length == 0)
        {
            return;
        }

        LogicTerrainField? terrain = _engine.GetService(CoreServiceKeys.LogicTerrain) as LogicTerrainField;
        if (terrain == null)
        {
            return;
        }

        float tileW = (terrain.ChunkSizeCells * terrain.HorizontalStepCm) / 100f;
        float tileH = (terrain.ChunkSizeCells * terrain.VerticalStepCm) / 100f;
        float originX = 0f;
        float originZ = 0f;
        terrain.GetWorldPositionMeters(0, 0, out originX, out originZ);

        for (int i = 0; i < pending.Length; i++)
        {
            float cx = originX + ((pending[i].ChunkX + 0.5f) * tileW);
            float cz = originZ + ((pending[i].ChunkY + 0.5f) * tileH);
            AddTileOutline(overlays, heightmap, cx, cz, tileW, tileH, Orange);
        }
    }

    private void DrawSquad(GroundOverlayBuffer overlays, IContinuousHeightmap? heightmap)
    {
        for (int i = 0; i < _state.Agents.Count; i++)
        {
            NavGateAgent agent = _state.Agents[i];
            if (!agent.HasPath || agent.PathCursor >= agent.PathXcm.Length)
            {
                var pos = agent.Position.Value;
                AddCircle(overlays, heightmap, pos.X.RoundToInt(), pos.Y.RoundToInt(), 260f, Cyan);
                continue;
            }

            Vector4 color = agent.DetourPath ? Yellow : Green;
            int prevX = agent.Position.Value.X.RoundToInt();
            int prevY = agent.Position.Value.Y.RoundToInt();
            for (int w = agent.PathCursor; w < agent.PathXcm.Length; w++)
            {
                AddSegment(overlays, heightmap, prevX, prevY, agent.PathXcm[w], agent.PathZcm[w], color);
                prevX = agent.PathXcm[w];
                prevY = agent.PathZcm[w];
            }

            var unitPos = agent.Position.Value;
            AddCircle(overlays, heightmap, unitPos.X.RoundToInt(), unitPos.Y.RoundToInt(), 260f, agent.Arrived ? Green : Cyan);
        }
    }

    private void AddSegment(GroundOverlayBuffer overlays, IContinuousHeightmap? heightmap, int axCm, int ayCm, int bxCm, int byCm, Vector4 color)
    {
        float ax = axCm / 100f;
        float az = ayCm / 100f;
        float bx = bxCm / 100f;
        float bz = byCm / 100f;
        float dx = bx - ax;
        float dz = bz - az;
        float length = MathF.Sqrt((dx * dx) + (dz * dz));
        if (length < 0.05f)
        {
            return;
        }

        overlays.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Line,
            Center = SampleWorld(heightmap, axCm, ayCm, liftMeters: 0.4f),
            Rotation = MathF.Atan2(dz, dx),
            Length = length,
            Width = 2.2f,
            BorderColor = color,
            FillColor = color,
        });
    }

    private void AddCircle(GroundOverlayBuffer overlays, IContinuousHeightmap? heightmap, int xCm, int yCm, float radiusCm, Vector4 color)
    {
        overlays.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Circle,
            Center = SampleWorld(heightmap, xCm, yCm, liftMeters: 0.25f),
            Radius = radiusCm / 100f,
            BorderColor = color,
            FillColor = default,
            BorderWidth = 3f,
        });
    }

    private void AddRing(GroundOverlayBuffer overlays, IContinuousHeightmap? heightmap, int xCm, int yCm, float radiusCm, Vector4 color, float width, float innerRatio = 0.86f)
    {
        overlays.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Ring,
            Center = SampleWorld(heightmap, xCm, yCm, liftMeters: 0.15f),
            Radius = radiusCm / 100f,
            InnerRadius = (radiusCm / 100f) * innerRatio,
            BorderColor = color,
            FillColor = default,
            BorderWidth = width,
        });
    }

    private void AddTileOutline(GroundOverlayBuffer overlays, IContinuousHeightmap? heightmap, float cx, float cz, float w, float h, Vector4 color)
    {
        int minX = (int)((cx - (w / 2)) * 100);
        int maxX = (int)((cx + (w / 2)) * 100);
        int minZ = (int)((cz - (h / 2)) * 100);
        int maxZ = (int)((cz + (h / 2)) * 100);
        AddSegment(overlays, heightmap, minX, minZ, maxX, minZ, color);
        AddSegment(overlays, heightmap, maxX, minZ, maxX, maxZ, color);
        AddSegment(overlays, heightmap, maxX, maxZ, minX, maxZ, color);
        AddSegment(overlays, heightmap, minX, maxZ, minX, minZ, color);
    }

    private static Vector3 SampleWorld(IContinuousHeightmap? heightmap, int xCm, int yCm, float liftMeters)
    {
        float y = 0f;
        if (heightmap != null && heightmap.TrySampleHeightCm(xCm, yCm, out float heightCm))
        {
            y = (heightCm / 100f) + liftMeters;
        }
        else
        {
            y = liftMeters;
        }

        return new Vector3(xCm / 100f, y, yCm / 100f);
    }
}
