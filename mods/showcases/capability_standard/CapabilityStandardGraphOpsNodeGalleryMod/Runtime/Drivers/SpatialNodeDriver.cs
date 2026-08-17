using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Spatial;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class SpatialNodeDriver : IGraphOpsNodeDriver
{
    public const int CasterTeamId = 1;
    public const int EnemyTeamId = 2;
    public const uint CasterLayer = GraphOpsNodeGalleryHost.AllyLayer;
    public const uint EnemyLayer = GraphOpsNodeGalleryHost.EnemyLayer;

    private const float HeadTopYOffset = 1.2f;
    private const float BadgeYOffset = 1.7f;
    private const float UnitRadius = 0.85f;
    private const float OutlineDarkThickness = 0.18f;
    private const float OutlineBrightThickness = 0.1f;
    private const float CellGrayThickness = 0.06f;

    // Orange (not pure yellow) so the frame reads against sand terrain after video encoding.
    private static readonly DebugDrawColor FrameOrange = new(255, 140, 0);

    private Entity[] _units = Array.Empty<Entity>();
    private byte[] _unitInRange = Array.Empty<byte>();
    private byte[] _fanCandidate = Array.Empty<byte>();
    private bool _seeded;
    private int _focusIndex = -1;
    private int _casterIndex = -1;
    private bool _casterInFan;
    private float _casterX;
    private float _casterY;
    private float _anchorX;
    private float _anchorY;

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
        _fanCandidate = new byte[_units.Length];
        _casterIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        _casterX = ctx.Vignette.Actors[_casterIndex].X;
        _casterY = ctx.Vignette.Actors[_casterIndex].Y;
        int anchorIndex = AnchorIndex(ctx);
        _anchorX = ctx.Vignette.Actors[anchorIndex].X;
        _anchorY = ctx.Vignette.Actors[anchorIndex].Y;
        WorldCmInt2 origin = ctx.SimWorld.Get<WorldPositionCm>(ctx.SimActors[anchorIndex]).ToWorldCmInt2();
        ctx.TargetPosCm = new IntVector2(origin.X, origin.Y);
        ctx.HasTargetPosCm = true;
        ctx.Metrics.AgentCount = ctx.SimActors.Length;
        ctx.Metrics.Detail = ctx.Vignette.Beat;
        BuildFanCandidates(ctx);
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
        string op = ctx.Vignette.Op;
        if (IsHexOp(op))
        {
            DrawHexStage(ctx, debugDraw);
            return;
        }

        if (UsesConeOverlay(op))
        {
            DrawCone(ctx, debugDraw);
            if (IsFilterOp(op))
            {
                DrawFilterStage(ctx, debugDraw);
            }
            else if (string.Equals(op, nameof(GraphNodeOp.AggCount), StringComparison.Ordinal))
            {
                DrawCountStage(ctx, debugDraw);
            }
            else if (string.Equals(op, nameof(GraphNodeOp.TargetListGet), StringComparison.Ordinal))
            {
                DrawRosterOrderStage(ctx, debugDraw);
            }
            else if (string.Equals(op, nameof(GraphNodeOp.AggMinByDistance), StringComparison.Ordinal))
            {
                DrawDistanceStage(ctx, debugDraw);
            }

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

    private static int AnchorIndex(GraphOpsNodeDriverContext ctx)
    {
        // QueryRectangle frames the ground in front of the caster: the ally standing ahead is
        // the anchor role; every other spatial query stays centered on the caster.
        bool isRectangle = string.Equals(
            ctx.Vignette.Op, nameof(GraphNodeOp.QueryRectangle), StringComparison.Ordinal);
        int index = GraphOpsNodeActorBinding.FindRole(
            ctx.Vignette, isRectangle ? "ally" : "caster");
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Gallery '{ctx.Vignette.Op}' requires an '{(isRectangle ? "ally" : "caster")}' anchor actor.");
        }

        return index;
    }

    private void BuildFanCandidates(GraphOpsNodeDriverContext ctx)
    {
        if (!UsesConeOverlay(ctx.Vignette.Op))
        {
            return;
        }

        (int dirDeg, int halfDeg, float rangeM) = ConeParams(ctx);
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _units.Length; i++)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[i]);
            _fanCandidate[i] = InFan(
                actors[actorIndex].X - _casterX,
                actors[actorIndex].Y - _casterY,
                dirDeg, halfDeg, rangeM) ? (byte)1 : (byte)0;
        }

        _casterInFan = true;
    }

    private static bool InFan(float dx, float dy, int dirDeg, int halfDeg, float rangeM)
    {
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance > rangeM)
        {
            return false;
        }

        float angle = MathF.Atan2(dy, dx) * 180f / MathF.PI;
        float delta = MathF.Abs(NormalizeDeg180(angle - dirDeg));
        return delta <= halfDeg;
    }

    private static float NormalizeDeg180(float deg)
    {
        deg = (deg + 180f) % 360f;
        if (deg < 0f)
        {
            deg += 360f;
        }

        return deg - 180f;
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

        ApplyStageLighting(ctx);
    }

    private void ApplyStageLighting(GraphOpsNodeDriverContext ctx)
    {
        string op = ctx.Vignette.Op;
        bool settle = ctx.Wave % 2 == 1;
        bool stageFanBeat = !settle && UsesConeOverlay(op);
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            if (i == _casterIndex)
            {
                continue;
            }

            int unitIndex = IndexOfUnit(ctx.SimActors[i]);
            ctx.ActorHudLit[i] = unitIndex >= 0
                && (stageFanBeat ? _fanCandidate[unitIndex] != 0 : _unitInRange[unitIndex] != 0);
        }

        if (_casterIndex >= 0)
        {
            ctx.ActorHudLit[_casterIndex] = CasterInList
                || (stageFanBeat && IsFilterNotEntity(op) && _casterInFan);
        }
    }

    private int LitUnitCount()
    {
        int lit = 0;
        for (int i = 0; i < _unitInRange.Length; i++)
        {
            if (_unitInRange[i] != 0)
            {
                lit++;
            }
        }

        return lit;
    }

    private static bool NeedsNamedEntity(string op)
        => op is nameof(GraphNodeOp.AggMinByDistance) or nameof(GraphNodeOp.TargetListGet);

    private static bool UsesConeOverlay(string op)
        => op is nameof(GraphNodeOp.QueryCone)
            or nameof(GraphNodeOp.QueryFilterNotEntity)
            or nameof(GraphNodeOp.QueryFilterLayer)
            or nameof(GraphNodeOp.QueryFilterRelationship)
            or nameof(GraphNodeOp.AggCount)
            or nameof(GraphNodeOp.AggMinByDistance)
            or nameof(GraphNodeOp.TargetListGet);

    private static bool IsFilterOp(string op)
        => op is nameof(GraphNodeOp.QueryFilterNotEntity)
            or nameof(GraphNodeOp.QueryFilterLayer)
            or nameof(GraphNodeOp.QueryFilterRelationship);

    private static bool IsFilterNotEntity(string op)
        => op == nameof(GraphNodeOp.QueryFilterNotEntity);

    private static bool IsHexOp(string op)
        => op is nameof(GraphNodeOp.QueryHexRange)
            or nameof(GraphNodeOp.QueryHexRing)
            or nameof(GraphNodeOp.QueryHexNeighbors);

    private (int DirDeg, int HalfDeg, float RangeM) ConeParams(GraphOpsNodeDriverContext ctx)
    {
        GraphInstruction cone = RequireInstruction(ctx, GraphNodeOp.QueryCone);
        return (RequireConstInt(ctx, cone.A), RequireConstInt(ctx, cone.B), cone.ImmF * 0.01f);
    }

    private void DrawCone(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        (int dirDeg, int halfDeg, float rangeM) = ConeParams(ctx);
        float start = (dirDeg - halfDeg) * MathF.PI / 180f;
        float end = (dirDeg + halfDeg) * MathF.PI / 180f;
        if (ctx.Wave % 2 == 0)
        {
            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw, _casterX, _casterY,
                _casterX + MathF.Cos(start) * rangeM, _casterY + MathF.Sin(start) * rangeM,
                OutlineBrightThickness, GraphShowcaseStagePresenter.SentryAlert, arrowEnd: false);
            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw, _casterX, _casterY,
                _casterX + MathF.Cos(end) * rangeM, _casterY + MathF.Sin(end) * rangeM,
                OutlineBrightThickness, GraphShowcaseStagePresenter.SentryAlert, arrowEnd: false);
            GraphShowcaseStagePresenter.DrawArcArrow(
                debugDraw, _casterX, _casterY, rangeM, dirDeg - halfDeg, dirDeg + halfDeg,
                GraphShowcaseStagePresenter.SentryAlert);
            return;
        }

        Vector2[] points = FanOutlinePoints(dirDeg, halfDeg, rangeM);
        GraphShowcaseStagePresenter.DrawPolyline(
            debugDraw, points, GraphShowcaseStagePresenter.OutlineDark, OutlineDarkThickness);
        GraphShowcaseStagePresenter.DrawPolyline(
            debugDraw, points, GraphShowcaseStagePresenter.SentryAlert, OutlineBrightThickness);
        float rad = dirDeg * MathF.PI / 180f;
        float ux = MathF.Cos(rad);
        float uy = MathF.Sin(rad);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            _casterX + ux * (rangeM - 1.1f), _casterY + uy * (rangeM - 1.1f),
            _casterX + ux * (rangeM + 0.5f), _casterY + uy * (rangeM + 0.5f),
            0.14f, GraphShowcaseStagePresenter.SentryAlert);
    }

    private Vector2[] FanOutlinePoints(int dirDeg, int halfDeg, float rangeM)
    {
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

        return points;
    }

    private void DrawFilterStage(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        string op = ctx.Vignette.Op;
        bool settle = ctx.Wave % 2 == 1;
        bool byRelationship = string.Equals(
            op, nameof(GraphNodeOp.QueryFilterRelationship), StringComparison.Ordinal);
        GraphShowcaseStagePresenter.BadgeKind badgeKind = byRelationship
            ? GraphShowcaseStagePresenter.BadgeKind.Flag
            : GraphShowcaseStagePresenter.BadgeKind.Diamond;
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _units.Length; i++)
        {
            if (IsFilterNotEntity(op))
            {
                break;
            }

            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[i]);
            if (actorIndex < 0)
            {
                continue;
            }

            float x = actors[actorIndex].X;
            float y = actors[actorIndex].Y;
            if (settle)
            {
                if (_unitInRange[i] != 0)
                {
                    GraphShowcaseStagePresenter.DrawBadge(
                        debugDraw, x, y + BadgeYOffset, badgeKind,
                        GraphShowcaseStagePresenter.EnemyColor, scale: 1.1f);
                }
                else
                {
                    GraphShowcaseStagePresenter.DrawGhostCircle(
                        debugDraw, x, y, UnitRadius, GraphShowcaseStagePresenter.GhostColor);
                }
            }
            else
            {
                bool keeps = byRelationship
                    ? IsHostileToCaster(ctx, _units[i])
                    : IsEnemyLayerUnit(ctx, _units[i]);
                GraphShowcaseStagePresenter.DrawBadge(
                    debugDraw, x, y + BadgeYOffset, badgeKind,
                    keeps ? GraphShowcaseStagePresenter.EnemyColor : GraphShowcaseStagePresenter.GuardColor,
                    scale: 1.1f);
            }
        }

        if (IsFilterNotEntity(op))
        {
            if (settle)
            {
                GraphShowcaseStagePresenter.DrawGhostCircle(
                    debugDraw, _casterX, _casterY, 0.7f, GraphShowcaseStagePresenter.GhostColor);
            }
            else
            {
                GraphShowcaseStagePresenter.DrawActor(
                    debugDraw, _casterX, _casterY, 0.7f, GraphShowcaseStagePresenter.GateColor, 0.1f);
            }
        }

        int shown = settle ? LitUnitCount() : FanUnitCount() + (IsFilterNotEntity(op) ? 1 : 0);
        float panelX = _casterX - 2.4f;
        float panelY = _casterY - 1.15f;
        GraphShowcaseStagePresenter.DrawPanelBox(
            debugDraw, panelX, panelY, 1.0f, 0.7f, 1, GraphShowcaseStagePresenter.GateColor);
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, panelX + 0.32f, panelY, shown, 0.5f, GraphShowcaseStagePresenter.SentryAlert);
    }

    private int FanUnitCount()
    {
        int count = 0;
        for (int i = 0; i < _fanCandidate.Length; i++)
        {
            if (_fanCandidate[i] != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsHostileToCaster(GraphOpsNodeDriverContext ctx, Entity unit)
    {
        if (!ctx.SimWorld.Has<Team>(ctx.Caster) || !ctx.SimWorld.Has<Team>(unit))
        {
            return false;
        }

        return TeamManager.GetRelationship(
            ctx.SimWorld.Get<Team>(ctx.Caster).Id,
            ctx.SimWorld.Get<Team>(unit).Id) == TeamRelationship.Hostile;
    }

    private static bool IsEnemyLayerUnit(GraphOpsNodeDriverContext ctx, Entity unit)
    {
        return ctx.SimWorld.Has<EntityLayer>(unit)
            && ctx.SimWorld.Get<EntityLayer>(unit).Value.Category == EnemyLayer;
    }

    private void DrawCountStage(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        bool settle = ctx.Wave % 2 == 1;
        int total = LitUnitCount();
        const float pitch = 0.24f;
        float railY = _casterY + 1.55f;
        float width = MathF.Max(total, 1) * pitch;
        GraphShowcaseStagePresenter.DrawPolyline(
            debugDraw,
            new[]
            {
                new Vector2(_casterX - width * 0.5f, railY),
                new Vector2(_casterX + width * 0.5f, railY)
            },
            GraphShowcaseStagePresenter.GhostColor,
            0.04f);
        if (!settle)
        {
            return;
        }

        for (int k = 0; k < total; k++)
        {
            float x = _casterX + (k - (total - 1) * 0.5f) * pitch;
            GraphShowcaseStagePresenter.DrawPolyline(
                debugDraw,
                new[]
                {
                    new Vector2(x, railY + 0.06f),
                    new Vector2(x, railY + 0.48f)
                },
                GraphShowcaseStagePresenter.SentryAlert,
                0.07f);
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _units.Length; i++)
        {
            if (_unitInRange[i] == 0)
            {
                continue;
            }

            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[i]);
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(actors[actorIndex].X, actors[actorIndex].Y + HeadTopYOffset + 0.2f),
                HalfWidth = 0.09f,
                HalfHeight = 0.09f,
                Thickness = 0.05f,
                Color = GraphShowcaseStagePresenter.SentryAlert
            });
        }
    }

    private void DrawRosterOrderStage(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        bool settle = ctx.Wave % 2 == 1;
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[i]);
            if (actorIndex < 0)
            {
                continue;
            }

            float x = actors[actorIndex].X;
            float y = actors[actorIndex].Y;
            bool first = i == 0;
            GraphShowcaseStagePresenter.DrawRankPips(
                debugDraw, x, y + HeadTopYOffset, ctx.HitTargetCount - i,
                settle && first
                    ? GraphShowcaseStagePresenter.SentryAlert
                    : GraphShowcaseStagePresenter.GhostColor);
            if (settle && first)
            {
                GraphShowcaseStagePresenter.DrawThickOutlineCircle(
                    debugDraw, x, y, UnitRadius + 0.1f,
                    DebugDrawColor.White, GraphShowcaseStagePresenter.SentryAlert);
                GraphShowcaseStagePresenter.DrawAggroLine(debugDraw, _casterX, _casterY, x, y);
            }
        }
    }

    private void DrawDistanceStage(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        bool settle = ctx.Wave % 2 == 1;
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _units.Length; i++)
        {
            if (_unitInRange[i] == 0)
            {
                continue;
            }

            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[i]);
            if (actorIndex < 0)
            {
                continue;
            }

            float x = actors[actorIndex].X;
            float y = actors[actorIndex].Y;
            bool winner = settle && i == _focusIndex;
            if (winner)
            {
                GraphShowcaseStagePresenter.DrawDirectedLine(
                    debugDraw, _casterX, _casterY, x, y, 0.16f,
                    GraphShowcaseStagePresenter.SentryCombat);
                float dx = x - _casterX;
                float dy = y - _casterY;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > 0.5f)
                {
                    float ux = dx / dist;
                    float uy = dy / dist;
                    for (float d = 1f; d < dist; d += 1f)
                    {
                        float px = _casterX + ux * d;
                        float py = _casterY + uy * d;
                        GraphShowcaseStagePresenter.DrawPolyline(
                            debugDraw,
                            new[]
                            {
                                new Vector2(px - uy * 0.15f, py + ux * 0.15f),
                                new Vector2(px + uy * 0.15f, py - ux * 0.15f)
                            },
                            GraphShowcaseStagePresenter.SentryCombat,
                            0.07f);
                    }
                }
            }
            else if (settle)
            {
                GraphShowcaseStagePresenter.DrawGhostSegment(
                    debugDraw, _casterX, _casterY, x, y, GraphShowcaseStagePresenter.GhostColor);
            }
            else
            {
                GraphShowcaseStagePresenter.DrawPolyline(
                    debugDraw,
                    new[] { new Vector2(_casterX, _casterY), new Vector2(x, y) },
                    GraphShowcaseStagePresenter.GhostColor,
                    0.06f);
            }
        }
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
                _anchorX + local[i].X * c - local[i].Y * s,
                _anchorY + local[i].X * s + local[i].Y * c);
        }

        if (ctx.Wave % 2 == 0)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = world[i];
                Vector2 b = world[(i + 1) % 4];
                GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                    debugDraw, a.X, a.Y, b.X, b.Y, OutlineBrightThickness, FrameOrange,
                    arrowStart: false, arrowEnd: false);
            }

            return;
        }

        GraphShowcaseStagePresenter.DrawPolyline(
            debugDraw, world, GraphShowcaseStagePresenter.OutlineDark, OutlineDarkThickness);
        GraphShowcaseStagePresenter.DrawPolyline(debugDraw, world, FrameOrange, OutlineBrightThickness);
        for (int i = 0; i < 4; i++)
        {
            Vector2 corner = world[i];
            Vector2 outward = Vector2.Normalize(corner - new Vector2(_anchorX, _anchorY));
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                corner.X + outward.X * 0.6f, corner.Y + outward.Y * 0.6f,
                corner.X - outward.X * 0.25f, corner.Y - outward.Y * 0.25f,
                0.09f, FrameOrange);
        }
    }

    private void DrawLine(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        GraphInstruction line = RequireInstruction(ctx, GraphNodeOp.QueryLine);
        int dirDeg = RequireConstInt(ctx, line.A);
        float len = RequireConstInt(ctx, line.B) * 0.01f;
        float halfWidth = line.Imm * 0.01f;
        float rad = dirDeg * MathF.PI / 180f;
        float ux = MathF.Cos(rad);
        float uy = MathF.Sin(rad);
        float endX = _casterX + ux * len;
        float endY = _casterY + uy * len;
        float px = -uy;
        float py = ux;
        bool settle = ctx.Wave % 2 == 1;
        if (settle)
        {
            GraphShowcaseStagePresenter.DrawPolyline(
                debugDraw,
                new[] { new Vector2(_casterX, _casterY), new Vector2(endX, endY) },
                GraphShowcaseStagePresenter.OutlineDark,
                OutlineDarkThickness);
            GraphShowcaseStagePresenter.DrawPolyline(
                debugDraw,
                new[] { new Vector2(_casterX, _casterY), new Vector2(endX, endY) },
                GraphShowcaseStagePresenter.SentryAlert,
                OutlineBrightThickness);
        }
        else
        {
            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw, _casterX, _casterY, endX, endY, OutlineBrightThickness,
                GraphShowcaseStagePresenter.SentryAlert);
        }

        for (int side = -1; side <= 1; side += 2)
        {
            float ox = px * halfWidth * side;
            float oy = py * halfWidth * side;
            GraphShowcaseStagePresenter.DrawPolyline(
                debugDraw,
                new[]
                {
                    new Vector2(_casterX + ox, _casterY + oy),
                    new Vector2(endX + ox, endY + oy)
                },
                GraphShowcaseStagePresenter.SentryAlert,
                0.06f);
        }

        if (settle)
        {
            DrawNearMiss(ctx, debugDraw, ux, uy, px, py, halfWidth);
        }
    }

    private static void DrawNearMiss(
        GraphOpsNodeDriverContext ctx,
        DebugDrawCommandBuffer debugDraw,
        float ux, float uy, float px, float py, float halfWidth)
    {
        GraphOpsNodeActor? near = null;
        for (int i = 0; i < ctx.Vignette.Actors.Length; i++)
        {
            if (string.Equals(ctx.Vignette.Actors[i].Id, "near", StringComparison.Ordinal))
            {
                near = ctx.Vignette.Actors[i];
                break;
            }
        }

        if (near == null)
        {
            return;
        }

        float casterX = ctx.Vignette.Actors[GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster")].X;
        float casterY = ctx.Vignette.Actors[GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster")].Y;
        float dx = near.X - casterX;
        float dy = near.Y - casterY;
        float along = dx * ux + dy * uy;
        float across = dx * px + dy * py;
        if (along < 0f || MathF.Abs(across) <= halfWidth)
        {
            return;
        }

        float sign = across > 0f ? 1f : -1f;
        float edgeX = casterX + ux * along + px * halfWidth * sign;
        float edgeY = casterY + uy * along + py * halfWidth * sign;
        GraphShowcaseStagePresenter.DrawPolyline(
            debugDraw,
            new[] { new Vector2(near.X, near.Y), new Vector2(edgeX, edgeY) },
            GraphShowcaseStagePresenter.GhostColor,
            0.06f);
        GraphShowcaseStagePresenter.DrawPolyline(
            debugDraw,
            new[]
            {
                new Vector2(edgeX - ux * 0.16f, edgeY - uy * 0.16f),
                new Vector2(edgeX + ux * 0.16f, edgeY + uy * 0.16f)
            },
            GraphShowcaseStagePresenter.GhostColor,
            0.08f);
    }

    private static void DrawHexStage(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        string op = ctx.Vignette.Op;
        if (string.Equals(op, nameof(GraphNodeOp.QueryHexRange), StringComparison.Ordinal))
        {
            DrawHexCells(ctx, debugDraw, RequireQueryImm(ctx, GraphNodeOp.QueryHexRange),
                ringOnly: false, skipUnoccupiedInner: true);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.QueryHexRing), StringComparison.Ordinal))
        {
            DrawHexCells(ctx, debugDraw, RequireQueryImm(ctx, GraphNodeOp.QueryHexRing),
                ringOnly: true, skipUnoccupiedInner: false);
            return;
        }

        DrawHexCells(ctx, debugDraw, radius: 1, ringOnly: true, skipUnoccupiedInner: false);
    }

    private static void DrawHexCells(
        GraphOpsNodeDriverContext ctx,
        DebugDrawCommandBuffer debugDraw,
        int radius,
        bool ringOnly,
        bool skipUnoccupiedInner)
    {
        ISpatialCoordinateConverter coords = ctx.Coords
            ?? throw new InvalidOperationException("Spatial overlay requires host coordinate converter.");
        if (!ctx.SimWorld.IsAlive(ctx.Caster) || !ctx.SimWorld.Has<WorldPositionCm>(ctx.Caster))
        {
            throw new InvalidOperationException("Hex overlay requires the caster's live WorldPositionCm.");
        }

        HexCoordinates center = coords.WorldToHex(ctx.SimWorld.Get<WorldPositionCm>(ctx.Caster).ToWorldCmInt2());
        var memberCells = new List<HexCoordinates>();
        var memberRings = new List<int>();
        if (ringOnly)
        {
            AddRing(ctx, center, radius, radius, memberCells, memberRings);
        }
        else
        {
            for (int r = 0; r <= radius; r++)
            {
                AddRing(ctx, center, r, r, memberCells, memberRings);
            }
        }

        var occupied = new List<HexCoordinates>();
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            if (!ctx.SimWorld.IsAlive(ctx.SimActors[i]) || !ctx.SimWorld.Has<WorldPositionCm>(ctx.SimActors[i]))
            {
                continue;
            }

            HexCoordinates cell = coords.WorldToHex(ctx.SimWorld.Get<WorldPositionCm>(ctx.SimActors[i]).ToWorldCmInt2());
            if (!occupied.Contains(cell))
            {
                occupied.Add(cell);
            }
        }

        for (int i = 0; i < memberCells.Count; i++)
        {
            if (skipUnoccupiedInner && memberRings[i] < radius && !occupied.Contains(memberCells[i]))
            {
                continue;
            }

            WorldCmInt2 world = coords.HexToWorld(memberCells[i]);
            DrawHexOutline(debugDraw, world.X * 0.01f, world.Y * 0.01f,
                GraphShowcaseStagePresenter.OutlineDark, OutlineDarkThickness);
            DrawHexOutline(debugDraw, world.X * 0.01f, world.Y * 0.01f,
                GraphShowcaseStagePresenter.SentryAlert, OutlineBrightThickness);
        }

        for (int i = 0; i < occupied.Count; i++)
        {
            if (memberCells.Contains(occupied[i]))
            {
                continue;
            }

            WorldCmInt2 world = coords.HexToWorld(occupied[i]);
            DrawHexOutline(debugDraw, world.X * 0.01f, world.Y * 0.01f,
                GraphShowcaseStagePresenter.GhostColor, CellGrayThickness);
        }
    }

    private static void AddRing(
        GraphOpsNodeDriverContext ctx,
        HexCoordinates center,
        int radius,
        int ringIndex,
        List<HexCoordinates> cells,
        List<int> rings)
    {
        int count = radius <= 0 ? 1 : HexCoordinates.RingCount(radius);
        Span<HexCoordinates> ring = stackalloc HexCoordinates[count];
        int written = HexCoordinates.GetRing(center, radius, ring);
        for (int i = 0; i < written; i++)
        {
            cells.Add(ring[i]);
            rings.Add(ringIndex);
        }
    }

    private static void DrawHexOutline(
        DebugDrawCommandBuffer debugDraw, float cx, float cy, DebugDrawColor color, float thickness)
    {
        float size = HexCoordinates.EdgeLengthCm * 0.01f;
        var points = new Vector2[6];
        for (int p = 0; p < 6; p++)
        {
            float a = (60f * p - 30f) * MathF.PI / 180f;
            points[p] = new Vector2(cx + MathF.Cos(a) * size, cy + MathF.Sin(a) * size);
        }

        GraphShowcaseStagePresenter.DrawPolyline(debugDraw, points, color, thickness);
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
