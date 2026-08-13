using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class RelNodeDriver : IGraphOpsNodeDriver
{
    private bool _seeded;
    private int _socialBondTypeId;
    private int _loyaltyMetricId;
    private int _estrangedFlagId;
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
        CollectFriends(ctx);
        _linked = new byte[_friends.Length];
        _lit = new bool[_friends.Length];
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
        FillCaption(ctx, op, result, linksBefore, linksAfter);
        ApplyBars(ctx, op, result);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        if (caster < 0)
        {
            return;
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

                values["status"] = "连着";
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
