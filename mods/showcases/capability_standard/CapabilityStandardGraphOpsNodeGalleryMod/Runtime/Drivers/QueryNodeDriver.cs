using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class QueryNodeDriver : IGraphOpsNodeDriver
{
    public const int EnemyTeamId = 2;
    public const int AllyTeamId = 1;
    public const string SquadCollectionKey = GraphOpsNodeGalleryHost.SquadCollectionKey;
    public const float ResidualHealthMax = 40f;

    // Head-top anchor: 0.85m unit radius plus clearance so pips clear both the roster circle and the health bar.
    private const float HeadTopYOffset = 1.2f;
    private const float BadgeYOffset = 1.7f;
    private const float UnitRadius = 0.85f;
    private const float UnitRingThickness = 0.16f;

    private Entity[] _units = Array.Empty<Entity>();
    private byte[] _unitInRange = Array.Empty<byte>();
    private bool _seeded;
    private float _aggregateValue;
    private int[] _azimuthOrder = Array.Empty<int>();
    private float[] _unitAzimuthDeg = Array.Empty<float>();
    private float _scanRadius;

    public int LastTargetCount { get; private set; }
    public int StrongestIndex { get; private set; } = -1;
    public int WeakestIndex { get; private set; } = -1;
    public int UnitCount => _units.Length;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        CollectUnits(ctx);
        _unitInRange = new byte[_units.Length];
        ctx.Metrics.AgentCount = ctx.SimActors.Length;
        ctx.Metrics.Detail = ctx.Vignette.Beat;
        _seeded = true;
        GraphOpsNodeActorBinding.BindHud(ctx);
        BuildScanOrder(ctx);
        if (string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.QueryCollectActiveEffects), StringComparison.Ordinal))
        {
            SeedActiveEffectsOnCaster(ctx);
        }
    }

    private static void SeedActiveEffectsOnCaster(GraphOpsNodeDriverContext ctx)
    {
        Entity caster = ctx.Caster;
        if (!ctx.World.Has<ActiveEffectContainer>(caster))
        {
            ctx.World.Add(caster, new ActiveEffectContainer());
        }

        ref ActiveEffectContainer container = ref ctx.World.Get<ActiveEffectContainer>(caster);
        for (int i = 0; i < 3; i++)
        {
            Entity effect = GameplayEffectFactory.CreateEffect(
                ctx.World,
                rootId: i + 1,
                source: caster,
                target: caster,
                durationTicks: 100,
                lifetimeKind: EffectLifetimeKind.Infinite);
            ctx.World.Get<GameplayEffect>(effect).State = EffectState.Committed;
            if (!container.Add(effect))
            {
                throw new InvalidOperationException("QueryCollectActiveEffects gallery could not seed ActiveEffectContainer.");
            }
        }
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded)
        {
            throw new InvalidOperationException($"Query driver for {ctx.Vignette.Op} must Seed before Tick.");
        }

        GraphOpsNodeExecuteResult result = ctx.ExecuteFeaturedGraph();
        LastTargetCount = result.TargetCount;
        if (LastTargetCount <= 0)
        {
            throw new InvalidOperationException(
                $"Query gallery '{ctx.Vignette.Op}' returned 0 targets; seed or graph failed closed.");
        }

        MarkInRange(ctx);
        LightHitsOnly(ctx);
        ResolveExtremes(ctx, result);
        if (IsAggregateValueOp(ctx.Vignette.Op))
        {
            _aggregateValue = result.FloatValue;
        }

        FillCaptions(ctx, result);
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        if (caster < 0)
        {
            return;
        }

        string op = ctx.Vignette.Op;
        if (string.Equals(op, nameof(GraphNodeOp.QuerySortByAttribute), StringComparison.Ordinal))
        {
            DrawSortRankOverlay(ctx, debugDraw, caster);
            return;
        }

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        DrawSeatRings(debugDraw, casterActor.X, casterActor.Y);

        if (string.Equals(op, nameof(GraphNodeOp.QueryAllMapEntities), StringComparison.Ordinal))
        {
            DrawScanOverlay(ctx, debugDraw, caster);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.QueryFromCollection), StringComparison.Ordinal))
        {
            DrawRosterOverlay(ctx, debugDraw, caster);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.QueryCollectActiveEffects), StringComparison.Ordinal))
        {
            DrawRosterOverlay(ctx, debugDraw, caster);
            return;
        }

        if (IsAggregateValueOp(op))
        {
            DrawValuePanelOverlay(ctx, debugDraw);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.AggMinEntityByAttribute), StringComparison.Ordinal) ||
            string.Equals(op, nameof(GraphNodeOp.AggMaxEntityByAttribute), StringComparison.Ordinal))
        {
            DrawExtremeEntityOverlay(ctx, debugDraw, caster);
            return;
        }

        DrawFilterOverlay(ctx, debugDraw);
    }

    private void DrawSortRankOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        int ranked = Math.Min(3, ctx.HitTargetCount);
        for (int i = 0; i < ranked; i++)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[i]);
            if (actorIndex < 0)
            {
                continue;
            }

            // Rank 1 wears three chevrons; pip count descends with rank so the caption's 三道杠 stays honest.
            float headX = actors[actorIndex].X;
            float headY = actors[actorIndex].Y + HeadTopYOffset;
            GraphShowcaseStagePresenter.DrawRankPips(
                debugDraw,
                headX,
                headY,
                3 - i,
                GraphShowcaseStagePresenter.SentryAlert);
            if (i == 0)
            {
                GraphShowcaseStagePresenter.DrawDirectedLine(
                    debugDraw,
                    actors[caster].X,
                    actors[caster].Y,
                    headX,
                    headY,
                    0.14f,
                    GraphShowcaseStagePresenter.SentryAlert);
            }
        }
    }

    private static void DrawSeatRings(DebugDrawCommandBuffer debugDraw, float x, float y)
    {
        GraphShowcaseStagePresenter.DrawActor(debugDraw, x, y, 0.5f, GraphShowcaseStagePresenter.GhostColor, 0.1f);
        GraphShowcaseStagePresenter.DrawActor(debugDraw, x, y, 0.3f, GraphShowcaseStagePresenter.SentryAlert, 0.12f);
    }

    private void DrawFilterOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        bool resultWave = ctx.Wave % 2 == 1;
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _units.Length; i++)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[i]);
            if (actorIndex < 0)
            {
                continue;
            }

            float x = actors[actorIndex].X;
            float y = actors[actorIndex].Y;
            if (!resultWave || _unitInRange[i] != 0)
            {
                DrawUnitCircle(debugDraw, x, y);
            }
            else
            {
                GraphShowcaseStagePresenter.DrawGhostCircle(debugDraw, x, y, UnitRadius, GraphShowcaseStagePresenter.GhostColor);
            }
        }

        DrawTagBadges(ctx, debugDraw);
    }

    private void DrawTagBadges(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        bool any = string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.QueryFilterTagAny), StringComparison.Ordinal);
        bool none = string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.QueryFilterTagNone), StringComparison.Ordinal);
        if (!any && !none)
        {
            return;
        }

        string tag = any ? "Enemy" : "Dead";
        DebugDrawColor color = any ? GraphShowcaseStagePresenter.EnemyColor : GraphShowcaseStagePresenter.GhostColor;
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _units.Length; i++)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[i]);
            if (actorIndex < 0 || !HasTag(actors[actorIndex], tag))
            {
                continue;
            }

            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw,
                actors[actorIndex].X,
                actors[actorIndex].Y + BadgeYOffset,
                GraphShowcaseStagePresenter.BadgeKind.Diamond,
                color,
                scale: 1.1f);
        }
    }

    private static bool HasTag(GraphOpsNodeActor actor, string tag)
    {
        for (int i = 0; i < actor.Tags.Length; i++)
        {
            if (string.Equals(actor.Tags[i], tag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void DrawRosterOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        GraphOpsNodeCollection roster = ctx.Vignette.Collections[0];
        float bx = actors[caster].X + 4.6f;
        float by = actors[caster].Y - 0.2f;
        const float boardWidth = 3.2f;
        const float boardHeight = 1.5f;
        GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, bx, by, boardWidth, boardHeight, 2, GraphShowcaseStagePresenter.GateColor);
        float columnWidth = boardWidth / 3f;
        for (int column = 1; column < 3; column++)
        {
            float sx = bx - boardWidth * 0.5f + columnWidth * column;
            RawLine(debugDraw, sx, by - boardHeight * 0.5f, sx, by + boardHeight * 0.5f, 0.05f, GraphShowcaseStagePresenter.GateColor);
        }

        bool resultWave = ctx.Wave % 2 == 1;
        for (int m = 0; m < roster.Members.Length; m++)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOfId(ctx.Vignette, roster.Members[m]);
            (float cellX, float cellY) = RosterCell(bx, by, boardWidth, boardHeight, m);
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(cellX, cellY),
                HalfWidth = 0.16f,
                HalfHeight = 0.16f,
                Thickness = 0.05f,
                Color = GraphShowcaseStagePresenter.SentryAlert
            });
            if (resultWave)
            {
                GraphShowcaseStagePresenter.DrawDirectedLine(
                    debugDraw,
                    cellX,
                    cellY,
                    actors[actorIndex].X,
                    actors[actorIndex].Y,
                    0.07f,
                    GraphShowcaseStagePresenter.SentryAlert);
            }
        }

        for (int i = 0; i < _units.Length; i++)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[i]);
            if (actorIndex < 0)
            {
                continue;
            }

            float x = actors[actorIndex].X;
            float y = actors[actorIndex].Y;
            if (!resultWave || _unitInRange[i] != 0)
            {
                DrawUnitCircle(debugDraw, x, y);
            }
            else
            {
                GraphShowcaseStagePresenter.DrawGhostCircle(debugDraw, x, y, UnitRadius, GraphShowcaseStagePresenter.GhostColor);
            }
        }
    }

    private static (float X, float Y) RosterCell(float bx, float by, float boardWidth, float boardHeight, int member)
    {
        int column = member % 3;
        int row = member / 3;
        float x = bx - boardWidth * 0.5f + boardWidth * (column + 0.5f) / 3f;
        float y = by - boardHeight * 0.5f + boardHeight * (row + 0.5f) / 2f;
        return (x, y);
    }

    private void DrawScanOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        bool resultWave = ctx.Wave % 2 == 1;
        int swept = resultWave ? Math.Min(_units.Length, (ctx.Wave + 1) / 2) : _units.Length;
        for (int k = 0; k < swept; k++)
        {
            int i = _azimuthOrder[k];
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[i]);
            if (actorIndex < 0)
            {
                continue;
            }

            float x = actors[actorIndex].X;
            float y = actors[actorIndex].Y;
            DrawUnitCircle(debugDraw, x, y);
            GraphShowcaseStagePresenter.DrawNumber(
                debugDraw,
                x + 0.25f,
                y + HeadTopYOffset,
                (int)ctx.ActorHealth[actorIndex],
                0.36f,
                GraphShowcaseStagePresenter.SentryAlert);
        }

        if (swept > 1)
        {
            GraphShowcaseStagePresenter.DrawArcArrow(
                debugDraw,
                actors[caster].X,
                actors[caster].Y,
                _scanRadius,
                _unitAzimuthDeg[_azimuthOrder[0]],
                _unitAzimuthDeg[_azimuthOrder[swept - 1]],
                GraphShowcaseStagePresenter.SentryAlert);
        }

        int counter = resultWave ? swept + 1 : LastTargetCount;
        float counterX = actors[caster].X - 2.3f;
        float counterY = actors[caster].Y - 0.7f;
        GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, counterX, counterY, 1.1f, 0.7f, 1, GraphShowcaseStagePresenter.GateColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, counterX + 0.35f, counterY, counter, 0.5f, GraphShowcaseStagePresenter.SentryAlert);
    }

    private void DrawValuePanelOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        string op = ctx.Vignette.Op;
        int cells = string.Equals(op, nameof(GraphNodeOp.AggAverageAttribute), StringComparison.Ordinal) ? 3
            : string.Equals(op, nameof(GraphNodeOp.AggSumAttribute), StringComparison.Ordinal) ? 2
            : 1;
        float panelWidth = cells == 3 ? 3.3f : cells == 2 ? 2.4f : 1.5f;
        const float panelHeight = 0.9f;
        const float panelX = 6.8f;
        const float panelY = 2.6f;
        GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, panelX, panelY, panelWidth, panelHeight, cells, GraphShowcaseStagePresenter.GateColor);

        bool resultWave = ctx.Wave % 2 == 1;
        if (resultWave)
        {
            if (cells > 1)
            {
                DrawFeedLines(ctx, debugDraw, panelX, panelY);
            }
        }
        else
        {
            DrawAllUnitCircles(ctx, debugDraw);
        }

        int litCells = Math.Max(1, Math.Min(cells, (ctx.Wave + 1) / 2));
        for (int c = 0; c < cells; c++)
        {
            if (c >= litCells)
            {
                continue;
            }

            float cellX = panelX - panelWidth * 0.5f + panelWidth * (c + 0.5f) / cells;
            GraphShowcaseStagePresenter.DrawNumber(
                debugDraw,
                cellX + 0.4f,
                panelY,
                ValueCellNumber(ctx, op, c),
                0.5f,
                GraphShowcaseStagePresenter.SentryAlert);
        }

        if (cells == 1)
        {
            bool isMin = string.Equals(op, nameof(GraphNodeOp.AggMinAttribute), StringComparison.Ordinal);
            DrawMinMaxMarker(debugDraw, panelX, panelY + panelHeight * 0.5f + 0.55f, isMin);
        }
    }

    private int ValueCellNumber(GraphOpsNodeDriverContext ctx, string op, int cell)
    {
        if (string.Equals(op, nameof(GraphNodeOp.AggSumAttribute), StringComparison.Ordinal))
        {
            return cell == 0 ? LastTargetCount : (int)_aggregateValue;
        }

        if (string.Equals(op, nameof(GraphNodeOp.AggAverageAttribute), StringComparison.Ordinal))
        {
            return cell switch
            {
                0 => LastTargetCount,
                1 => (int)SumHitHealth(ctx),
                _ => (int)Math.Round(_aggregateValue)
            };
        }

        return (int)_aggregateValue;
    }

    private void DrawAllUnitCircles(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _units.Length; i++)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[i]);
            if (actorIndex < 0)
            {
                continue;
            }

            DrawUnitCircle(debugDraw, actors[actorIndex].X, actors[actorIndex].Y);
        }
    }

    private void DrawFeedLines(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, float panelX, float panelY)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[i]);
            if (actorIndex < 0)
            {
                continue;
            }

            GraphShowcaseStagePresenter.DrawGhostSegment(
                debugDraw,
                actors[actorIndex].X,
                actors[actorIndex].Y,
                panelX,
                panelY,
                GraphShowcaseStagePresenter.GhostColor);
        }
    }

    private static void DrawMinMaxMarker(DebugDrawCommandBuffer debugDraw, float x, float y, bool isMin)
    {
        if (isMin)
        {
            RawLine(debugDraw, x, y - 0.35f, x - 0.3f, y + 0.25f, 0.08f, GraphShowcaseStagePresenter.SentryAlert);
            RawLine(debugDraw, x, y - 0.35f, x + 0.3f, y + 0.25f, 0.08f, GraphShowcaseStagePresenter.SentryAlert);
            RawLine(debugDraw, x - 0.3f, y + 0.25f, x + 0.3f, y + 0.25f, 0.08f, GraphShowcaseStagePresenter.SentryAlert);
        }
        else
        {
            RawLine(debugDraw, x, y + 0.35f, x - 0.3f, y - 0.25f, 0.08f, GraphShowcaseStagePresenter.SentryAlert);
            RawLine(debugDraw, x, y + 0.35f, x + 0.3f, y - 0.25f, 0.08f, GraphShowcaseStagePresenter.SentryAlert);
            RawLine(debugDraw, x - 0.3f, y - 0.25f, x + 0.3f, y - 0.25f, 0.08f, GraphShowcaseStagePresenter.SentryAlert);
        }
    }

    private void DrawExtremeEntityOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        int named = StrongestIndex >= 0 ? StrongestIndex : WeakestIndex;
        if (named < 0)
        {
            return;
        }

        bool resultWave = ctx.Wave % 2 == 1;
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _units.Length; i++)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[i]);
            if (actorIndex < 0)
            {
                continue;
            }

            float x = actors[actorIndex].X;
            float y = actors[actorIndex].Y;
            if (!resultWave || i == named)
            {
                DrawUnitCircle(debugDraw, x, y);
            }
            else
            {
                GraphShowcaseStagePresenter.DrawGhostCircle(debugDraw, x, y, UnitRadius, GraphShowcaseStagePresenter.GhostColor);
            }
        }

        if (!resultWave)
        {
            return;
        }

        int targetIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[named]);
        GraphOpsNodeActor target = actors[targetIndex];
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            actors[caster].X,
            actors[caster].Y,
            target.X,
            target.Y + HeadTopYOffset,
            0.12f,
            GraphShowcaseStagePresenter.SentryAlert);
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw,
            target.X,
            target.Y + BadgeYOffset,
            GraphShowcaseStagePresenter.BadgeKind.Diamond,
            GraphShowcaseStagePresenter.EnemyColor,
            scale: 1.1f);
        float hp = ctx.ActorHealth[targetIndex];
        float healthMax = target.HealthMax > 0f ? target.HealthMax : target.Health;
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw,
            target.X + 0.3f,
            target.Y + 1.35f,
            (int)hp,
            0.4f,
            GraphShowcaseStagePresenter.SentryAlert);
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw,
            target.X + 1.0f,
            target.Y + 1.35f,
            (int)healthMax,
            0.32f,
            GraphShowcaseStagePresenter.GhostColor);
    }

    private static void DrawUnitCircle(DebugDrawCommandBuffer debugDraw, float x, float y)
    {
        GraphShowcaseStagePresenter.DrawActor(debugDraw, x, y, UnitRadius, GraphShowcaseStagePresenter.SentryAlert, UnitRingThickness);
    }

    private static void RawLine(DebugDrawCommandBuffer debugDraw, float ax, float ay, float bx, float by, float thickness, DebugDrawColor color)
    {
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(ax, ay),
            B = new Vector2(bx, by),
            Thickness = thickness,
            Color = color
        });
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

    private void BuildScanOrder(GraphOpsNodeDriverContext ctx)
    {
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        if (caster < 0 || _units.Length == 0)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        float casterX = actors[caster].X;
        float casterY = actors[caster].Y;
        _azimuthOrder = new int[_units.Length];
        _unitAzimuthDeg = new float[_units.Length];
        _scanRadius = 0f;
        for (int i = 0; i < _units.Length; i++)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[i]);
            float dx = actors[actorIndex].X - casterX;
            float dy = actors[actorIndex].Y - casterY;
            _unitAzimuthDeg[i] = MathF.Atan2(dy, dx) * 180f / MathF.PI;
            _scanRadius = MathF.Max(_scanRadius, MathF.Sqrt(dx * dx + dy * dy));
            _azimuthOrder[i] = i;
        }

        Array.Sort(_azimuthOrder, (a, b) => _unitAzimuthDeg[a].CompareTo(_unitAzimuthDeg[b]));
        _scanRadius += 0.5f;
    }

    private void MarkInRange(GraphOpsNodeDriverContext ctx)
    {
        for (int i = 0; i < _units.Length; i++)
        {
            _unitInRange[i] = 0;
        }

        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            int idx = IndexOfUnit(ctx.HitTargets[i]);
            if (idx >= 0)
            {
                _unitInRange[idx] = 1;
            }
        }
    }

    private void ResolveExtremes(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        StrongestIndex = -1;
        WeakestIndex = -1;
        if (string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.AggMaxEntityByAttribute), StringComparison.Ordinal))
        {
            if (result.EntityValue == Entity.Null)
            {
                throw new InvalidOperationException("AggMaxEntityByAttribute did not name an entity.");
            }

            StrongestIndex = RequireIndex(result.EntityValue, ctx.Vignette.Op);
            return;
        }

        if (string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.AggMinEntityByAttribute), StringComparison.Ordinal))
        {
            if (result.EntityValue == Entity.Null)
            {
                throw new InvalidOperationException("AggMinEntityByAttribute did not name an entity.");
            }

            WeakestIndex = RequireIndex(result.EntityValue, ctx.Vignette.Op);
            return;
        }

        if (string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.QuerySortByAttribute), StringComparison.Ordinal))
        {
            StrongestIndex = RequireIndex(ctx.HitTargets[0], ctx.Vignette.Op);
        }
    }

    private void FillCaptions(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        string op = ctx.Vignette.Op;
        ctx.CaptionValues["count"] = CountFor(op).ToString();
        ctx.CaptionValues["threshold"] = ResidualHealthMax.ToString("0");
        if (string.Equals(op, nameof(GraphNodeOp.QueryFromCollection), StringComparison.Ordinal))
        {
            ctx.CaptionValues["rest"] = (_units.Length - CountFor(op)).ToString();
        }

        if (string.Equals(op, nameof(GraphNodeOp.AggSumAttribute), StringComparison.Ordinal) ||
            string.Equals(op, nameof(GraphNodeOp.AggAverageAttribute), StringComparison.Ordinal))
        {
            ctx.CaptionValues["sum"] = SumHitHealth(ctx).ToString("0");
        }

        ctx.CaptionValues["avg"] = result.FloatValue.ToString("0");
        ctx.CaptionValues["max"] = result.FloatValue.ToString("0");
        ctx.CaptionValues["min"] = result.FloatValue.ToString("0");

        int named = StrongestIndex >= 0 ? StrongestIndex : WeakestIndex;
        if (named >= 0)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[named]);
            ctx.CaptionValues["label"] = ctx.Vignette.Actors[actorIndex].Name;
            ctx.CaptionValues["hp"] = ctx.ActorHealth[actorIndex].ToString("0");
        }
        else
        {
            ctx.CaptionValues["label"] = "无人";
            ctx.CaptionValues["hp"] = "0";
        }
    }

    private int CountFor(string op)
    {
        return op is nameof(GraphNodeOp.QueryAllMapEntities)
            or nameof(GraphNodeOp.QueryCollectActiveEffects)
            or nameof(GraphNodeOp.AggSumAttribute)
            or nameof(GraphNodeOp.AggAverageAttribute)
            ? LastTargetCount
            : FieldHitCount();
    }

    private int FieldHitCount()
    {
        int count = 0;
        for (int i = 0; i < _units.Length; i++)
        {
            if (_unitInRange[i] != 0)
            {
                count++;
            }
        }

        return count;
    }

    private float SumHitHealth(GraphOpsNodeDriverContext ctx)
    {
        float sum = 0f;
        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            int index = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[i]);
            if (index >= 0)
            {
                sum += ctx.ActorHealth[index];
            }
        }

        return sum;
    }

    private static void LightHitsOnly(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.EnsureHudLitBuffer(ctx);
        Array.Fill(ctx.ActorHudLit, false);
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            int index = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[i]);
            if (index >= 0 && index != caster)
            {
                ctx.ActorHudLit[index] = true;
            }
        }
    }

    private int RequireIndex(Entity entity, string op)
    {
        int idx = IndexOfUnit(entity);
        if (idx < 0)
        {
            throw new InvalidOperationException($"Query gallery '{op}' named an entity that is not on the map.");
        }

        return idx;
    }

    private int IndexOfUnit(Entity entity)
    {
        for (int i = 0; i < _units.Length; i++)
        {
            if (_units[i] == entity)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsAggregateValueOp(string op)
        => op is nameof(GraphNodeOp.AggSumAttribute)
            or nameof(GraphNodeOp.AggAverageAttribute)
            or nameof(GraphNodeOp.AggMaxAttribute)
            or nameof(GraphNodeOp.AggMinAttribute);
}
