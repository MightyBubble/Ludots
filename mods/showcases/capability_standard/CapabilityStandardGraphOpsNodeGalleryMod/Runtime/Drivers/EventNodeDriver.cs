using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Placement;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.MultiLayerGraph;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class EventNodeDriver : IGraphOpsNodeDriver
{
    public const string DamageDealtTag = "Event.DamageDealt";
    public const string DispatchStubEffect = "Effect.GraphOp.DispatchStub";
    public const string DispatchPreset = GraphOpsNodeGalleryHost.TargetToResolvedPreset;
    public const string SnapCollectionKey = GraphOpsNodeGalleryHost.SnapCollectionKey;

    private const float RangeCm = 500f;
    private const float SnapRadiusCm = 200f;
    private const float PayloadFloatValue = 2.5f;
    private const int PayloadIntValue = 99;
    private const float HitMagnitude = 18f;

    private bool _seeded;
    private Entity _viewer;
    private int _dispatchTemplateId;
    private bool _overlayArmed;
    private float _overlayRangeMeters;
    private int _aimX;
    private int _aimY;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        if (ctx.EventBus == null || ctx.EffectRequests == null || ctx.Ownership == null || ctx.Knowledge == null)
        {
            throw new InvalidOperationException($"Event gallery '{ctx.Vignette.Op}' requires host event/ownership/knowledge services.");
        }

        TagRegistry.Register(DamageDealtTag);
        _dispatchTemplateId = EffectTemplateIdRegistry.GetId(DispatchStubEffect);
        if (_dispatchTemplateId <= 0)
        {
            throw new InvalidOperationException(
                $"Event gallery requires '{DispatchStubEffect}' loaded through EffectTemplateLoader.");
        }

        if (ctx.OwnsSimulationWorld)
        {
            ctx.Api.BindLoadedGraphRuntime(BuildNavGraph());
        }
        BindViewer(ctx);
        SeedOwnershipAndKnowledge(ctx);
        ctx.TargetPosCm = SeedTargetPos(ctx);
        ctx.HasTargetPosCm = true;
        ctx.EventPayload = BuildPayload(ctx.Vignette.Op);
        PrefillFanOut(ctx);
        _overlayRangeMeters = OverlayRangeMeters(ctx.Vignette.Op);
        ctx.Metrics.AgentCount = ctx.SimActors.Length;
        ctx.Metrics.Detail = ctx.Vignette.Beat;
        _seeded = true;
        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded || ctx.EventBus == null || ctx.EffectRequests == null)
        {
            throw new InvalidOperationException($"Event driver for {ctx.Vignette.Op} was not seeded.");
        }

        ctx.EffectRequests.Clear();
        ctx.EventPayload = BuildPayload(ctx.Vignette.Op);
        GraphOpsNodeExecuteResult result = ctx.ExecuteFeaturedGraph();
        ctx.EventBus.Update();
        _aimX = ctx.TargetPosCm.X;
        _aimY = ctx.TargetPosCm.Y;
        ApplyBeat(ctx, result);
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        GraphOpsNodeVignetteLoader.RejectBannedCaption(ctx.Metrics.Detail, ctx.Vignette.Op, "detail");
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
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

        int target = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
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

    private void BindViewer(GraphOpsNodeDriverContext ctx)
    {
        _viewer = ctx.SimActors[0];
        int viewerRole = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "viewer");
        if (viewerRole >= 0)
        {
            _viewer = ctx.SimActors[viewerRole];
        }

        ctx.Viewer = _viewer;
        int targetRole = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        if (targetRole >= 0)
        {
            ctx.Target = ctx.SimActors[targetRole];
            ctx.TargetContext = ctx.Target;
        }
    }

    private void SeedOwnershipAndKnowledge(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            Entity entity = ctx.SimActors[i];
            if (entity == ctx.Caster || entity == _viewer)
            {
                continue;
            }

            ctx.Ownership!.EnsureOwnership(ctx.Caster, entity);
            ctx.Knowledge!.Upsert(_viewer, entity, CreateDisclosure(_viewer));
        }

        if (ctx.Target != Entity.Null && ctx.Target != _viewer)
        {
            ctx.Knowledge!.Upsert(_viewer, ctx.Target, CreateDisclosure(_viewer));
        }
    }

    private void PrefillFanOut(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        var targets = new List<Entity>();
        for (int i = 0; i < actors.Length; i++)
        {
            if (string.Equals(actors[i].Role, "caster", StringComparison.Ordinal) ||
                string.Equals(actors[i].Role, "viewer", StringComparison.Ordinal))
            {
                continue;
            }

            targets.Add(ctx.SimActors[i]);
        }

        ctx.PrefillTargets = targets.ToArray();
        ctx.PrefillTargetCount = targets.Count;
    }

    private void ApplyBeat(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        string op = ctx.Vignette.Op;
        int targetIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        float healthBefore = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        ctx.CaptionValues.Clear();
        bool featuredBool = ReadFeaturedBool(ctx);

        switch (op)
        {
            case "FanOutDispatchEffect":
                RequireDispatchTargets(ctx, result.TargetCount);
                RequireDispatched(ctx);
                HurtNonCasters(ctx, HitMagnitude);
                ctx.CaptionValues["result"] = "派给";
                ctx.CaptionValues["count"] = result.TargetCount.ToString();
                _overlayArmed = true;
                break;
            case "FanOutDispatchEffectDynamic":
                RequireDispatchTargets(ctx, result.TargetCount);
                RequireDispatched(ctx);
                HurtNonCasters(ctx, HitMagnitude);
                ctx.CaptionValues["result"] = "读出来再派";
                ctx.CaptionValues["count"] = result.TargetCount.ToString();
                _overlayArmed = true;
                break;
            case "SendEvent":
                if (ctx.EventBus!.Events.Count <= 0)
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
                RequireSnapSucceeded(ctx, featuredBool, "吸到");
                MoveMarker(ctx, _aimX / 100f, _aimY / 100f);
                ctx.CaptionValues["result"] = "吸到";
                _overlayArmed = true;
                break;
            case "SnapToNearestGraphEdge":
                RequireSnapSucceeded(ctx, featuredBool, "路边");
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
        int index = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, role);
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

    private static void RequireDispatched(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.EffectRequests!.Count <= 0)
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

    private static bool ReadFeaturedBool(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Vignette.Op != "SnapToNearestInCollection")
        {
            return ctx.LastBoolRegisters[ctx.FeaturedDest] != 0;
        }

        GraphInstruction featured = FindFeaturedInstruction(ctx);
        if (featured.Flags == byte.MaxValue)
        {
            return false;
        }

        return ctx.LastBoolRegisters[featured.Flags] != 0;
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
        next = next <= 0f ? opening : next;
        GraphOpsNodeActorBinding.WriteHealth(
            ctx.SimWorld,
            ctx.SimActors[targetIndex],
            next,
            ctx.Vignette.Actors[targetIndex].HealthMax);
        ctx.ActorHealth[targetIndex] = next;
    }

    private static void MoveMarker(GraphOpsNodeDriverContext ctx, float x, float y)
    {
        int target = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
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
}
