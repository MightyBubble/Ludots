using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Spatial;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class SpatialNodeDriver : IGraphOpsNodeDriver
{
    public const int CasterTeamId = 1;
    public const int EnemyTeamId = 2;
    public const uint CasterLayer = 0b0001;
    public const uint EnemyLayer = 0b0010;
    public const int ConeDirDeg = 90;
    public const int ConeHalfDeg = 30;
    public const int ConeRangeCm = 800;
    public const int RectHalfWidthCm = 120;
    public const int RectHalfHeightCm = 60;
    public const int RectRotationDeg = 15;
    public const int LineDirDeg = 45;
    public const int LineLengthCm = 500;
    public const int LineHalfWidthCm = 25;
    public const int HexRadius = 2;

    private GasGraphRuntimeApi? _spatialApi;
    private SpatialQueryService? _spatial;
    private SpatialCoordinateConverter? _coords;
    private GridSpatialPartitionWorld? _grid;
    private Entity _caster;
    private Entity[] _units = Array.Empty<Entity>();
    private Entity[] _stageUnitProxies = Array.Empty<Entity>();
    private Entity _stageCasterProxy;
    private float[] _unitX = Array.Empty<float>();
    private float[] _unitY = Array.Empty<float>();
    private byte[] _unitInRange = Array.Empty<byte>();
    private string[] _unitLabels = Array.Empty<string>();
    private readonly Entity[] _lastTargets = new Entity[GraphVmLimits.MaxTargets];
    private bool _seeded;
    private bool _patched;
    private bool _visualsSpawned;
    private float _casterX;
    private float _casterY;
    private int _focusIndex = -1;

    public int LastTargetCount { get; private set; }
    public bool CasterInList { get; private set; }
    public int FocusIndex => _focusIndex;
    public int UnitCount => _units.Length;
    public float CasterX => _casterX;
    public float CasterY => _casterY;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded)
        {
            BindSpatialRuntime(ctx);
            PatchFeaturedGraph(ctx);
            SeedMap(ctx);
            ctx.RuntimeApiOverride = _spatialApi;
            ctx.Caster = _caster;
            ctx.SimActors = _units;
            ctx.ActorHealth = new float[_units.Length];
            ctx.Metrics.AgentCount = _units.Length;
            ctx.Metrics.Detail = ctx.Vignette.Beat;
            _seeded = true;
        }

        SpawnStage(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded || _spatialApi == null)
        {
            throw new InvalidOperationException(
                $"Spatial driver for {ctx.Vignette.Op} must Seed with ISpatialQueryService before Tick.");
        }

        GraphOpsNodeExecuteResult result = ExecuteSpatialGraph(ctx);
        LastTargetCount = result.TargetCount;
        if (LastTargetCount <= 0)
        {
            throw new InvalidOperationException(
                $"Spatial gallery '{ctx.Vignette.Op}' returned an empty TargetList.");
        }

        if (NeedsNamedEntity(ctx.Vignette.Op) && result.EntityValue == Entity.Null)
        {
            throw new InvalidOperationException(
                $"Spatial gallery '{ctx.Vignette.Op}' did not name an entity on the TargetList.");
        }

        MarkInRange(result);
        FillCaptions(ctx);
        ctx.Metrics.Detail = FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        SyncStage(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        DrawFeaturedShape(ctx.Vignette.Op, debugDraw);
        if (_focusIndex >= 0 && _focusIndex < _units.Length)
        {
            GraphShowcaseStagePresenter.DrawAggroLine(
                debugDraw,
                _casterX,
                _casterY,
                _unitX[_focusIndex],
                _unitY[_focusIndex]);
        }
    }

    private void BindSpatialRuntime(GraphOpsNodeDriverContext ctx)
    {
        TeamManager.SetRelationship(CasterTeamId, EnemyTeamId, TeamRelationship.Hostile);
        TeamManager.SetRelationship(EnemyTeamId, CasterTeamId, TeamRelationship.Hostile);

        _coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);
        _grid = new GridSpatialPartitionWorld(cellSize: 4);
        _spatial = new SpatialQueryService(new GridSpatialPartitionBackend(_grid, _coords));
        _spatial.SetCoordinateConverter(_coords);
        _spatial.SetPositionProvider(entity =>
        {
            ref WorldPositionCm pos = ref ctx.SimWorld.Get<WorldPositionCm>(entity);
            return pos.Value.ToWorldCmInt2();
        });

        var tagOps = new TagOps(
            new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
            new TagRuleRegistry());
        var relationships = new RelationshipRuntime(
            ctx.SimWorld,
            new RelationshipTypeRegistry(),
            new RelationshipMetricRegistry(),
            new RelationshipFlagRegistry(),
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(),
            new RelationshipReverseIndex(ctx.SimWorld));
        var entityQueries = new EntitySetQueryRuntime(ctx.SimWorld, tagOps, relationships);
        _spatialApi = new GasGraphRuntimeApi(
            ctx.SimWorld,
            _spatial,
            _coords,
            tagOps: tagOps,
            relationshipRuntime: relationships,
            entityQueries: entityQueries);
        if (_spatial == null || _spatialApi == null)
        {
            throw new InvalidOperationException("Spatial gallery failed to bind ISpatialQueryService.");
        }
    }

    private void PatchFeaturedGraph(GraphOpsNodeDriverContext ctx)
    {
        if (_patched)
        {
            return;
        }

        if (!ctx.Compiled.Package.HasValue)
        {
            throw new InvalidOperationException(
                $"Spatial gallery '{ctx.Vignette.Op}' compiled without a program package; GraphProgramSymbolPatcher cannot run.");
        }

        GraphProgramPackage package = ctx.Compiled.Package.Value;
        GraphProgramSymbolPatcher.Patch(
            package.Symbols,
            package.Program,
            new GraphOpsNodeGallerySymbolResolver(),
            GraphOpsNodeGallerySymbolResolver.Collections);
        _patched = true;
    }

    private void SeedMap(GraphOpsNodeDriverContext ctx)
    {
        WorldCmInt2 origin = _coords!.HexToWorld(new HexCoordinates(0, 0));
        _casterX = origin.X * 0.01f;
        _casterY = origin.Y * 0.01f;
        _caster = SpawnCombatant(ctx, origin.X, origin.Y, CasterTeamId, CasterLayer);

        _units = new Entity[12];
        _unitX = new float[12];
        _unitY = new float[12];
        _unitInRange = new byte[12];
        _unitLabels = new string[12];

        SpawnUnit(ctx, 0, origin.X, origin.Y + 250, CasterTeamId, CasterLayer, "友军");
        SpawnUnit(ctx, 1, origin.X, origin.Y + 450, EnemyTeamId, EnemyLayer, "北面的人");
        SpawnHex(ctx, 2, 0, 1, EnemyTeamId, EnemyLayer, "北格的人");
        SpawnHex(ctx, 3, 1, 0, EnemyTeamId, EnemyLayer, "东格的人");
        SpawnHex(ctx, 4, 1, -1, EnemyTeamId, EnemyLayer, "西南格的人");
        SpawnHex(ctx, 5, -1, 1, EnemyTeamId, EnemyLayer, "西北格的人");
        SpawnHex(ctx, 6, 2, 0, EnemyTeamId, EnemyLayer, "外环的人");
        SpawnUnit(ctx, 7, origin.X + 60, origin.Y + 20, EnemyTeamId, EnemyLayer, "近处的人");
        SpawnUnit(ctx, 8, origin.X - 50, origin.Y + 30, EnemyTeamId, EnemyLayer, "身侧的人");
        SpawnUnit(ctx, 9, origin.X + 180, origin.Y + 180, EnemyTeamId, EnemyLayer, "斜线上的人");
        SpawnUnit(ctx, 10, origin.X + 250, origin.Y + 250, EnemyTeamId, EnemyLayer, "更远斜线的人");
        SpawnUnit(ctx, 11, origin.X - 900, origin.Y, EnemyTeamId, EnemyLayer, "西边的人");
    }

    private void SpawnHex(GraphOpsNodeDriverContext ctx, int index, int q, int r, int teamId, uint layer, string name)
    {
        WorldCmInt2 world = _coords!.HexToWorld(new HexCoordinates(q, r));
        SpawnUnit(ctx, index, world.X, world.Y, teamId, layer, name);
    }

    private void SpawnUnit(GraphOpsNodeDriverContext ctx, int index, int xCm, int yCm, int teamId, uint layer, string name)
    {
        _units[index] = SpawnCombatant(ctx, xCm, yCm, teamId, layer);
        _unitX[index] = xCm * 0.01f;
        _unitY[index] = yCm * 0.01f;
        _unitLabels[index] = name;
    }

    private Entity SpawnCombatant(GraphOpsNodeDriverContext ctx, int xCm, int yCm, int teamId, uint layerCategory)
    {
        Entity entity = ctx.SimWorld.Create(
            new MapEntity(),
            new Team { Id = teamId },
            WorldPositionCm.FromCm(xCm, yCm),
            new EntityLayer(category: layerCategory, mask: uint.MaxValue),
            new BlackboardIntBuffer(),
            new BlackboardEntityBuffer());
        IntVector2 grid = _coords!.WorldToGrid(new WorldCmInt2(xCm, yCm));
        _grid!.Add(entity, new IntRect(grid.X, grid.Y, grid.X + 1, grid.Y + 1));
        return entity;
    }

    private GraphOpsNodeExecuteResult ExecuteSpatialGraph(GraphOpsNodeDriverContext ctx)
    {
        WorldCmInt2 origin = ctx.SimWorld.Get<WorldPositionCm>(_caster).ToWorldCmInt2();
        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
        Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
        var targetList = new GraphTargetList(targets);
        entities[0] = _caster;

        var state = new GraphExecutionState
        {
            World = ctx.SimWorld,
            Caster = _caster,
            ExplicitTarget = Entity.Null,
            TargetPosCm = new IntVector2(origin.X, origin.Y),
            Api = _spatialApi!,
            F = floats,
            I = ints,
            B = bools,
            E = entities,
            Targets = targets,
            TargetList = targetList,
            CallStack = callStack,
            RandomSeed = (uint)(0xA5A5A5A5u ^ (uint)ctx.Wave),
            Status = GraphExecutionStatus.Running
        };

        GasGraphOpHandlerTable.Execute(ref state, ctx.Compiled.Program, GasGraphOpHandlerTable.Instance);
        if (state.Status != GraphExecutionStatus.Halted)
        {
            throw new InvalidOperationException(
                $"Featured graph for {ctx.Vignette.Op} ended with status {state.Status}.");
        }

        int count = state.TargetList.Count;
        ReadOnlySpan<Entity> found = state.TargetList.Span;
        for (int i = 0; i < count; i++)
        {
            _lastTargets[i] = found[i];
        }

        return new GraphOpsNodeExecuteResult(
            floats[ctx.FeaturedDest],
            ints[ctx.FeaturedDest],
            bools[ctx.FeaturedDest] != 0,
            entities[ctx.FeaturedDest],
            state.ReturnInt,
            count);
    }

    private void MarkInRange(GraphOpsNodeExecuteResult result)
    {
        CasterInList = false;
        _focusIndex = -1;
        for (int i = 0; i < _units.Length; i++)
        {
            _unitInRange[i] = 0;
        }

        for (int i = 0; i < LastTargetCount; i++)
        {
            Entity hit = _lastTargets[i];
            if (hit.Equals(_caster))
            {
                CasterInList = true;
                continue;
            }

            int idx = IndexOf(hit);
            if (idx >= 0)
            {
                _unitInRange[idx] = 1;
            }
        }

        if (result.EntityValue != Entity.Null && !result.EntityValue.Equals(_caster))
        {
            _focusIndex = IndexOf(result.EntityValue);
        }
    }

    private void FillCaptions(GraphOpsNodeDriverContext ctx)
    {
        int lit = 0;
        for (int i = 0; i < _unitInRange.Length; i++)
        {
            if (_unitInRange[i] != 0)
            {
                lit++;
            }
        }

        ctx.CaptionValues["count"] = lit.ToString();
        ctx.CaptionValues["self"] = CasterInList ? "名单里有自己" : "名单里没有自己";
        int named = _focusIndex;
        if (named < 0)
        {
            for (int i = 0; i < _units.Length; i++)
            {
                if (_unitInRange[i] != 0)
                {
                    named = i;
                    break;
                }
            }
        }

        ctx.CaptionValues["name"] = named >= 0 ? _unitLabels[named] : "没有人";
        if (ctx.ActorHealth.Length == _units.Length)
        {
            for (int i = 0; i < _units.Length; i++)
            {
                ctx.ActorHealth[i] = _unitInRange[i] != 0 ? 100f : 0f;
            }
        }
    }

    private static bool NeedsNamedEntity(string op)
        => op is nameof(GraphNodeOp.AggMinByDistance) or nameof(GraphNodeOp.TargetListGet);

    private void DrawFeaturedShape(string op, DebugDrawCommandBuffer debugDraw)
    {
        if (UsesConeOverlay(op))
        {
            DrawCone(debugDraw);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.QueryRectangle), StringComparison.Ordinal))
        {
            DrawRectangle(debugDraw);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.QueryLine), StringComparison.Ordinal))
        {
            DrawLine(debugDraw);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.QueryHexRange), StringComparison.Ordinal))
        {
            DrawHexCells(debugDraw, radius: HexRadius, ringOnly: false);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.QueryHexRing), StringComparison.Ordinal))
        {
            DrawHexCells(debugDraw, radius: HexRadius, ringOnly: true);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.QueryHexNeighbors), StringComparison.Ordinal))
        {
            DrawHexCells(debugDraw, radius: 1, ringOnly: true);
        }
    }

    private static bool UsesConeOverlay(string op)
        => op is nameof(GraphNodeOp.QueryCone)
            or nameof(GraphNodeOp.QueryFilterNotEntity)
            or nameof(GraphNodeOp.QueryFilterLayer)
            or nameof(GraphNodeOp.QueryFilterRelationship)
            or nameof(GraphNodeOp.AggCount)
            or nameof(GraphNodeOp.AggMinByDistance)
            or nameof(GraphNodeOp.TargetListGet);

    private void DrawCone(DebugDrawCommandBuffer debugDraw)
    {
        const int segments = 10;
        var points = new Vector2[segments + 2];
        points[0] = new Vector2(_casterX, _casterY);
        float rangeM = ConeRangeCm * 0.01f;
        float start = (ConeDirDeg - ConeHalfDeg) * MathF.PI / 180f;
        float step = (ConeHalfDeg * 2f) * MathF.PI / 180f / segments;
        for (int i = 0; i <= segments; i++)
        {
            float a = start + step * i;
            points[i + 1] = new Vector2(
                _casterX + MathF.Cos(a) * rangeM,
                _casterY + MathF.Sin(a) * rangeM);
        }

        GraphShowcaseStagePresenter.DrawPolyline(debugDraw, points, GraphShowcaseStagePresenter.SentryAlert, thickness: 0.1f);
    }

    private void DrawRectangle(DebugDrawCommandBuffer debugDraw)
    {
        float hw = RectHalfWidthCm * 0.01f;
        float hh = RectHalfHeightCm * 0.01f;
        float rad = RectRotationDeg * MathF.PI / 180f;
        float c = MathF.Cos(rad);
        float s = MathF.Sin(rad);
        var local = new Vector2[]
        {
            new(-hw, -hh),
            new(hw, -hh),
            new(hw, hh),
            new(-hw, hh)
        };
        var world = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            world[i] = new Vector2(
                _casterX + local[i].X * c - local[i].Y * s,
                _casterY + local[i].X * s + local[i].Y * c);
        }

        GraphShowcaseStagePresenter.DrawPolyline(debugDraw, world, GraphShowcaseStagePresenter.SentryAlert, thickness: 0.1f);
    }

    private void DrawLine(DebugDrawCommandBuffer debugDraw)
    {
        float rad = LineDirDeg * MathF.PI / 180f;
        float len = LineLengthCm * 0.01f;
        var points = new Vector2[]
        {
            new(_casterX, _casterY),
            new(_casterX + MathF.Cos(rad) * len, _casterY + MathF.Sin(rad) * len)
        };
        GraphShowcaseStagePresenter.DrawPolyline(debugDraw, points, GraphShowcaseStagePresenter.SentryAlert, thickness: 0.14f);
    }

    private void DrawHexCells(DebugDrawCommandBuffer debugDraw, int radius, bool ringOnly)
    {
        var center = new HexCoordinates(0, 0);
        int count = ringOnly ? HexCoordinates.RingCount(radius) : HexCoordinates.RangeCount(radius);
        Span<HexCoordinates> hexes = stackalloc HexCoordinates[count];
        int written = ringOnly
            ? HexCoordinates.GetRing(center, radius, hexes)
            : HexCoordinates.GetRange(center, radius, hexes);
        for (int i = 0; i < written; i++)
        {
            DrawOneHex(debugDraw, hexes[i]);
        }
    }

    private void DrawOneHex(DebugDrawCommandBuffer debugDraw, HexCoordinates hex)
    {
        WorldCmInt2 world = _coords!.HexToWorld(hex);
        float cx = world.X * 0.01f;
        float cy = world.Y * 0.01f;
        float size = HexCoordinates.EdgeLengthCm * 0.01f;
        var points = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float a = (60f * i - 30f) * MathF.PI / 180f;
            points[i] = new Vector2(cx + MathF.Cos(a) * size, cy + MathF.Sin(a) * size);
        }

        GraphShowcaseStagePresenter.DrawPolyline(debugDraw, points, GraphShowcaseStagePresenter.PathColor, thickness: 0.08f);
    }

    private void SpawnStage(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Stage == null || _visualsSpawned)
        {
            return;
        }

        _stageCasterProxy = ctx.Stage.Spawn(
            GraphOpsVisualTemplates.Caster,
            "施法者",
            _casterX,
            _casterY,
            100f,
            100f);
        _stageUnitProxies = new Entity[_units.Length];
        ctx.StageProxies = new Entity[_units.Length + 1];
        ctx.StageProxies[0] = _stageCasterProxy;
        for (int i = 0; i < _units.Length; i++)
        {
            string template = i == 0 ? GraphOpsVisualTemplates.Ally : GraphOpsVisualTemplates.Target;
            _stageUnitProxies[i] = ctx.Stage.Spawn(template, _unitLabels[i], _unitX[i], _unitY[i], 100f, 100f);
            ctx.StageProxies[i + 1] = _stageUnitProxies[i];
        }

        _visualsSpawned = true;
    }

    private void SyncStage(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Stage == null || !_visualsSpawned)
        {
            return;
        }

        ctx.Stage.SetHealth(_stageCasterProxy, 100f, 100f);
        ctx.Stage.SetPosition(_stageCasterProxy, _casterX, _casterY);
        for (int i = 0; i < _units.Length; i++)
        {
            ctx.Stage.SetPosition(_stageUnitProxies[i], _unitX[i], _unitY[i]);
            ctx.Stage.SetHealth(_stageUnitProxies[i], _unitInRange[i] != 0 ? 100f : 0f, 100f);
        }
    }

    private int IndexOf(Entity entity)
    {
        for (int i = 0; i < _units.Length; i++)
        {
            if (_units[i].Equals(entity))
            {
                return i;
            }
        }

        return -1;
    }

    private static string FormatDetail(string template, Dictionary<string, string> values)
    {
        string text = template;
        foreach (KeyValuePair<string, string> pair in values)
        {
            text = text.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        }

        if (text.Contains('{', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Detail template still has unsubstituted placeholders: {text}");
        }

        return text;
    }
}
