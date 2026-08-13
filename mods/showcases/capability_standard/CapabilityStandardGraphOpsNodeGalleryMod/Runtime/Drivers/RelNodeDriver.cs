using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class RelNodeDriver : IGraphOpsNodeDriver
{
    private const int IncomingFriendA = 1;
    private const int IncomingFriendB = 2;
    private const int TrustedLoyaltyFloor = 50;

    private bool _seeded;
    private GasGraphRuntimeApi? _api;
    private RelationshipRuntime? _relationships;
    private int _socialBondTypeId;
    private int _loyaltyMetricId;
    private int _trustedFlagId;
    private int _estrangedFlagId;
    private Entity _player;
    private Entity[] _friends = Array.Empty<Entity>();
    private byte[] _linked = Array.Empty<byte>();
    private bool[] _lit = Array.Empty<bool>();
    private readonly Entity[] _matches = new Entity[GraphVmLimits.MaxTargets];
    private int _matchCount;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded)
        {
            SeedWorld(ctx);
            _seeded = true;
        }

        SpawnStage(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded || _relationships == null || _api == null)
        {
            throw new InvalidOperationException($"RelNodeDriver.Seed required before Tick for {ctx.Vignette.Op}.");
        }

        if (!GraphNodeOpParser.TryParse(ctx.Vignette.Op, out GraphNodeOp op))
        {
            throw new InvalidOperationException($"Unknown rel op '{ctx.Vignette.Op}'.");
        }

        int linksBefore = CountLinkedFriends();
        ctx.Target = op is GraphNodeOp.RelationshipRemoveLink or GraphNodeOp.RelationshipSetFlag
            ? RequireWeakestLinkedFriend()
            : _friends[0];
        RelExecuteResult result = ExecuteFeatured(ctx);
        RefreshLinkedFlags();
        int linksAfter = CountLinkedFriends();
        FillCaption(ctx, op, result, linksBefore, linksAfter);
        ApplyBars(ctx, op, result);
        SyncStage(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int caster = FindRole(ctx, "caster");
        if (caster < 0)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < _friends.Length; i++)
        {
            int actorIndex = IndexOf(ctx, _friends[i]);
            if (ctx.ActorHealth[actorIndex] <= 0f)
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

    private void SeedWorld(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        int friendCount = CountFriends(actors);
        if (friendCount < 4)
        {
            throw new InvalidOperationException(
                $"Rel vignette {ctx.Vignette.Op} requires a caster plus at least 4 friends.");
        }

        var types = new RelationshipTypeRegistry();
        var metrics = new RelationshipMetricRegistry();
        var flags = new RelationshipFlagRegistry();
        var reasons = new RelationshipReasonRegistry();
        _socialBondTypeId = types.Register("SocialBond");
        _loyaltyMetricId = metrics.Register("Loyalty", -100, 100, 0);
        _trustedFlagId = flags.Register("Trusted");
        _estrangedFlagId = flags.Register("Estranged");
        reasons.Register("Scenario.Setup");
        if (_trustedFlagId < 0 || _estrangedFlagId < 0)
        {
            throw new InvalidOperationException("Rel gallery requires Trusted and Estranged flags.");
        }

        if (ctx.Compiled.Package == null)
        {
            throw new InvalidOperationException($"Rel graph for {ctx.Vignette.Op} compiled without a symbol package.");
        }

        PatchRelProgram(
            ctx.Compiled.Program,
            _socialBondTypeId,
            _loyaltyMetricId,
            _trustedFlagId,
            _estrangedFlagId);

        _relationships = new RelationshipRuntime(
            ctx.SimWorld,
            types,
            metrics,
            flags,
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(),
            new RelationshipReverseIndex(ctx.SimWorld));
        var tagOps = new TagOps(
            new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
            new TagRuleRegistry(),
            new GasBudget());
        var entityQueries = new EntitySetQueryRuntime(ctx.SimWorld, tagOps, _relationships);
        _api = new GasGraphRuntimeApi(
            ctx.SimWorld,
            tagOps: tagOps,
            relationshipRuntime: _relationships,
            typeRegistry: types,
            metricRegistry: metrics,
            flagRegistry: flags,
            reasonRegistry: reasons,
            entityQueries: entityQueries);

        ctx.SimActors = new Entity[actors.Length];
        ctx.ActorHealth = new float[actors.Length];
        _friends = new Entity[friendCount];
        _linked = new byte[friendCount];
        _lit = new bool[friendCount];

        int friendIndex = 0;
        for (int i = 0; i < actors.Length; i++)
        {
            Entity entity = ctx.SimWorld.Create();
            ctx.SimActors[i] = entity;
            if (string.Equals(actors[i].Role, "caster", StringComparison.Ordinal))
            {
                ctx.Caster = entity;
                _player = entity;
                ctx.ActorHealth[i] = actors[i].Health;
                continue;
            }

            if (friendIndex >= friendCount)
            {
                throw new InvalidOperationException($"Rel vignette {ctx.Vignette.Op} has extra non-caster actors.");
            }

            _friends[friendIndex] = entity;
            ctx.ActorHealth[i] = (int)MathF.Round(actors[i].Health);
            friendIndex++;
        }

        if (_player == Entity.Null)
        {
            throw new InvalidOperationException($"Rel vignette {ctx.Vignette.Op} requires a caster actor.");
        }

        int seeded = 0;
        for (int i = 0; i < actors.Length; i++)
        {
            if (string.Equals(actors[i].Role, "caster", StringComparison.Ordinal))
            {
                continue;
            }

            int loyalty = (int)MathF.Round(actors[i].Health);
            Entity friend = _friends[seeded];
            _relationships.SetMetric(_player, friend, _socialBondTypeId, _loyaltyMetricId, loyalty, reasonId: 0);
            _linked[seeded] = 1;
            if (loyalty >= TrustedLoyaltyFloor)
            {
                _relationships.SetFlag(_player, friend, _socialBondTypeId, _trustedFlagId, true);
            }

            seeded++;
        }

        _relationships.SetMetric(_friends[IncomingFriendA], _player, _socialBondTypeId, _loyaltyMetricId, 70, reasonId: 0);
        _relationships.SetMetric(_friends[IncomingFriendB], _player, _socialBondTypeId, _loyaltyMetricId, 55, reasonId: 0);
        ctx.Target = _friends[0];
        ctx.Metrics.AgentCount = actors.Length;
        ctx.Metrics.Detail = ctx.Vignette.Beat;
    }

    private static void PatchRelProgram(
        GraphInstruction[] program,
        int socialBondTypeId,
        int loyaltyMetricId,
        int trustedFlagId,
        int estrangedFlagId)
    {
        byte type = checked((byte)socialBondTypeId);
        for (int i = 0; i < program.Length; i++)
        {
            ref GraphInstruction ins = ref program[i];
            switch ((GraphNodeOp)ins.Op)
            {
                case GraphNodeOp.RelationshipQueryOutgoing:
                case GraphNodeOp.RelationshipQueryIncoming:
                case GraphNodeOp.RelationshipQueryMutual:
                case GraphNodeOp.RelationshipQueryBetweenPair:
                case GraphNodeOp.RelationshipRemoveLink:
                    ins.Dst = type;
                    break;
                case GraphNodeOp.RelationshipGetMetric:
                case GraphNodeOp.RelationshipAggSumMetric:
                case GraphNodeOp.RelationshipAggMaxMetric:
                case GraphNodeOp.RelationshipAggAverageMetric:
                case GraphNodeOp.RelationshipAggMinMetric:
                case GraphNodeOp.RelationshipAggMaxEntityByMetric:
                case GraphNodeOp.RelationshipAggMinEntityByMetric:
                    ins.Imm = loyaltyMetricId;
                    ins.Flags = type;
                    break;
                case GraphNodeOp.RelationshipFilterMetricRange:
                case GraphNodeOp.RelationshipSortByMetric:
                    ins.Imm = loyaltyMetricId;
                    ins.Dst = type;
                    break;
                case GraphNodeOp.RelationshipFilterFlag:
                    ins.Imm = trustedFlagId;
                    ins.Dst = type;
                    break;
                case GraphNodeOp.RelationshipSetFlag:
                    ins.Imm = estrangedFlagId;
                    ins.Flags = type;
                    break;
                case GraphNodeOp.RelationshipHasLink:
                    ins.Flags = type;
                    break;
            }
        }
    }

    private RelExecuteResult ExecuteFeatured(GraphOpsNodeDriverContext ctx)
    {
        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
        Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
        entities[0] = ctx.Caster;
        entities[1] = ctx.Target;
        var targetList = new GraphTargetList(targets);
        var state = new GraphExecutionState
        {
            World = ctx.SimWorld,
            Caster = ctx.Caster,
            ExplicitTarget = ctx.Target,
            Api = _api!,
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
                $"Featured rel graph for {ctx.Vignette.Op} ended with status {state.Status}.");
        }

        _matchCount = state.TargetList.Count;
        ReadOnlySpan<Entity> matched = state.TargetList.Span;
        for (int i = 0; i < _matchCount; i++)
        {
            _matches[i] = matched[i];
        }

        byte dest = ctx.FeaturedDest;
        return new RelExecuteResult(
            dest < ints.Length ? ints[dest] : 0,
            dest < bools.Length && bools[dest] != 0,
            dest < entities.Length ? entities[dest] : Entity.Null,
            state.TargetList.Count);
    }

    private void FillCaption(
        GraphOpsNodeDriverContext ctx,
        GraphNodeOp op,
        RelExecuteResult result,
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
                if (!_relationships!.HasFlag(_player, ctx.Target, _socialBondTypeId, _estrangedFlagId))
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
                values["friend"] = EntityLabel(ctx, _matches[0]);
                break;
            case GraphNodeOp.RelationshipSortByMetric:
                RequireMatches(ctx.Vignette.Op, result.TargetCount);
                values["friend"] = EntityLabel(ctx, _matches[0]);
                values["loyalty"] = _relationships!.GetMetric(
                    _player,
                    _matches[0],
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
                values["friend"] = EntityLabel(ctx, FriendWithLoyalty(result.IntValue));
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

        ctx.Metrics.Detail = FormatDetail(ctx.Vignette.DetailTemplate, values);
    }

    private void ApplyBars(GraphOpsNodeDriverContext ctx, GraphNodeOp op, RelExecuteResult result)
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
                MarkMatchesLit();
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

        for (int i = 0; i < _friends.Length; i++)
        {
            int loyalty = _linked[i] == 0
                ? 0
                : _relationships!.GetMetric(_player, _friends[i], _socialBondTypeId, _loyaltyMetricId);
            ctx.ActorHealth[IndexOf(ctx, _friends[i])] = _lit[i] ? loyalty : 0;
        }
    }

    private void MarkMatchesLit()
    {
        for (int m = 0; m < _matchCount; m++)
        {
            MarkFriendLit(_matches[m]);
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

    private Entity RequireWeakestLinkedFriend()
    {
        Entity weakest = Entity.Null;
        int min = int.MaxValue;
        for (int i = 0; i < _friends.Length; i++)
        {
            if (!_relationships!.HasLink(_player, _friends[i], _socialBondTypeId))
            {
                continue;
            }

            int loyalty = _relationships.GetMetric(_player, _friends[i], _socialBondTypeId, _loyaltyMetricId);
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

    private Entity FriendWithLoyalty(int loyalty)
    {
        for (int i = 0; i < _friends.Length; i++)
        {
            if (_linked[i] == 0)
            {
                continue;
            }

            if (_relationships!.GetMetric(_player, _friends[i], _socialBondTypeId, _loyaltyMetricId) == loyalty)
            {
                return _friends[i];
            }
        }

        throw new InvalidOperationException($"No friend currently holds 好感 {loyalty}.");
    }

    private int CountLinkedFriends()
    {
        int count = 0;
        for (int i = 0; i < _friends.Length; i++)
        {
            if (_relationships!.HasLink(_player, _friends[i], _socialBondTypeId))
            {
                count++;
            }
        }

        return count;
    }

    private void RefreshLinkedFlags()
    {
        for (int i = 0; i < _friends.Length; i++)
        {
            _linked[i] = _relationships!.HasLink(_player, _friends[i], _socialBondTypeId) ? (byte)1 : (byte)0;
        }
    }

    private static void RequireMatches(string op, int count)
    {
        if (count <= 0)
        {
            throw new InvalidOperationException($"{op} returned 0 friends; silent empty friend chains are forbidden.");
        }
    }

    private static int IndexOf(GraphOpsNodeDriverContext ctx, Entity entity)
    {
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            if (ctx.SimActors[i] == entity)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Rel actor entity {entity.Id} is not in SimActors.");
    }

    private static int CountFriends(GraphOpsNodeActor[] actors)
    {
        int count = 0;
        for (int i = 0; i < actors.Length; i++)
        {
            if (!string.Equals(actors[i].Role, "caster", StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static string EntityLabel(GraphOpsNodeDriverContext ctx, Entity entity)
    {
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            if (ctx.SimActors[i] == entity)
            {
                return ctx.Vignette.Actors[i].Name;
            }
        }

        return entity == Entity.Null ? "无" : "未知";
    }

    private static int FindActorIndex(Entity entity, GraphOpsNodeDriverContext ctx)
    {
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            if (ctx.SimActors[i] == entity)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Rel actor entity is not in the seeded sim actor list.");
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

    private static int FindRole(GraphOpsNodeDriverContext ctx, string role)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            if (string.Equals(actors[i].Role, role, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static void SpawnStage(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Stage == null || ctx.StageProxies.Length > 0)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        ctx.StageProxies = new Entity[actors.Length];
        for (int i = 0; i < actors.Length; i++)
        {
            GraphOpsNodeActor actor = actors[i];
            ctx.StageProxies[i] = ctx.Stage.Spawn(
                actor.Template,
                actor.Name,
                actor.X,
                actor.Y,
                ctx.ActorHealth[i],
                actor.HealthMax);
        }
    }

    private static void SyncStage(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Stage == null || ctx.StageProxies.Length == 0)
        {
            return;
        }

        for (int i = 0; i < ctx.StageProxies.Length; i++)
        {
            GraphOpsNodeActor actor = ctx.Vignette.Actors[i];
            ctx.Stage.SetPosition(ctx.StageProxies[i], actor.X, actor.Y);
            ctx.Stage.SetHealth(ctx.StageProxies[i], ctx.ActorHealth[i], actor.HealthMax);
        }
    }

    private readonly record struct RelExecuteResult(int IntValue, bool BoolValue, Entity EntityValue, int TargetCount);
}
