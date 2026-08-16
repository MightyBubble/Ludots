using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class RelNodeDriver : IGraphOpsNodeDriver
{
    // Head-top anchors clear the HUD name + bar band so badges/markers read on their own.
    private const float HeadTopYOffset = 1.2f;
    private const float HeadMarkerYOffset = 1.7f;
    private const float HeadClipboardYOffset = 1.9f;
    private const float BenchX = 7.0f;
    private const float BenchPanelY = 0.9f;
    private const float BenchResultLiftY = 3.5f;
    private const float BenchResultDropY = -2.3f;

    private bool _seeded;
    private int _socialBondTypeId;
    private int _loyaltyMetricId;
    private int _estrangedFlagId;
    private int _trustedFlagId;
    private int _aggResult;
    private Entity _extremeEntity = Entity.Null;
    private readonly List<Entity> _severed = new();
    private Entity[] _friends = Array.Empty<Entity>();
    private byte[] _linked = Array.Empty<byte>();
    private bool[] _lit = Array.Empty<bool>();

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        if (ctx.Relationships == null || ctx.RelationshipTypes == null || ctx.RelationshipMetrics == null || ctx.RelationshipFlags == null)
        {
            throw new InvalidOperationException($"Rel gallery '{ctx.Vignette.Op}' requires host relationship services.");
        }

        _socialBondTypeId = ctx.RelationshipTypes.Register("SocialBond");
        _loyaltyMetricId = ctx.RelationshipMetrics.Register("Loyalty", -100, 100, 0);
        _estrangedFlagId = ctx.RelationshipFlags.Register("Estranged");
        _trustedFlagId = ctx.RelationshipFlags.Register("Trusted");
        CollectFriends(ctx);
        _linked = new byte[_friends.Length];
        _lit = new bool[_friends.Length];
        _aggResult = 0;
        _extremeEntity = Entity.Null;
        _severed.Clear();
        ReseedLinks(ctx);
        RefreshLinkedFlags(ctx);
        ctx.Target = _friends[0];
        ctx.Metrics.AgentCount = ctx.SimActors.Length;
        ctx.Metrics.Detail = ctx.Vignette.Beat;
        _seeded = true;
        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded || ctx.Relationships == null)
        {
            throw new InvalidOperationException($"RelNodeDriver.Seed required before Tick for {ctx.Vignette.Op}.");
        }

        if (!GraphNodeOpParser.TryParse(ctx.Vignette.Op, out GraphNodeOp op))
        {
            throw new InvalidOperationException($"Unknown rel op '{ctx.Vignette.Op}'.");
        }

        ReseedLinks(ctx);
        RefreshLinkedFlags(ctx);
        int linksBefore = CountLinkedFriends(ctx);
        ctx.Target = op is GraphNodeOp.RelationshipRemoveLink or GraphNodeOp.RelationshipSetFlag
            ? RequireWeakestLinkedFriend(ctx)
            : _friends[0];
        GraphOpsNodeExecuteResult result = ctx.ExecuteFeaturedGraph();
        RefreshLinkedFlags(ctx);
        int linksAfter = CountLinkedFriends(ctx);
        if (op == GraphNodeOp.RelationshipRemoveLink &&
            !ctx.Relationships!.HasLink(ctx.Caster, ctx.Target, _socialBondTypeId) &&
            !_severed.Contains(ctx.Target))
        {
            _severed.Add(ctx.Target);
        }

        FillCaption(ctx, op, result, linksBefore, linksAfter);
        ApplyBars(ctx, op, result);
        if (IsAggValueOp(op))
        {
            _aggResult = result.IntValue;
        }

        if (IsAggEntOp(op))
        {
            _extremeEntity = result.EntityValue;
        }

        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        if (caster < 0)
        {
            return;
        }

        if (GraphNodeOpParser.TryParse(ctx.Vignette.Op, out GraphNodeOp op))
        {
            switch (op)
            {
                case GraphNodeOp.RelationshipQueryIncoming:
                    DrawIncomingOverlay(ctx, debugDraw, caster);
                    return;
                case GraphNodeOp.RelationshipSetFlag:
                    DrawSetFlagOverlay(ctx, debugDraw, caster);
                    return;
                case GraphNodeOp.RelationshipQueryMutual:
                    DrawMutualOverlay(ctx, debugDraw, caster);
                    return;
                case GraphNodeOp.RelationshipFilterFlag:
                    DrawFilterFlagOverlay(ctx, debugDraw, caster);
                    return;
                case GraphNodeOp.RelationshipQueryOutgoing:
                    DrawOutgoingOverlay(ctx, debugDraw, caster);
                    return;
                case GraphNodeOp.RelationshipFilterMetricRange:
                    DrawMetricRangeOverlay(ctx, debugDraw, caster);
                    return;
                case GraphNodeOp.RelationshipAggSumMetric:
                case GraphNodeOp.RelationshipAggAverageMetric:
                case GraphNodeOp.RelationshipAggMinMetric:
                case GraphNodeOp.RelationshipAggMaxMetric:
                    DrawAggBenchOverlay(ctx, debugDraw, caster, op);
                    return;
                case GraphNodeOp.RelationshipSortByMetric:
                    DrawSortOverlay(ctx, debugDraw, caster);
                    return;
                case GraphNodeOp.RelationshipGetMetric:
                    DrawGetMetricOverlay(ctx, debugDraw, caster);
                    return;
                case GraphNodeOp.RelationshipHasLink:
                    DrawHasLinkOverlay(ctx, debugDraw, caster);
                    return;
                case GraphNodeOp.RelationshipRemoveLink:
                    DrawRemoveLinkOverlay(ctx, debugDraw, caster);
                    return;
                case GraphNodeOp.RelationshipAggMinEntityByMetric:
                    DrawMinEntOverlay(ctx, debugDraw, caster);
                    return;
                case GraphNodeOp.RelationshipAggMaxEntityByMetric:
                    DrawMaxEntOverlay(ctx, debugDraw, caster);
                    return;
                case GraphNodeOp.RelationshipQueryBetweenPair:
                    DrawBetweenPairOverlay(ctx, debugDraw, caster);
                    return;
            }
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _friends.Length; i++)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _friends[i]);
            if (!ctx.ActorHudLit[actorIndex])
            {
                continue;
            }

            GraphShowcaseStagePresenter.DrawAggroLine(
                debugDraw,
                actors[caster].X,
                actors[caster].Y,
                actors[actorIndex].X,
                actors[actorIndex].Y);
        }
    }

    private void DrawIncomingOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        GraphOpsNodeLink[] links = ctx.Vignette.Links;
        for (int i = 0; i < links.Length; i++)
        {
            int from = GraphOpsNodeActorBinding.IndexOfId(ctx.Vignette, links[i].From);
            int to = GraphOpsNodeActorBinding.IndexOfId(ctx.Vignette, links[i].To);
            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw,
                actors[from].X,
                actors[from].Y,
                actors[to].X,
                actors[to].Y,
                0.08f,
                GraphShowcaseStagePresenter.GhostColor);
        }

        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            int hit = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[i]);
            if (hit < 0 || hit == caster)
            {
                continue;
            }

            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                actors[hit].X,
                actors[hit].Y,
                actors[caster].X,
                actors[caster].Y,
                0.14f,
                GraphShowcaseStagePresenter.SentryAlert);
        }
    }

    private void DrawSetFlagOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        if (ctx.Target == Entity.Null ||
            !ctx.Relationships!.HasFlag(ctx.Caster, ctx.Target, _socialBondTypeId, _estrangedFlagId))
        {
            return;
        }

        int target = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.Target);
        if (target < 0)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        float ax = actors[caster].X;
        float ay = actors[caster].Y;
        float bx = actors[target].X;
        float by = actors[target].Y;
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            ax,
            ay,
            bx,
            by,
            0.12f,
            GraphShowcaseStagePresenter.SentryIdle);
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw,
            (ax + bx) * 0.5f,
            (ay + by) * 0.5f,
            GraphShowcaseStagePresenter.BadgeKind.Flag,
            GraphShowcaseStagePresenter.EnemyColor);
    }

    private void DrawChainField(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        GraphOpsNodeLink[] links = ctx.Vignette.Links;
        for (int i = 0; i < links.Length; i++)
        {
            int from = GraphOpsNodeActorBinding.IndexOfId(ctx.Vignette, links[i].From);
            int to = GraphOpsNodeActorBinding.IndexOfId(ctx.Vignette, links[i].To);
            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw,
                actors[from].X,
                actors[from].Y,
                actors[to].X,
                actors[to].Y,
                0.08f,
                GraphShowcaseStagePresenter.GhostColor);
        }
    }

    private void DrawMutualOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        DrawChainField(ctx, debugDraw);
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _friends.Length; i++)
        {
            if (_linked[i] == 0 || !IsMutual(ctx, _friends[i]))
            {
                continue;
            }

            int fi = GraphOpsNodeActorBinding.IndexOf(ctx, _friends[i]);
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                actors[caster].X,
                actors[caster].Y,
                actors[fi].X,
                actors[fi].Y,
                0.14f,
                GraphShowcaseStagePresenter.SentryAlert,
                arrowStart: true,
                arrowEnd: true);
        }
    }

    private bool IsMutual(GraphOpsNodeDriverContext ctx, Entity friend)
    {
        return ctx.Relationships!.HasLink(ctx.Caster, friend, _socialBondTypeId)
            && ctx.Relationships.HasLink(friend, ctx.Caster, _socialBondTypeId);
    }

    private void DrawFilterFlagOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        DrawChainField(ctx, debugDraw);
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _friends.Length; i++)
        {
            if (_linked[i] == 0 ||
                !ctx.Relationships!.HasFlag(ctx.Caster, _friends[i], _socialBondTypeId, _trustedFlagId))
            {
                continue;
            }

            int fi = GraphOpsNodeActorBinding.IndexOf(ctx, _friends[i]);
            float ax = actors[caster].X;
            float ay = actors[caster].Y;
            float bx = actors[fi].X;
            float by = actors[fi].Y;
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                ax,
                ay,
                bx,
                by,
                0.12f,
                GraphShowcaseStagePresenter.GuardColor);
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw,
                (ax + bx) * 0.5f,
                (ay + by) * 0.5f,
                GraphShowcaseStagePresenter.BadgeKind.Flag,
                GraphShowcaseStagePresenter.SentryAlert);
        }
    }

    private void DrawOutgoingOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        DrawChainField(ctx, debugDraw);
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _friends.Length; i++)
        {
            if (_linked[i] == 0)
            {
                continue;
            }

            int fi = GraphOpsNodeActorBinding.IndexOf(ctx, _friends[i]);
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                actors[caster].X,
                actors[caster].Y,
                actors[fi].X,
                actors[fi].Y,
                0.14f,
                GraphShowcaseStagePresenter.SentryAlert);
        }
    }

    private void DrawMetricRangeOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        DrawChainField(ctx, debugDraw);
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int m = 0; m < ctx.HitTargetCount; m++)
        {
            int fi = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[m]);
            if (fi < 0 || fi == caster)
            {
                continue;
            }

            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                actors[caster].X,
                actors[caster].Y,
                actors[fi].X,
                actors[fi].Y,
                0.12f,
                GraphShowcaseStagePresenter.SentryAlert);
        }

        const float left = -4.8f;
        const float right = 4.8f;
        const float y80 = 2.5f;
        const float y30 = 1.9f;
        RawLine(debugDraw, left, 1.3f, left, 2.9f, 0.12f, GraphShowcaseStagePresenter.GateColor);
        RawLine(debugDraw, right, 1.3f, right, 2.9f, 0.12f, GraphShowcaseStagePresenter.GateColor);
        RawLine(debugDraw, left, y80, right, y80, 0.09f, GraphShowcaseStagePresenter.SentryAlert);
        RawLine(debugDraw, left, y30, right, y30, 0.09f, GraphShowcaseStagePresenter.SentryAlert);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, right + 0.9f, y80, 80, 0.4f, GraphShowcaseStagePresenter.SentryAlert);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, right + 0.9f, y30, 30, 0.4f, GraphShowcaseStagePresenter.SentryAlert);

        for (int i = 0; i < _friends.Length; i++)
        {
            if (_linked[i] == 0)
            {
                continue;
            }

            int fi = GraphOpsNodeActorBinding.IndexOf(ctx, _friends[i]);
            int loyalty = FriendLoyalty(ctx, _friends[i]);
            float hx = actors[fi].X;
            float hy = actors[fi].Y + HeadClipboardYOffset;
            GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, hx, hy, 0.8f, 0.5f, 1, GraphShowcaseStagePresenter.GateColor);
            GraphShowcaseStagePresenter.DrawNumber(debugDraw, hx + 0.25f, hy, loyalty, 0.42f, GraphShowcaseStagePresenter.SentryAlert);
        }
    }

    private void DrawSortOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        int ranked = ctx.HitTargetCount;
        for (int i = 0; i < ranked; i++)
        {
            int fi = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[i]);
            if (fi < 0)
            {
                continue;
            }

            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                actors[caster].X,
                actors[caster].Y,
                actors[fi].X,
                actors[fi].Y,
                0.16f - i * 0.03f,
                GraphShowcaseStagePresenter.SentryAlert);
            DrawRankBadge(debugDraw, actors[fi].X, actors[fi].Y + HeadTopYOffset, i + 1);
        }
    }

    private static void DrawRankBadge(DebugDrawCommandBuffer debugDraw, float x, float y, int rank)
    {
        debugDraw.Boxes.Add(new DebugDrawBox2D
        {
            Center = new Vector2(x, y),
            HalfWidth = 0.34f,
            HalfHeight = 0.44f,
            Thickness = 0.06f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, x + 0.22f, y, rank, 0.45f, GraphShowcaseStagePresenter.OutlineDark);
    }

    private void DrawGetMetricOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        DrawChainField(ctx, debugDraw);
        if (ctx.Target == Entity.Null)
        {
            return;
        }

        int fi = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.Target);
        if (fi < 0)
        {
            return;
        }

        int loyalty = FriendLoyalty(ctx, ctx.Target);
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        float ax = actors[caster].X;
        float ay = actors[caster].Y;
        float bx = actors[fi].X;
        float by = actors[fi].Y;
        float mx = (ax + bx) * 0.5f;
        float my = (ay + by) * 0.5f + 0.6f;
        GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, mx, my, 0.9f, 0.6f, 1, GraphShowcaseStagePresenter.GateColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, mx + 0.28f, my, loyalty, 0.5f, GraphShowcaseStagePresenter.SentryAlert);
        GraphShowcaseStagePresenter.DrawDirectedLine(debugDraw, mx, my, ax, ay, 0.09f, GraphShowcaseStagePresenter.SentryAlert);

        float cx = ax - 2.2f;
        float cy = ay - 1.6f;
        GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, cx, cy, 1.1f, 0.7f, 1, GraphShowcaseStagePresenter.GateColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, cx + 0.35f, cy, loyalty, 0.55f, GraphShowcaseStagePresenter.SentryAlert);
    }

    private void DrawHasLinkOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        if (ctx.Target == Entity.Null ||
            !ctx.Relationships!.HasLink(ctx.Caster, ctx.Target, _socialBondTypeId))
        {
            return;
        }

        int fi = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.Target);
        if (fi < 0)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        float ax = actors[caster].X;
        float ay = actors[caster].Y;
        float bx = actors[fi].X;
        float by = actors[fi].Y;
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            ax,
            ay,
            bx,
            by,
            0.14f,
            GraphShowcaseStagePresenter.GuardColor);
        float mx = (ax + bx) * 0.5f;
        float my = (ay + by) * 0.5f;
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw,
            mx,
            my,
            GraphShowcaseStagePresenter.BadgeKind.Ring,
            GraphShowcaseStagePresenter.GuardColor,
            scale: 1.1f);
        float dx = bx - ax;
        float dy = by - ay;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len > 1e-4f)
        {
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw,
                mx + dx / len * 0.22f,
                my + dy / len * 0.22f,
                GraphShowcaseStagePresenter.BadgeKind.Ring,
                GraphShowcaseStagePresenter.GuardColor,
                scale: 0.9f);
        }
    }

    private void DrawRemoveLinkOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _friends.Length; i++)
        {
            if (_linked[i] == 0)
            {
                continue;
            }

            int fi = GraphOpsNodeActorBinding.IndexOf(ctx, _friends[i]);
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                actors[caster].X,
                actors[caster].Y,
                actors[fi].X,
                actors[fi].Y,
                0.08f,
                GraphShowcaseStagePresenter.GhostColor);
        }

        for (int s = 0; s < _severed.Count; s++)
        {
            int fi = GraphOpsNodeActorBinding.IndexOf(ctx, _severed[s]);
            if (fi < 0)
            {
                continue;
            }

            float ax = actors[caster].X;
            float ay = actors[caster].Y;
            float bx = actors[fi].X;
            float by = actors[fi].Y;
            float dx = bx - ax;
            float dy = by - ay;
            GraphShowcaseStagePresenter.DrawGhostSegment(debugDraw, ax, ay, ax + dx * 0.42f, ay + dy * 0.42f, GraphShowcaseStagePresenter.GhostColor);
            GraphShowcaseStagePresenter.DrawGhostSegment(debugDraw, ax + dx * 0.58f, ay + dy * 0.58f, bx, by, GraphShowcaseStagePresenter.GhostColor);
        }
    }

    private void DrawAggBenchOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, GraphNodeOp op)
    {
        if (_aggResult <= 0)
        {
            return;
        }

        bool extreme = IsMinMaxValue(op);
        int winnerIndex = extreme ? IndexOfFriend(FriendWithLoyalty(ctx, _aggResult)) : -1;
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _friends.Length; i++)
        {
            if (_linked[i] == 0)
            {
                continue;
            }

            int gi = GraphOpsNodeActorBinding.IndexOf(ctx, _friends[i]);
            if (extreme && i != winnerIndex)
            {
                GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                    debugDraw,
                    actors[caster].X,
                    actors[caster].Y,
                    actors[gi].X,
                    actors[gi].Y,
                    0.08f,
                    GraphShowcaseStagePresenter.GhostColor);
                continue;
            }

            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                actors[caster].X,
                actors[caster].Y,
                actors[gi].X,
                actors[gi].Y,
                0.12f,
                GraphShowcaseStagePresenter.SentryAlert);
        }

        if (extreme && winnerIndex >= 0)
        {
            int gi = GraphOpsNodeActorBinding.IndexOf(ctx, _friends[winnerIndex]);
            DrawExtremeMarker(
                debugDraw,
                actors[gi].X,
                actors[gi].Y + HeadMarkerYOffset,
                op == GraphNodeOp.RelationshipAggMinMetric,
                FriendLoyalty(ctx, _friends[winnerIndex]));
        }

        float resultY = extreme ? BenchResultLiftY : BenchResultDropY;
        if (!extreme)
        {
            for (int i = 0; i < _friends.Length; i++)
            {
                int gi = GraphOpsNodeActorBinding.IndexOf(ctx, _friends[i]);
                float midX = (actors[caster].X + actors[gi].X) * 0.5f;
                float midY = (actors[caster].Y + actors[gi].Y) * 0.5f;
                GraphShowcaseStagePresenter.DrawGhostSegment(debugDraw, midX, midY, SlotX(i), BenchPanelY, GraphShowcaseStagePresenter.GhostColor);
            }

            GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, BenchX, BenchPanelY, 2.6f, 1.9f, 4, GraphShowcaseStagePresenter.GateColor);
            for (int i = 0; i < _friends.Length; i++)
            {
                GraphShowcaseStagePresenter.DrawNumber(
                    debugDraw,
                    SlotX(i) + 0.25f,
                    BenchPanelY,
                    FriendLoyalty(ctx, _friends[i]),
                    0.44f,
                    GraphShowcaseStagePresenter.SentryAlert);
            }

            if (op == GraphNodeOp.RelationshipAggSumMetric)
            {
                DrawPlusGlyph(debugDraw, BenchX, -0.5f);
            }
            else
            {
                DrawDivideGlyph(debugDraw, BenchX, -0.5f);
            }
        }
        else
        {
            GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, BenchX, BenchPanelY, 2.6f, 1.9f, 4, GraphShowcaseStagePresenter.GateColor);
            for (int i = 0; i < _friends.Length; i++)
            {
                if (i == winnerIndex)
                {
                    continue;
                }

                GraphShowcaseStagePresenter.DrawNumber(
                    debugDraw,
                    SlotX(i) + 0.25f,
                    BenchPanelY,
                    FriendLoyalty(ctx, _friends[i]),
                    0.44f,
                    GraphShowcaseStagePresenter.GhostColor);
            }
        }

        GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, BenchX, resultY, 1.6f, 0.9f, 1, GraphShowcaseStagePresenter.GateColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, BenchX + 0.5f, resultY, _aggResult, 0.6f, GraphShowcaseStagePresenter.SentryAlert);
        if (extreme && winnerIndex >= 0)
        {
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                SlotX(winnerIndex),
                BenchPanelY,
                BenchX,
                resultY - 0.55f,
                0.1f,
                GraphShowcaseStagePresenter.SentryAlert);
        }
        else
        {
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                BenchX,
                -0.5f,
                BenchX,
                resultY + 0.55f,
                0.1f,
                GraphShowcaseStagePresenter.SentryAlert);
        }
    }

    private void DrawMinEntOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        DrawExtremeEntityOverlay(ctx, debugDraw, caster, isMin: true);
    }

    private void DrawMaxEntOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        DrawExtremeEntityOverlay(ctx, debugDraw, caster, isMin: false);
    }

    private void DrawExtremeEntityOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, bool isMin)
    {
        if (_extremeEntity == Entity.Null)
        {
            return;
        }

        int fi = GraphOpsNodeActorBinding.IndexOf(ctx, _extremeEntity);
        if (fi < 0)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _friends.Length; i++)
        {
            if (_linked[i] == 0)
            {
                continue;
            }

            int gi = GraphOpsNodeActorBinding.IndexOf(ctx, _friends[i]);
            if (gi == fi)
            {
                continue;
            }

            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw,
                actors[caster].X,
                actors[caster].Y,
                actors[gi].X,
                actors[gi].Y,
                0.08f,
                GraphShowcaseStagePresenter.GhostColor);
        }

        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            actors[caster].X,
            actors[caster].Y,
            actors[fi].X,
            actors[fi].Y,
            0.14f,
            GraphShowcaseStagePresenter.SentryAlert);
        DrawExtremeMarker(
            debugDraw,
            actors[fi].X,
            actors[fi].Y + HeadMarkerYOffset,
            isMin,
            FriendLoyalty(ctx, _extremeEntity));
    }

    private void DrawBetweenPairOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        DrawChainField(ctx, debugDraw);
        if (ctx.HitTargetCount <= 0)
        {
            return;
        }

        int fi = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[0]);
        if (fi < 0 || fi == caster)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        float ax = actors[caster].X;
        float ay = actors[caster].Y;
        float bx = actors[fi].X;
        float by = actors[fi].Y;
        DrawUpTriangle(debugDraw, bx, by + HeadMarkerYOffset);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            ax,
            ay,
            bx,
            by,
            0.16f,
            GraphShowcaseStagePresenter.SentryAlert,
            arrowStart: true,
            arrowEnd: true);
        float mx = (ax + bx) * 0.5f;
        float my = (ay + by) * 0.5f;
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw,
            mx,
            my,
            GraphShowcaseStagePresenter.BadgeKind.Ring,
            GraphShowcaseStagePresenter.SentryAlert,
            scale: 1.2f);
        float fx = mx + 0.7f;
        float fy = my - 1.0f;
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw,
            fx,
            fy,
            GraphShowcaseStagePresenter.BadgeKind.Cross,
            GraphShowcaseStagePresenter.SentryAlert);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, fx + 0.4f, fy, ctx.HitTargetCount, 0.4f, GraphShowcaseStagePresenter.SentryAlert);
    }

    private int FriendLoyalty(GraphOpsNodeDriverContext ctx, Entity friend)
    {
        return ctx.Relationships!.GetMetric(ctx.Caster, friend, _socialBondTypeId, _loyaltyMetricId);
    }

    private static float SlotX(int index)
    {
        return BenchX + (index - 1.5f) * 0.62f;
    }

    private int IndexOfFriend(Entity friend)
    {
        for (int i = 0; i < _friends.Length; i++)
        {
            if (_friends[i] == friend)
            {
                return i;
            }
        }

        return -1;
    }

    private static void DrawPlusGlyph(DebugDrawCommandBuffer debugDraw, float x, float y)
    {
        RawLine(debugDraw, x - 0.3f, y, x + 0.3f, y, 0.08f, GraphShowcaseStagePresenter.GateColor);
        RawLine(debugDraw, x, y - 0.3f, x, y + 0.3f, 0.08f, GraphShowcaseStagePresenter.GateColor);
    }

    private static void DrawDivideGlyph(DebugDrawCommandBuffer debugDraw, float x, float y)
    {
        RawLine(debugDraw, x - 0.35f, y, x + 0.35f, y, 0.08f, GraphShowcaseStagePresenter.GateColor);
        debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = new Vector2(x, y + 0.28f),
            Radius = 0.07f,
            Thickness = 0.06f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, x + 0.62f, y, 4, 0.4f, GraphShowcaseStagePresenter.GateColor);
    }

    private static void DrawExtremeMarker(DebugDrawCommandBuffer debugDraw, float x, float y, bool isMin, int value)
    {
        if (isMin)
        {
            DrawDownTriangle(debugDraw, x, y);
        }
        else
        {
            DrawUpTriangle(debugDraw, x, y);
        }

        GraphShowcaseStagePresenter.DrawNumber(debugDraw, x + 0.5f, y, value, 0.45f, GraphShowcaseStagePresenter.GateColor);
    }

    private static void DrawUpTriangle(DebugDrawCommandBuffer debugDraw, float x, float y)
    {
        RawLine(debugDraw, x, y + 0.35f, x - 0.3f, y - 0.25f, 0.08f, GraphShowcaseStagePresenter.SentryAlert);
        RawLine(debugDraw, x, y + 0.35f, x + 0.3f, y - 0.25f, 0.08f, GraphShowcaseStagePresenter.SentryAlert);
        RawLine(debugDraw, x - 0.3f, y - 0.25f, x + 0.3f, y - 0.25f, 0.08f, GraphShowcaseStagePresenter.SentryAlert);
    }

    private static void DrawDownTriangle(DebugDrawCommandBuffer debugDraw, float x, float y)
    {
        RawLine(debugDraw, x, y - 0.35f, x - 0.3f, y + 0.25f, 0.08f, GraphShowcaseStagePresenter.SentryAlert);
        RawLine(debugDraw, x, y - 0.35f, x + 0.3f, y + 0.25f, 0.08f, GraphShowcaseStagePresenter.SentryAlert);
        RawLine(debugDraw, x - 0.3f, y + 0.25f, x + 0.3f, y + 0.25f, 0.08f, GraphShowcaseStagePresenter.SentryAlert);
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

    private static bool IsAggValueOp(GraphNodeOp op)
    {
        return op is GraphNodeOp.RelationshipAggSumMetric
            or GraphNodeOp.RelationshipAggAverageMetric
            or GraphNodeOp.RelationshipAggMinMetric
            or GraphNodeOp.RelationshipAggMaxMetric;
    }

    private static bool IsAggEntOp(GraphNodeOp op)
    {
        return op is GraphNodeOp.RelationshipAggMinEntityByMetric
            or GraphNodeOp.RelationshipAggMaxEntityByMetric;
    }

    private static bool IsMinMaxValue(GraphNodeOp op)
    {
        return op is GraphNodeOp.RelationshipAggMinMetric or GraphNodeOp.RelationshipAggMaxMetric;
    }

    private void CollectFriends(GraphOpsNodeDriverContext ctx)
    {
        var friends = new List<Entity>();
        for (int i = 0; i < ctx.Vignette.Actors.Length; i++)
        {
            if (string.Equals(ctx.Vignette.Actors[i].Role, "caster", StringComparison.Ordinal))
            {
                continue;
            }

            friends.Add(ctx.SimActors[i]);
        }

        if (friends.Count < 4)
        {
            throw new InvalidOperationException(
                $"Rel vignette {ctx.Vignette.Op} requires a caster plus at least 4 friends.");
        }

        _friends = friends.ToArray();
    }

    private void ReseedLinks(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeLink[] links = ctx.Vignette.Links;
        if (links.Length == 0)
        {
            throw new InvalidOperationException($"Rel vignette {ctx.Vignette.Op} has no links to seed.");
        }

        for (int i = 0; i < links.Length; i++)
        {
            GraphOpsNodeLink link = links[i];
            Entity from = RequireActor(ctx, link.From);
            Entity to = RequireActor(ctx, link.To);
            int typeId = ctx.RelationshipTypes!.Register(link.Type);
            ctx.Relationships!.EnsureLink(from, to, typeId);
            if (!string.IsNullOrWhiteSpace(link.Metric))
            {
                int metricId = ctx.RelationshipMetrics!.Register(link.Metric, -100, 100, 0);
                ctx.Relationships.SetMetric(from, to, typeId, metricId, link.MetricValue, reasonId: 0);
            }

            if (link.Flags == null)
            {
                continue;
            }

            for (int f = 0; f < link.Flags.Length; f++)
            {
                int flagId = ctx.RelationshipFlags!.Register(link.Flags[f]);
                ctx.Relationships.SetFlag(from, to, typeId, flagId, true);
            }
        }
    }

    private static Entity RequireActor(GraphOpsNodeDriverContext ctx, string id)
    {
        for (int i = 0; i < ctx.Vignette.Actors.Length; i++)
        {
            if (string.Equals(ctx.Vignette.Actors[i].Id, id, StringComparison.Ordinal))
            {
                return ctx.SimActors[i];
            }
        }

        throw new InvalidOperationException($"Rel vignette {ctx.Vignette.Op} unknown actor '{id}'.");
    }

    private void FillCaption(
        GraphOpsNodeDriverContext ctx,
        GraphNodeOp op,
        GraphOpsNodeExecuteResult result,
        int linksBefore,
        int linksAfter)
    {
        Dictionary<string, string> values = ctx.CaptionValues;
        values["linksBefore"] = linksBefore.ToString();
        values["linksAfter"] = linksAfter.ToString();
        values["friendCount"] = result.TargetCount.ToString();
        values["sum"] = result.IntValue.ToString();
        values["max"] = result.IntValue.ToString();
        values["avg"] = result.IntValue.ToString();
        values["min"] = result.IntValue.ToString();
        values["loyalty"] = result.IntValue.ToString();
        values["friend"] = "-";
        values["status"] = result.BoolValue ? "连着" : "没连";

        switch (op)
        {
            case GraphNodeOp.RelationshipRemoveLink:
                if (linksAfter >= linksBefore)
                {
                    throw new InvalidOperationException(
                        $"RelationshipRemoveLink did not decrease links ({linksBefore} -> {linksAfter}).");
                }

                values["friend"] = EntityLabel(ctx, ctx.Target);
                break;
            case GraphNodeOp.RelationshipGetMetric:
                if (result.IntValue <= 0)
                {
                    throw new InvalidOperationException("RelationshipGetMetric returned 0; seed must have a live 好感.");
                }

                values["loyalty"] = result.IntValue.ToString();
                values["friend"] = EntityLabel(ctx, ctx.Target);
                break;
            case GraphNodeOp.RelationshipSetFlag:
                if (!ctx.Relationships!.HasFlag(ctx.Caster, ctx.Target, _socialBondTypeId, _estrangedFlagId))
                {
                    throw new InvalidOperationException("RelationshipSetFlag did not plant the estranged flag.");
                }

                values["friend"] = EntityLabel(ctx, ctx.Target);
                break;
            case GraphNodeOp.RelationshipQueryOutgoing:
            case GraphNodeOp.RelationshipQueryIncoming:
            case GraphNodeOp.RelationshipQueryMutual:
            case GraphNodeOp.RelationshipQueryBetweenPair:
            case GraphNodeOp.RelationshipFilterMetricRange:
            case GraphNodeOp.RelationshipFilterFlag:
                RequireMatches(ctx.Vignette.Op, result.TargetCount);
                values["friendCount"] = result.TargetCount.ToString();
                values["friend"] = EntityLabel(ctx, ctx.HitTargets[0]);
                break;
            case GraphNodeOp.RelationshipSortByMetric:
                RequireMatches(ctx.Vignette.Op, result.TargetCount);
                values["friend"] = EntityLabel(ctx, ctx.HitTargets[0]);
                values["loyalty"] = ctx.Relationships!.GetMetric(
                    ctx.Caster,
                    ctx.HitTargets[0],
                    _socialBondTypeId,
                    _loyaltyMetricId).ToString();
                break;
            case GraphNodeOp.RelationshipAggSumMetric:
            case GraphNodeOp.RelationshipAggAverageMetric:
                if (result.IntValue <= 0)
                {
                    throw new InvalidOperationException($"{ctx.Vignette.Op} aggregated to 0; friend seed must be live.");
                }

                values["sum"] = result.IntValue.ToString();
                values["avg"] = result.IntValue.ToString();
                break;
            case GraphNodeOp.RelationshipAggMaxMetric:
            case GraphNodeOp.RelationshipAggMinMetric:
                if (result.IntValue <= 0)
                {
                    throw new InvalidOperationException($"{ctx.Vignette.Op} aggregated to 0; friend seed must be live.");
                }

                values["max"] = result.IntValue.ToString();
                values["min"] = result.IntValue.ToString();
                values["friend"] = EntityLabel(ctx, FriendWithLoyalty(ctx, result.IntValue));
                break;
            case GraphNodeOp.RelationshipAggMaxEntityByMetric:
            case GraphNodeOp.RelationshipAggMinEntityByMetric:
                if (result.EntityValue == Entity.Null)
                {
                    throw new InvalidOperationException($"{ctx.Vignette.Op} returned no person.");
                }

                values["friend"] = EntityLabel(ctx, result.EntityValue);
                break;
            case GraphNodeOp.RelationshipHasLink:
                if (!result.BoolValue)
                {
                    throw new InvalidOperationException("RelationshipHasLink was false after seeding a live chain.");
                }

                values["friend"] = EntityLabel(ctx, ctx.Target);
                break;
            default:
                throw new InvalidOperationException($"RelNodeDriver cannot caption {ctx.Vignette.Op}.");
        }

        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, values);
    }

    private void ApplyBars(GraphOpsNodeDriverContext ctx, GraphNodeOp op, GraphOpsNodeExecuteResult result)
    {
        Array.Clear(_lit);
        switch (op)
        {
            case GraphNodeOp.RelationshipQueryOutgoing:
            case GraphNodeOp.RelationshipQueryIncoming:
            case GraphNodeOp.RelationshipQueryMutual:
            case GraphNodeOp.RelationshipQueryBetweenPair:
            case GraphNodeOp.RelationshipFilterMetricRange:
            case GraphNodeOp.RelationshipFilterFlag:
            case GraphNodeOp.RelationshipSortByMetric:
                for (int m = 0; m < ctx.HitTargetCount; m++)
                {
                    MarkFriendLit(ctx.HitTargets[m]);
                }

                break;
            case GraphNodeOp.RelationshipAggMaxEntityByMetric:
            case GraphNodeOp.RelationshipAggMinEntityByMetric:
                MarkFriendLit(result.EntityValue);
                break;
            case GraphNodeOp.RelationshipGetMetric:
            case GraphNodeOp.RelationshipHasLink:
            case GraphNodeOp.RelationshipSetFlag:
                MarkFriendLit(ctx.Target);
                break;
            default:
                for (int i = 0; i < _friends.Length; i++)
                {
                    _lit[i] = _linked[i] != 0;
                }

                break;
        }

        GraphOpsNodeActorBinding.EnsureHudLitBuffer(ctx);
        Array.Fill(ctx.ActorHudLit, false);
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        if (caster >= 0)
        {
            ctx.ActorHudLit[caster] = true;
        }

        for (int i = 0; i < _friends.Length; i++)
        {
            ctx.ActorHudLit[GraphOpsNodeActorBinding.IndexOf(ctx, _friends[i])] = _lit[i];
        }
    }

    private void MarkFriendLit(Entity entity)
    {
        for (int i = 0; i < _friends.Length; i++)
        {
            if (_friends[i] == entity)
            {
                _lit[i] = true;
                return;
            }
        }
    }

    private Entity RequireWeakestLinkedFriend(GraphOpsNodeDriverContext ctx)
    {
        Entity weakest = Entity.Null;
        int min = int.MaxValue;
        for (int i = 0; i < _friends.Length; i++)
        {
            if (!ctx.Relationships!.HasLink(ctx.Caster, _friends[i], _socialBondTypeId))
            {
                continue;
            }

            int loyalty = ctx.Relationships.GetMetric(ctx.Caster, _friends[i], _socialBondTypeId, _loyaltyMetricId);
            if (loyalty < min)
            {
                min = loyalty;
                weakest = _friends[i];
            }
        }

        if (weakest == Entity.Null)
        {
            throw new InvalidOperationException("Rel gallery has no live friend chain to pick the weakest from.");
        }

        return weakest;
    }

    private Entity FriendWithLoyalty(GraphOpsNodeDriverContext ctx, int loyalty)
    {
        for (int i = 0; i < _friends.Length; i++)
        {
            if (_linked[i] == 0)
            {
                continue;
            }

            if (ctx.Relationships!.GetMetric(ctx.Caster, _friends[i], _socialBondTypeId, _loyaltyMetricId) == loyalty)
            {
                return _friends[i];
            }
        }

        throw new InvalidOperationException($"No friend currently holds 好感 {loyalty}.");
    }

    private int CountLinkedFriends(GraphOpsNodeDriverContext ctx)
    {
        int count = 0;
        for (int i = 0; i < _friends.Length; i++)
        {
            if (ctx.Relationships!.HasLink(ctx.Caster, _friends[i], _socialBondTypeId))
            {
                count++;
            }
        }

        return count;
    }

    private void RefreshLinkedFlags(GraphOpsNodeDriverContext ctx)
    {
        for (int i = 0; i < _friends.Length; i++)
        {
            _linked[i] = ctx.Relationships!.HasLink(ctx.Caster, _friends[i], _socialBondTypeId) ? (byte)1 : (byte)0;
        }
    }

    private static void RequireMatches(string op, int count)
    {
        if (count <= 0)
        {
            throw new InvalidOperationException($"{op} returned 0 friends; silent empty friend chains are forbidden.");
        }
    }

    private static string EntityLabel(GraphOpsNodeDriverContext ctx, Entity entity)
    {
        int index = GraphOpsNodeActorBinding.IndexOf(ctx, entity);
        return index >= 0 ? ctx.Vignette.Actors[index].Name : entity == Entity.Null ? "无" : "未知";
    }
}
