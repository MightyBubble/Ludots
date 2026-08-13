using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Placement;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.MultiLayerGraph;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class EventNodeDriver : IGraphOpsNodeDriver
{
    public const string DamageDealtTag = "Event.DamageDealt";
    public const string DispatchStubEffect = "Effect.GraphOp.DispatchStub";
    public const string DispatchPreset = "TargetToResolved";
    public const string SnapCollectionKey = "showcase.graph_op.snap";

    private const float RangeCm = 500f;
    private const float SnapRadiusCm = 200f;
    private const float PayloadFloatValue = 2.5f;
    private const int PayloadIntValue = 99;
    private const float HitMagnitude = 18f;

    private bool _seeded;
    private KnowledgeProjectionStore? _knowledge;
    private GasGraphRuntimeApi? _eventApi;
    private GameplayEventBus? _eventBus;
    private EffectRequestQueue? _effectRequests;
    private Entity _viewer;
    private IntVector2 _seedTargetPosCm;
    private int _dispatchTemplateId;
    private bool _overlayArmed;
    private float _overlayRangeMeters;
    private int _aimX;
    private int _aimY;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded)
        {
            SeedSimulation(ctx);
            _seeded = true;
        }

        SpawnStage(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (_eventApi == null || _eventBus == null || _effectRequests == null)
        {
            throw new InvalidOperationException($"Event driver for {ctx.Vignette.Op} was not seeded.");
        }

        _effectRequests.Clear();
        GraphOpsNodeExecuteResult result = ExecuteEventGraph(ctx);
        ApplyBeat(ctx, result);
        ctx.Metrics.Detail = FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        GraphOpsNodeVignetteLoader.RejectBannedCaption(ctx.Metrics.Detail, ctx.Vignette.Op, "detail");
        SyncStage(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int caster = FindRole(ctx, "caster");
        if (caster < 0)
        {
            return;
        }

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        string op = ctx.Vignette.Op;
        if (op is "ClampTargetToRange" or "IsPointInCircle" or "FanOutDispatchEffect" or "FanOutDispatchEffectDynamic")
        {
            GraphShowcaseStagePresenter.DrawTriggerRing(
                debugDraw,
                casterActor.X,
                casterActor.Y,
                _overlayRangeMeters,
                _overlayArmed);
        }

        if (op is "SnapToNearestInCollection" or "SnapToNearestGraphEdge")
        {
            GraphShowcaseStagePresenter.DrawTriggerRing(
                debugDraw,
                _aimX / 100f,
                _aimY / 100f,
                SnapRadiusCm / 100f,
                _overlayArmed);
        }

        if (op == "SnapToNearestGraphEdge")
        {
            GraphShowcaseStagePresenter.DrawPolyline(
                debugDraw,
                [
                    new System.Numerics.Vector2(0f, 0f),
                    new System.Numerics.Vector2(1f, 0f),
                    new System.Numerics.Vector2(2f, 0f)
                ],
                GraphShowcaseStagePresenter.PathColor);
        }

        int target = FindRole(ctx, "target");
        if (target >= 0)
        {
            GraphShowcaseStagePresenter.DrawAggroLine(
                debugDraw,
                casterActor.X,
                casterActor.Y,
                ctx.Vignette.Actors[target].X,
                ctx.Vignette.Actors[target].Y);
        }
    }

    private void SeedSimulation(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        ctx.SimActors = new Entity[actors.Length];
        ctx.ActorHealth = new float[actors.Length];
        for (int i = 0; i < actors.Length; i++)
        {
            GraphOpsNodeActor actor = actors[i];
            var position = WorldPositionCm.FromCmFloat(actor.X * 100f, actor.Y * 100f);
            Entity entity = string.Equals(actor.Role, "caster", StringComparison.Ordinal)
                ? ctx.SimWorld.Create(new PlayerIdentity { PlayerId = 1 }, position)
                : ctx.SimWorld.Create(position);
            ctx.SimActors[i] = entity;
            ctx.ActorHealth[i] = actor.Health;
            if (string.Equals(actor.Role, "caster", StringComparison.Ordinal))
            {
                ctx.Caster = entity;
            }
            else if (string.Equals(actor.Role, "target", StringComparison.Ordinal) && ctx.Target == Entity.Null)
            {
                ctx.Target = entity;
            }
        }

        if (ctx.Caster == Entity.Null)
        {
            throw new InvalidOperationException($"Event vignette {ctx.Vignette.Op} requires a caster actor.");
        }

        _viewer = ctx.SimActors[0];
        int viewerRole = FindRole(ctx, "viewer");
        if (viewerRole >= 0)
        {
            _viewer = ctx.SimActors[viewerRole];
        }

        TagRegistry.Register(DamageDealtTag);
        _dispatchTemplateId = EffectTemplateIdRegistry.Register(DispatchStubEffect);

        TargetDispatchPresetRegistry presets = GraphOpsNodeGallerySymbolResolver.DispatchPresets;
        EntityCollectionStore collections = GraphOpsNodeGallerySymbolResolver.Collections;
        _ = collections.KeyRegistry.Register(SnapCollectionKey);
        _eventBus = new GameplayEventBus();
        _effectRequests = new EffectRequestQueue();

        var types = new RelationshipTypeRegistry();
        int ownsType = types.Register("Owns");
        int controlsType = types.Register("Controls");
        var relationships = new RelationshipRuntime(
            ctx.SimWorld,
            types,
            new RelationshipMetricRegistry(),
            new RelationshipFlagRegistry(),
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(capacity: 16),
            new RelationshipReverseIndex(ctx.SimWorld));
        var ownership = new OwnershipResolver(relationships, ownsType);
        var controlDomains = new ControlDomainQuery(ctx.SimWorld, relationships, ownership, ownsType, controlsType);
        _knowledge = new KnowledgeProjectionStore();
        var knowledgeResolver = new KnowledgeProjectionResolver(_knowledge);

        _eventApi = new GasGraphRuntimeApi(
            ctx.SimWorld,
            spatialQueries: null,
            coords: null,
            eventBus: _eventBus,
            effectRequests: _effectRequests,
            relationshipRuntime: relationships,
            targetDispatchPresets: presets,
            entityCollections: collections);
        _eventApi.BindTopologyServices(controlDomains, knowledgeResolver, new DiscreteClock());
        _eventApi.BindLoadedGraphRuntime(BuildNavGraph());

        int targetRole = FindRole(ctx, "target");
        if (targetRole >= 0)
        {
            ctx.Target = ctx.SimActors[targetRole];
            ctx.TargetContext = ctx.Target;
        }

        var snapMembers = new List<Entity>();
        for (int i = 0; i < actors.Length; i++)
        {
            Entity entity = ctx.SimActors[i];
            if (entity == ctx.Caster || entity == _viewer)
            {
                continue;
            }

            ownership.EnsureOwnership(ctx.Caster, entity);
            _knowledge.Upsert(_viewer, entity, CreateDisclosure(_viewer));
            snapMembers.Add(entity);
        }

        if (ctx.Target != Entity.Null && ctx.Target != _viewer)
        {
            _knowledge.Upsert(_viewer, ctx.Target, CreateDisclosure(_viewer));
        }

        if (snapMembers.Count > 0)
        {
            collections.Replace(
                ctx.Caster,
                EntityCollectionDescriptor.Create(
                    SnapCollectionKey,
                    EntityCollectionSourceKind.Debug,
                    EntityCollectionRoleKind.Debug),
                snapMembers.ToArray());
        }

        _seedTargetPosCm = SeedTargetPos(ctx);
        _overlayRangeMeters = OverlayRangeMeters(ctx.Vignette.Op);
        ctx.Metrics.AgentCount = actors.Length;
        ctx.Metrics.Detail = ctx.Vignette.Beat;
    }

    private GraphOpsNodeExecuteResult ExecuteEventGraph(GraphOpsNodeDriverContext ctx)
    {
        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
        Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
        var targetList = new GraphTargetList(targets);
        int targetCount = FillFanOutTargets(ctx, targets);
        targetList.SetCount(targetCount);
        entities[0] = ctx.Caster;
        entities[1] = ctx.Target;
        entities[2] = _viewer;

        var state = new GraphExecutionState
        {
            World = ctx.SimWorld,
            Caster = ctx.Caster,
            ExplicitTarget = ctx.Target,
            TargetContext = ctx.Target,
            Viewer = _viewer,
            EventPayload = BuildPayload(ctx.Vignette.Op),
            TargetPosCm = _seedTargetPosCm,
            Api = _eventApi!,
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

        _eventBus!.Update();
        _aimX = state.TargetPosCm.X;
        _aimY = state.TargetPosCm.Y;
        byte dest = ctx.FeaturedDest;
        GraphInstruction featured = FindFeaturedInstruction(ctx);
        return new GraphOpsNodeExecuteResult(
            floats[dest],
            ints[dest],
            ReadFeaturedBool(ctx.Vignette.Op, featured, bools, dest),
            entities[dest],
            state.ReturnInt,
            targetList.Count);
    }

    private void ApplyBeat(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        string op = ctx.Vignette.Op;
        int targetIndex = FindRole(ctx, "target");
        float healthBefore = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        ctx.CaptionValues.Clear();

        switch (op)
        {
            case "FanOutDispatchEffect":
                RequireDispatchTargets(ctx, result.TargetCount);
                RequireDispatched();
                HurtNonCasters(ctx, HitMagnitude);
                ctx.CaptionValues["result"] = "派给";
                ctx.CaptionValues["count"] = result.TargetCount.ToString();
                _overlayArmed = true;
                break;
            case "FanOutDispatchEffectDynamic":
                RequireDispatchTargets(ctx, result.TargetCount);
                RequireDispatched();
                HurtNonCasters(ctx, HitMagnitude);
                ctx.CaptionValues["result"] = "读出来再派";
                ctx.CaptionValues["count"] = result.TargetCount.ToString();
                _overlayArmed = true;
                break;
            case "SendEvent":
                if (_eventBus!.Events.Count <= 0)
                {
                    throw new InvalidOperationException("SendEvent gallery must broadcast; event bus stayed empty.");
                }

                HurtTarget(ctx, targetIndex, HitMagnitude);
                ctx.CaptionValues["result"] = "广播";
                break;
            case "LoadTargetPosX":
                MoveMarker(ctx, _aimX / 100f, ctx.Vignette.Actors[Math.Max(targetIndex, 0)].Y);
                ctx.CaptionValues["result"] = result.IntValue.ToString();
                break;
            case "LoadTargetPosY":
                MoveMarker(ctx, ctx.Vignette.Actors[Math.Max(targetIndex, 0)].X, _aimY / 100f);
                ctx.CaptionValues["result"] = result.IntValue.ToString();
                break;
            case "ClampTargetToRange":
                RequireClampedInRange(ctx);
                MoveMarker(ctx, _aimX / 100f, _aimY / 100f);
                ctx.CaptionValues["result"] = "拉回";
                ctx.CaptionValues["x"] = _aimX.ToString();
                ctx.CaptionValues["y"] = _aimY.ToString();
                _overlayArmed = true;
                break;
            case "IsPointInCircle":
                if (!result.BoolValue)
                {
                    throw new InvalidOperationException("IsPointInCircle gallery seeded in-circle but result was 不在圈里.");
                }

                ctx.CaptionValues["result"] = "在圈里";
                _overlayArmed = true;
                break;
            case "SnapToNearestInCollection":
                RequireSnapSucceeded(ctx, featuredSuccess: result.BoolValue, phrase: "吸到");
                MoveMarker(ctx, _aimX / 100f, _aimY / 100f);
                ctx.CaptionValues["result"] = "吸到";
                _overlayArmed = true;
                break;
            case "SnapToNearestGraphEdge":
                RequireSnapSucceeded(ctx, featuredSuccess: result.BoolValue, phrase: "路边");
                MoveMarker(ctx, _aimX / 100f, _aimY / 100f);
                ctx.CaptionValues["result"] = "路边";
                _overlayArmed = true;
                break;
            case "LoadViewer":
                if (result.EntityValue == Entity.Null)
                {
                    throw new InvalidOperationException("LoadViewer gallery did not read the viewer entity.");
                }

                ctx.CaptionValues["result"] = "自己这侧";
                break;
            case "LoadEventPayloadInt":
                ctx.CaptionValues["result"] = result.IntValue.ToString();
                break;
            case "LoadEventPayloadFloat":
                ctx.CaptionValues["result"] = result.FloatValue.ToString("0.#");
                break;
            case "ControlDomainResolve":
                if (result.EntityValue != ctx.Caster)
                {
                    throw new InvalidOperationException("ControlDomainResolve gallery must resolve to the captain caster.");
                }

                ctx.CaptionValues["result"] = "说了算";
                break;
            case "ControlDomainControls":
                if (!result.BoolValue)
                {
                    throw new InvalidOperationException("ControlDomainControls gallery expected 管得着.");
                }

                ctx.CaptionValues["result"] = "管得着";
                break;
            case "KnowledgeHasProjection":
                if (!result.BoolValue)
                {
                    throw new InvalidOperationException("KnowledgeHasProjection gallery expected 看得见.");
                }

                ctx.CaptionValues["result"] = "看得见";
                break;
            default:
                throw new InvalidOperationException($"EventNodeDriver does not host op '{op}'.");
        }

        if (targetIndex >= 0)
        {
            ctx.CaptionValues["healthBefore"] = healthBefore.ToString("0");
            ctx.CaptionValues["healthAfter"] = ctx.ActorHealth[targetIndex].ToString("0");
        }
    }

    private GraphEventPayload BuildPayload(string op)
    {
        return op switch
        {
            "FanOutDispatchEffectDynamic" => new GraphEventPayload
            {
                PayloadA = _dispatchTemplateId,
                PayloadB = _dispatchTemplateId,
                FloatA = PayloadFloatValue
            },
            "LoadEventPayloadInt" => new GraphEventPayload { PayloadA = PayloadIntValue },
            "LoadEventPayloadFloat" => new GraphEventPayload { FloatA = PayloadFloatValue },
            _ => new GraphEventPayload
            {
                PayloadA = PayloadIntValue,
                FloatA = PayloadFloatValue
            }
        };
    }

    private static IntVector2 SeedTargetPos(GraphOpsNodeDriverContext ctx)
    {
        return ctx.Vignette.Op switch
        {
            "LoadTargetPosX" or "LoadTargetPosY" => new IntVector2(360, 200),
            "ClampTargetToRange" => new IntVector2(2000, 0),
            "IsPointInCircle" => ActorPosCm(ctx, "caster") + new IntVector2(50, 0),
            "SnapToNearestInCollection" => new IntVector2(80, 30),
            "SnapToNearestGraphEdge" => new IntVector2(36, 20),
            _ => new IntVector2(36, 20)
        };
    }

    private static IntVector2 ActorPosCm(GraphOpsNodeDriverContext ctx, string role)
    {
        int index = FindRole(ctx, role);
        if (index < 0)
        {
            throw new InvalidOperationException($"Event vignette {ctx.Vignette.Op} missing '{role}' actor.");
        }

        GraphOpsNodeActor actor = ctx.Vignette.Actors[index];
        return new IntVector2((int)MathF.Round(actor.X * 100f), (int)MathF.Round(actor.Y * 100f));
    }

    private static float OverlayRangeMeters(string op)
    {
        return op is "FanOutDispatchEffect" or "FanOutDispatchEffectDynamic" ? 2.6f : RangeCm / 100f;
    }

    private void RequireDispatched()
    {
        if (_effectRequests!.Count <= 0)
        {
            throw new InvalidOperationException("Fan-out gallery dispatched 0 effect requests.");
        }
    }

    private static void RequireDispatchTargets(GraphOpsNodeDriverContext ctx, int count)
    {
        if (count <= 0)
        {
            throw new InvalidOperationException($"Fan-out gallery {ctx.Vignette.Op} has 0 targets.");
        }
    }

    private void RequireClampedInRange(GraphOpsNodeDriverContext ctx)
    {
        if (!PlacementValidation.TryGetEntityWorldPositionCm(ctx.SimWorld, ctx.Caster, out Fix64Vec2 originCm))
        {
            throw new InvalidOperationException("ClampTargetToRange gallery caster has no WorldPositionCm.");
        }

        var clamped = Fix64Vec2.FromInt(_aimX, _aimY);
        if (!PlacementValidation.IsPointInCircle(in clamped, in originCm, Fix64.FromFloat(RangeCm)))
        {
            throw new InvalidOperationException(
                $"ClampTargetToRange gallery still out of range after clamp: ({_aimX},{_aimY}).");
        }
    }

    private static void RequireSnapSucceeded(GraphOpsNodeDriverContext ctx, bool featuredSuccess, string phrase)
    {
        if (!featuredSuccess)
        {
            throw new InvalidOperationException(
                $"{ctx.Vignette.Op} gallery snap did not succeed; refuse silent always-fail ({phrase}).");
        }
    }

    private static bool ReadFeaturedBool(
        string op,
        in GraphInstruction featured,
        Span<byte> bools,
        byte dest)
    {
        if (op == "SnapToNearestInCollection")
        {
            if (featured.Flags == byte.MaxValue)
            {
                return false;
            }

            return bools[featured.Flags] != 0;
        }

        return bools[dest] != 0;
    }

    private static GraphInstruction FindFeaturedInstruction(GraphOpsNodeDriverContext ctx)
    {
        if (!GraphNodeOpParser.TryParse(ctx.Vignette.Op, out GraphNodeOp featuredOp))
        {
            throw new InvalidOperationException($"Unknown featured op '{ctx.Vignette.Op}'.");
        }

        GraphInstruction[] program = ctx.Compiled.Program;
        GraphInstructionSourceMap map = ctx.Compiled.SourceMap;
        for (int i = 0; i < program.Length; i++)
        {
            if (!map.TryGetSource(i, out GraphInstructionSource source) ||
                !string.Equals(source.NodeId, ctx.Vignette.FeaturedNodeId, StringComparison.Ordinal))
            {
                continue;
            }

            if (program[i].Op == (ushort)featuredOp)
            {
                return program[i];
            }
        }

        throw new InvalidOperationException(
            $"Compiled graph for {ctx.Vignette.Op} is missing featured node '{ctx.Vignette.FeaturedNodeId}'.");
    }

    private static int FillFanOutTargets(GraphOpsNodeDriverContext ctx, Span<Entity> targets)
    {
        int count = 0;
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < actors.Length && count < targets.Length; i++)
        {
            if (string.Equals(actors[i].Role, "caster", StringComparison.Ordinal) ||
                string.Equals(actors[i].Role, "viewer", StringComparison.Ordinal))
            {
                continue;
            }

            targets[count++] = ctx.SimActors[i];
        }

        return count;
    }

    private static void HurtNonCasters(GraphOpsNodeDriverContext ctx, float amount)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            if (string.Equals(actors[i].Role, "caster", StringComparison.Ordinal))
            {
                continue;
            }

            HurtTarget(ctx, i, amount);
        }
    }

    private static void HurtTarget(GraphOpsNodeDriverContext ctx, int targetIndex, float amount)
    {
        if (targetIndex < 0)
        {
            return;
        }

        float opening = ctx.Vignette.Actors[targetIndex].Health;
        float next = ctx.ActorHealth[targetIndex] - amount;
        ctx.ActorHealth[targetIndex] = next <= 0f ? opening : next;
    }

    private static void MoveMarker(GraphOpsNodeDriverContext ctx, float x, float y)
    {
        int target = FindRole(ctx, "target");
        if (target < 0)
        {
            return;
        }

        ctx.Vignette.Actors[target].X = x;
        ctx.Vignette.Actors[target].Y = y;
    }

    private static LoadedGraphRuntime BuildNavGraph()
    {
        var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm: 1000, loadedChunkCapacity: 1);
        var store = new ChunkedNodeGraphStore();
        store.SubscribeToLoadedChunks(loadedChunks);
        long chunkKey = GraphChunkKey.Pack(0, 0);
        var graphBuilder = new NodeGraphBuilder(3, 2);
        graphBuilder.AddNode(0, 0);
        graphBuilder.AddNode(100, 0);
        graphBuilder.AddNode(200, 0);
        graphBuilder.AddEdge(0, 1, 100f);
        graphBuilder.AddEdge(1, 2, 100f);
        store.AddOrReplace(chunkKey, new GraphChunkData(graphBuilder.Build(), Array.Empty<GraphCrossEdge>()));
        loadedChunks.SetLoaded(chunkKey, loaded: true);
        return new LoadedGraphRuntime(store, loadedChunks, preferredProjectionCellSizeCm: 100);
    }

    private static KnowledgeDisclosureRecord CreateDisclosure(Entity source)
    {
        KnowledgeIdMask256 empty = KnowledgeIdMask256.Empty;
        return new KnowledgeDisclosureRecord(
            KnowledgePresence.LiveVisible,
            KnowledgePositionAccess.Live,
            in empty,
            in empty,
            in empty,
            source,
            observedTick: 0,
            expiryTick: int.MaxValue,
            confidencePermille: 1000,
            revision: 0);
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
}
