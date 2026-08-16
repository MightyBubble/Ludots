using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class SpatialNodeDriver : IGraphOpsNodeDriver
{
    public const int CasterTeamId = 1;
    public const int EnemyTeamId = 2;
    public const uint CasterLayer = GraphOpsNodeGalleryHost.AllyLayer;
    public const uint EnemyLayer = GraphOpsNodeGalleryHost.EnemyLayer;

    private Entity[] _units = Array.Empty<Entity>();
    private byte[] _unitInRange = Array.Empty<byte>();
    private bool _seeded;
    private int _focusIndex = -1;
    private float _casterX;
    private float _casterY;

    public int LastTargetCount { get; private set; }
    public bool CasterInList { get; private set; }
    public int FocusIndex => _focusIndex;
    public int UnitCount => _units.Length;
    public float CasterX => _casterX;
    public float CasterY => _casterY;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        CollectUnits(ctx);
        _unitInRange = new byte[_units.Length];
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        _casterX = ctx.Vignette.Actors[caster].X;
        _casterY = ctx.Vignette.Actors[caster].Y;
        WorldCmInt2 origin = ctx.SimWorld.Get<WorldPositionCm>(ctx.Caster).ToWorldCmInt2();
        ctx.TargetPosCm = new IntVector2(origin.X, origin.Y);
        ctx.HasTargetPosCm = true;
        ctx.Metrics.AgentCount = ctx.SimActors.Length;
        ctx.Metrics.Detail = ctx.Vignette.Beat;
        _seeded = true;
        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded)
        {
            throw new InvalidOperationException(
                $"Spatial driver for {ctx.Vignette.Op} must Seed before Tick.");
        }

        GraphOpsNodeExecuteResult result = ctx.ExecuteFeaturedGraph();
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

        MarkInRange(ctx, result);
        FillCaptions(ctx);
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        DrawFeaturedShape(ctx, debugDraw);
        if (_focusIndex >= 0 && _focusIndex < _units.Length && !IsHexOp(ctx.Vignette.Op))
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[_focusIndex]);
            GraphShowcaseStagePresenter.DrawAggroLine(
                debugDraw,
                _casterX,
                _casterY,
                ctx.Vignette.Actors[actorIndex].X,
                ctx.Vignette.Actors[actorIndex].Y);
        }
    }

    private void CollectUnits(GraphOpsNodeDriverContext ctx)
    {
        var units = new List<Entity>();
        for (int i = 0; i < ctx.Vignette.Actors.Length; i++)
        {
            if (string.Equals(ctx.Vignette.Actors[i].Role, "caster", StringComparison.Ordinal))
            {
                continue;
            }

            units.Add(ctx.SimActors[i]);
        }

        _units = units.ToArray();
    }

    private void MarkInRange(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        CasterInList = false;
        _focusIndex = -1;
        for (int i = 0; i < _units.Length; i++)
        {
            _unitInRange[i] = 0;
        }

        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            Entity hit = ctx.HitTargets[i];
            if (hit.Equals(ctx.Caster))
            {
                CasterInList = true;
                continue;
            }

            int idx = IndexOfUnit(hit);
            if (idx >= 0)
            {
                _unitInRange[idx] = 1;
            }
        }

        if (result.EntityValue != Entity.Null && !result.EntityValue.Equals(ctx.Caster))
        {
            _focusIndex = IndexOfUnit(result.EntityValue);
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

        if (named >= 0)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[named]);
            ctx.CaptionValues["name"] = ctx.Vignette.Actors[actorIndex].Name;
        }
        else
        {
            ctx.CaptionValues["name"] = "没有人";
        }

        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            if (string.Equals(ctx.Vignette.Actors[i].Role, "caster", StringComparison.Ordinal))
            {
                continue;
            }

            int unitIndex = IndexOfUnit(ctx.SimActors[i]);
            ctx.ActorHudLit[i] = unitIndex >= 0 && _unitInRange[unitIndex] != 0;
        }

        int casterIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        if (casterIndex >= 0)
        {
            ctx.ActorHudLit[casterIndex] = true;
        }
    }

    private static bool NeedsNamedEntity(string op)
        => op is nameof(GraphNodeOp.AggMinByDistance) or nameof(GraphNodeOp.TargetListGet);

    private void DrawFeaturedShape(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        string op = ctx.Vignette.Op;
        if (UsesConeOverlay(op))
        {
            DrawCone(ctx, debugDraw);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.QueryRectangle), StringComparison.Ordinal))
        {
            DrawRectangle(ctx, debugDraw);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.QueryLine), StringComparison.Ordinal))
        {
            DrawLine(ctx, debugDraw);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.QueryHexRange), StringComparison.Ordinal))
        {
            DrawHexCells(ctx, debugDraw, radius: RequireQueryImm(ctx, GraphNodeOp.QueryHexRange), ringOnly: false);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.QueryHexRing), StringComparison.Ordinal))
        {
            DrawHexCells(ctx, debugDraw, radius: RequireQueryImm(ctx, GraphNodeOp.QueryHexRing), ringOnly: true);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.QueryHexNeighbors), StringComparison.Ordinal))
        {
            DrawHexCells(ctx, debugDraw, radius: 1, ringOnly: true);
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

    private void DrawCone(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        GraphInstruction cone = RequireInstruction(ctx, GraphNodeOp.QueryCone);
        int dirDeg = RequireConstInt(ctx, cone.A);
        int halfDeg = RequireConstInt(ctx, cone.B);
        float rangeM = cone.ImmF * 0.01f;
        const int segments = 10;
        var points = new Vector2[segments + 2];
        points[0] = new Vector2(_casterX, _casterY);
        float start = (dirDeg - halfDeg) * MathF.PI / 180f;
        float step = (halfDeg * 2f) * MathF.PI / 180f / segments;
        for (int i = 0; i <= segments; i++)
        {
            float a = start + step * i;
            points[i + 1] = new Vector2(
                _casterX + MathF.Cos(a) * rangeM,
                _casterY + MathF.Sin(a) * rangeM);
        }

        GraphShowcaseStagePresenter.DrawPolyline(debugDraw, points, GraphShowcaseStagePresenter.SentryAlert, thickness: 0.1f);
    }

    private void DrawRectangle(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        GraphInstruction rect = RequireInstruction(ctx, GraphNodeOp.QueryRectangle);
        float hw = RequireConstInt(ctx, rect.A) * 0.01f;
        float hh = RequireConstInt(ctx, rect.B) * 0.01f;
        float rad = rect.Imm * MathF.PI / 180f;
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

    private void DrawLine(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        GraphInstruction line = RequireInstruction(ctx, GraphNodeOp.QueryLine);
        int dirDeg = RequireConstInt(ctx, line.A);
        float len = RequireConstInt(ctx, line.B) * 0.01f;
        float rad = dirDeg * MathF.PI / 180f;
        var points = new Vector2[]
        {
            new(_casterX, _casterY),
            new(_casterX + MathF.Cos(rad) * len, _casterY + MathF.Sin(rad) * len)
        };
        GraphShowcaseStagePresenter.DrawPolyline(debugDraw, points, GraphShowcaseStagePresenter.SentryAlert, thickness: 0.14f);
    }

    private static bool IsHexOp(string op)
        => op is nameof(GraphNodeOp.QueryHexRange)
            or nameof(GraphNodeOp.QueryHexRing)
            or nameof(GraphNodeOp.QueryHexNeighbors);

    private static void DrawHexCells(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int radius, bool ringOnly)
    {
        ISpatialCoordinateConverter coords = ctx.Coords
            ?? throw new InvalidOperationException("Spatial overlay requires host coordinate converter.");
        if (!ctx.SimWorld.IsAlive(ctx.Caster) || !ctx.SimWorld.Has<WorldPositionCm>(ctx.Caster))
        {
            throw new InvalidOperationException("Hex overlay requires the caster's live WorldPositionCm.");
        }

        HexCoordinates center = coords.WorldToHex(ctx.SimWorld.Get<WorldPositionCm>(ctx.Caster).ToWorldCmInt2());
        int count = ringOnly ? HexCoordinates.RingCount(radius) : HexCoordinates.RangeCount(radius);
        Span<HexCoordinates> hexes = stackalloc HexCoordinates[count];
        int written = ringOnly
            ? HexCoordinates.GetRing(center, radius, hexes)
            : HexCoordinates.GetRange(center, radius, hexes);
        for (int i = 0; i < written; i++)
        {
            WorldCmInt2 world = coords.HexToWorld(hexes[i]);
            float cx = world.X * 0.01f;
            float cy = world.Y * 0.01f;
            float size = HexCoordinates.EdgeLengthCm * 0.01f;
            var points = new Vector2[6];
            for (int p = 0; p < 6; p++)
            {
                float a = (60f * p - 30f) * MathF.PI / 180f;
                points[p] = new Vector2(cx + MathF.Cos(a) * size, cy + MathF.Sin(a) * size);
            }

            GraphShowcaseStagePresenter.DrawPolyline(debugDraw, points, GraphShowcaseStagePresenter.SentryAlert, thickness: 0.18f);
        }
    }

    private static GraphInstruction RequireInstruction(GraphOpsNodeDriverContext ctx, GraphNodeOp op)
    {
        GraphInstruction found = default;
        int count = 0;
        GraphInstruction[] program = ctx.Compiled.Program;
        for (int i = 0; i < program.Length; i++)
        {
            if (program[i].Op != (ushort)op)
            {
                continue;
            }

            found = program[i];
            count++;
        }

        if (count != 1)
        {
            throw new InvalidOperationException(
                $"Gallery '{ctx.Vignette.Op}' overlay requires exactly one {op} instruction, found {count}.");
        }

        return found;
    }

    private static int RequireConstInt(GraphOpsNodeDriverContext ctx, byte register)
    {
        GraphInstruction[] program = ctx.Compiled.Program;
        for (int i = 0; i < program.Length; i++)
        {
            if (program[i].Op == (ushort)GraphNodeOp.ConstInt && program[i].Dst == register)
            {
                return program[i].Imm;
            }
        }

        throw new InvalidOperationException(
            $"Gallery '{ctx.Vignette.Op}' overlay missing ConstInt for register {register}.");
    }

    private static int RequireQueryImm(GraphOpsNodeDriverContext ctx, GraphNodeOp op)
    {
        int imm = RequireInstruction(ctx, op).Imm;
        if (imm <= 0)
        {
            throw new InvalidOperationException($"Gallery '{ctx.Vignette.Op}' {op} hexRadius must be positive.");
        }

        return imm;
    }

    private int IndexOfUnit(Entity entity)
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
}
