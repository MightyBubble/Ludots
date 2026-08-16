using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class QueryNodeDriver : IGraphOpsNodeDriver
{
    public const int EnemyTeamId = 2;
    public const int AllyTeamId = 1;
    public const string SquadCollectionKey = GraphOpsNodeGalleryHost.SquadCollectionKey;
    public const float ResidualHealthMax = 40f;

    // Head-top anchor: 0.85m unit radius plus clearance so pips clear both the roster circle and the health bar.
    private const float HeadTopYOffset = 1.2f;

    private Entity[] _units = Array.Empty<Entity>();
    private byte[] _unitInRange = Array.Empty<byte>();
    private bool _seeded;

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
        GraphOpsNodeActorBinding.LightCasterAndHits(ctx);
        ResolveExtremes(ctx, result);
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

        if (string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.QuerySortByAttribute), StringComparison.Ordinal))
        {
            DrawSortRankOverlay(ctx, debugDraw, caster);
            return;
        }

        float casterX = ctx.Vignette.Actors[caster].X;
        float casterY = ctx.Vignette.Actors[caster].Y;
        for (int i = 0; i < _units.Length; i++)
        {
            if (_unitInRange[i] == 0)
            {
                continue;
            }

            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[i]);
            GraphShowcaseStagePresenter.DrawActor(
                debugDraw,
                ctx.Vignette.Actors[actorIndex].X,
                ctx.Vignette.Actors[actorIndex].Y,
                radius: 0.85f,
                GraphShowcaseStagePresenter.SentryAlert,
                thickness: 0.16f);
        }

        if (StrongestIndex >= 0)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[StrongestIndex]);
            GraphShowcaseStagePresenter.DrawAggroLine(
                debugDraw,
                casterX,
                casterY,
                ctx.Vignette.Actors[actorIndex].X,
                ctx.Vignette.Actors[actorIndex].Y);
        }

        if (WeakestIndex >= 0 && WeakestIndex != StrongestIndex)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[WeakestIndex]);
            GraphShowcaseStagePresenter.DrawAggroLine(
                debugDraw,
                casterX,
                casterY,
                ctx.Vignette.Actors[actorIndex].X,
                ctx.Vignette.Actors[actorIndex].Y);
        }
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
            return;
        }

        ScanInRangeExtremes(ctx);
        if (string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.AggMinAttribute), StringComparison.Ordinal))
        {
            StrongestIndex = -1;
        }
        else if (string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.AggMaxAttribute), StringComparison.Ordinal))
        {
            WeakestIndex = -1;
        }
        else if (!IsAggregateValueOp(ctx.Vignette.Op))
        {
            StrongestIndex = -1;
            WeakestIndex = -1;
        }
    }

    private void ScanInRangeExtremes(GraphOpsNodeDriverContext ctx)
    {
        float maxHp = float.MinValue;
        float minHp = float.MaxValue;
        for (int i = 0; i < _units.Length; i++)
        {
            if (_unitInRange[i] == 0)
            {
                continue;
            }

            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[i]);
            float hp = ctx.ActorHealth[actorIndex];
            if (hp > maxHp)
            {
                maxHp = hp;
                StrongestIndex = i;
            }

            if (hp < minHp)
            {
                minHp = hp;
                WeakestIndex = i;
            }
        }
    }

    private void FillCaptions(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        ctx.CaptionValues["count"] = LastTargetCount.ToString();
        ctx.CaptionValues["threshold"] = ResidualHealthMax.ToString("0");
        ctx.CaptionValues["sum"] = result.FloatValue.ToString("0");
        ctx.CaptionValues["avg"] = result.FloatValue.ToString("0");
        ctx.CaptionValues["max"] = result.FloatValue.ToString("0");
        ctx.CaptionValues["min"] = result.FloatValue.ToString("0");

        int named = StrongestIndex >= 0 ? StrongestIndex : WeakestIndex;
        if (named < 0 && LastTargetCount > 0)
        {
            named = IndexOfUnit(ctx.HitTargets[0]);
        }

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
