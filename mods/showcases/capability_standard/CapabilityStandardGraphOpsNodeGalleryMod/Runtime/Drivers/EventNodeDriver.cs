using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
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
using Ludots.Platform.Abstractions;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class EventNodeDriver : IGraphOpsNodeDriver
{
    public const string DamageDealtTag = "Event.DamageDealt";
    public const string MarkEffect = "Effect.GraphOps.Mark";
    public const string PayloadProducerGraphFile = "LoadEventPayloadFloat.producer.json";
    public const string SendEventListenerGraphFile = "SendEvent.listener.json";

    private const float RangeCm = 500f;
    private const float SnapRadiusCm = 200f;
    private const float FanOutRadiusCm = 260f;
    private const float StrikeDamage = 18f;
    private const float SnapGhostRadius = 0.4f;
    private const float SnapDotRadius = 0.15f;
    private const int OutPointXCm = 650;
    private const float BadgeLift = 1.15f;
    private const float ChipSize = 0.22f;

    private static readonly DebugDrawColor CommandWhite = GraphShowcaseStagePresenter.GateColor;
    private static readonly DebugDrawColor LetterSeal = GraphShowcaseStagePresenter.SentryIdle;

    private readonly float[] _floats = new float[GraphVmLimits.MaxFloatRegisters];
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly Entity[] _entities = new Entity[GraphVmLimits.MaxEntityRegisters];
    private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];

    private bool _seeded;
    private Entity _viewer;
    private int _markTemplateId;
    private GraphInstruction[]? _producerProgram;
    private GraphInstruction[]? _listenerProgram;
    private bool _overlayArmed;
    private float _overlayRangeMeters;
    private int _aimX;
    private int _aimY;
    private int _preSnapX;
    private int _preSnapY;
    private bool _snapAimed;
    private bool _pointInside;
    private bool _pointOutsideSeen;
    private int _posReadoutX;
    private int _posReadoutY;

    public int LastBusEventCount { get; private set; }
    public GameplayEvent LastBusEvent { get; private set; }
    public GraphOpsNodeExecuteResult LastFeaturedResult { get; private set; }

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        if (ctx.EventBus == null || ctx.EffectRequests == null || ctx.Ownership == null || ctx.Knowledge == null)
        {
            throw new InvalidOperationException($"Event gallery '{ctx.Vignette.Op}' requires host event/ownership/knowledge services.");
        }

        string op = ctx.Vignette.Op;
        TagRegistry.Register(DamageDealtTag);
        _markTemplateId = EffectTemplateIdRegistry.GetId(MarkEffect);
        if (_markTemplateId <= 0)
        {
            throw new InvalidOperationException($"Event gallery requires '{MarkEffect}' loaded through EffectTemplateLoader.");
        }

        if (op is "LoadEventPayloadFloat" or "LoadEventPayloadInt")
        {
            _producerProgram = CompileAuxGraph(ctx, PayloadProducerGraphFile);
        }

        if (op == "SendEvent")
        {
            _listenerProgram = CompileAuxGraph(ctx, SendEventListenerGraphFile);
        }

        if (op == "SnapToNearestGraphEdge")
        {
            ctx.Api.BindLoadedGraphRuntime(BuildNavGraph());
        }

        BindViewer(ctx);
        SeedOwnershipAndKnowledge(ctx);
        ctx.TargetPosCm = SeedTargetPos(ctx);
        ctx.HasTargetPosCm = true;
        _preSnapX = ctx.TargetPosCm.X;
        _preSnapY = ctx.TargetPosCm.Y;
        ctx.EventPayload = BuildPayload(op);
        PrefillFanOut(ctx);
        _overlayRangeMeters = OverlayRangeMeters(op);
        ctx.Metrics.AgentCount = ctx.SimActors.Length;
        ctx.Metrics.Detail = ctx.Vignette.Beat;
        if (op == "KnowledgeHasProjection")
        {
            GraphOpsNodeActorBinding.SetHudLit(ctx, GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster"), lit: false);
        }

        _seeded = true;
        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded || ctx.EventBus == null || ctx.EffectRequests == null)
        {
            throw new InvalidOperationException($"Event driver for {ctx.Vignette.Op} was not seeded.");
        }

        string op = ctx.Vignette.Op;
        if (op is "FanOutDispatchEffect" or "FanOutDispatchEffectDynamic")
        {
            GraphOpsNodeActorBinding.RestoreVignetteHealth(ctx);
        }

        ctx.EventPayload = BuildPayload(op);
        int targetIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        float healthBefore = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;

        GraphOpsNodeExecuteResult result;
        if (op is "LoadEventPayloadFloat" or "LoadEventPayloadInt")
        {
            RunPayloadProducer(ctx);
            result = ctx.ExecuteFeaturedGraph();
        }
        else
        {
            result = ctx.ExecuteFeaturedGraph();
            GraphOpsNodeActorBinding.SyncActorHealthFromWorld(ctx);
            ctx.EventBus.Update();
            if (op == "SendEvent")
            {
                DispatchSendEventListener(ctx);
            }
        }

        LastFeaturedResult = result;
        _aimX = ctx.TargetPosCm.X;
        _aimY = ctx.TargetPosCm.Y;
        _snapAimed = true;
        ApplyBeat(ctx, result, healthBefore);
        RunSecondPasses(ctx);
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        GraphOpsNodeVignetteLoader.RejectBannedCaption(ctx.Metrics.Detail, ctx.Vignette.Op, "detail");
        GraphOpsNodeActorBinding.SyncHud(ctx);
        if (op == "KnowledgeHasProjection")
        {
            RunStrangerPass(ctx);
        }
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
            DrawSnapResidue(debugDraw);
        }

        int target = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        switch (op)
        {
            case "LoadViewer":
                DrawViewerRegister(ctx, debugDraw);
                break;
            case "SnapToNearestInCollection":
                DrawRosterSnap(ctx, debugDraw, target);
                break;
            case "SendEvent":
                DrawSendEventSignal(ctx, debugDraw, caster, target);
                break;
            case "ControlDomainResolve":
                DrawCommandChain(ctx, debugDraw, target);
                break;
            case "FanOutDispatchEffect":
                DrawPresetCardFan(ctx, debugDraw, caster);
                break;
            case "FanOutDispatchEffectDynamic":
                DrawCardSlotFan(ctx, debugDraw, caster);
                break;
            case "KnowledgeHasProjection":
                DrawKnowledgeContrast(ctx, debugDraw, caster, target);
                break;
            case "LoadEventPayloadFloat":
                DrawLetterBoard(ctx, debugDraw, caster, target, slot: 0, fractionalChip: true);
                break;
            case "LoadEventPayloadInt":
                DrawLetterBoard(ctx, debugDraw, caster, target, slot: 1, fractionalChip: false);
                break;
            case "LoadTargetPosX" or "LoadTargetPosY":
                DrawPosRulers(ctx, debugDraw, target);
                break;
            case "IsPointInCircle":
                DrawCircleVerdict(ctx, debugDraw, caster, target);
                break;
            case "ControlDomainControls":
                DrawControlsContrast(ctx, debugDraw, caster, target);
                break;
            case "ClampTargetToRange" or "SnapToNearestGraphEdge" when target >= 0:
                GraphShowcaseStagePresenter.DrawAggroLine(
                    debugDraw,
                    casterActor.X,
                    casterActor.Y,
                    ctx.Vignette.Actors[target].X,
                    ctx.Vignette.Actors[target].Y);
                break;
        }
    }

    private void DrawSnapResidue(DebugDrawCommandBuffer debugDraw)
    {
        float fromX = _preSnapX / 100f;
        float fromY = _preSnapY / 100f;
        GraphShowcaseStagePresenter.DrawGhostCircle(
            debugDraw,
            fromX,
            fromY,
            SnapGhostRadius,
            GraphShowcaseStagePresenter.GhostColor);
        if (!_snapAimed)
        {
            return;
        }

        float toX = _aimX / 100f;
        float toY = _aimY / 100f;
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            fromX,
            fromY,
            toX,
            toY,
            0.1f,
            GraphShowcaseStagePresenter.CasterColor);
        GraphShowcaseStagePresenter.DrawThickOutlineCircle(
            debugDraw,
            toX,
            toY,
            SnapDotRadius,
            GraphShowcaseStagePresenter.OutlineDark,
            GraphShowcaseStagePresenter.GateColor);
    }

    private void DrawViewerRegister(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int viewerIndex = GraphOpsNodeActorBinding.IndexOf(ctx, LastFeaturedResult.EntityValue);
        if (viewerIndex < 0)
        {
            return;
        }

        GraphOpsNodeActor viewer = ctx.Vignette.Actors[viewerIndex];
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw,
            viewer.X,
            viewer.Y + BadgeLift,
            GraphShowcaseStagePresenter.BadgeKind.Eye,
            CommandWhite);
        float boardX = viewer.X + 2.8f;
        float boardY = viewer.Y + 1.1f;
        GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, boardX, boardY, 1.6f, 0.8f, 1, CommandWhite);
        GraphShowcaseStagePresenter.DrawDashedDirectedLine(
            debugDraw,
            viewer.X,
            viewer.Y + 0.6f,
            boardX,
            boardY,
            0.06f,
            GraphShowcaseStagePresenter.CasterColor);
        DrawChip(debugDraw, boardX - 0.25f, boardY, GraphShowcaseStagePresenter.CasterColor);
    }

    private void DrawRosterSnap(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int target)
    {
        foreach (GraphOpsNodeCollection collection in ctx.Vignette.Collections)
        {
            if (!string.Equals(collection.Key, GraphOpsNodeGalleryHost.SnapCollectionKey, StringComparison.Ordinal))
            {
                continue;
            }

            for (int m = 0; m < collection.Members.Length; m++)
            {
                int index = GraphOpsNodeActorBinding.IndexOfId(ctx.Vignette, collection.Members[m]);
                GraphOpsNodeActor member = ctx.Vignette.Actors[index];
                GraphShowcaseStagePresenter.DrawActor(
                    debugDraw,
                    member.X,
                    member.Y,
                    0.45f,
                    GraphShowcaseStagePresenter.SentryIdle,
                    thickness: 0.08f);
            }
        }

        float fromX = _preSnapX / 100f;
        float fromY = _preSnapY / 100f;
        GraphShowcaseStagePresenter.DrawGhostCircle(
            debugDraw,
            fromX,
            fromY,
            SnapGhostRadius * 0.7f,
            GraphShowcaseStagePresenter.GhostColor);
        if (target < 0 || !_snapAimed)
        {
            return;
        }

        GraphOpsNodeActor snapped = ctx.Vignette.Actors[target];
        float toX = snapped.X;
        float toY = snapped.Y;
        float midX = (fromX + toX) / 2f;
        float midY = (fromY + toY) / 2f;
        float radius = MathF.Sqrt(MathF.Pow(toX - fromX, 2f) + MathF.Pow(toY - fromY, 2f)) / 2f;
        float fromDeg = MathF.Atan2(fromY - midY, fromX - midX) * 180f / MathF.PI;
        float toDeg = MathF.Atan2(toY - midY, toX - midX) * 180f / MathF.PI;
        GraphShowcaseStagePresenter.DrawArcArrow(
            debugDraw,
            midX,
            midY,
            radius,
            fromDeg,
            toDeg,
            GraphShowcaseStagePresenter.CasterColor);
        GraphShowcaseStagePresenter.DrawThickOutlineCircle(
            debugDraw,
            _aimX / 100f,
            _aimY / 100f,
            SnapDotRadius,
            GraphShowcaseStagePresenter.OutlineDark,
            GraphShowcaseStagePresenter.GateColor);
    }

    private void DrawSendEventSignal(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        if (target < 0)
        {
            return;
        }

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            casterActor.X,
            casterActor.Y,
            targetActor.X,
            targetActor.Y + 0.6f,
            0.08f,
            GraphShowcaseStagePresenter.CasterColor);
        if (!HasLiveMark(ctx, ctx.SimActors[target]))
        {
            return;
        }

        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw,
            targetActor.X,
            targetActor.Y + BadgeLift,
            GraphShowcaseStagePresenter.BadgeKind.Bell,
            GraphShowcaseStagePresenter.SentryAlert);
    }

    private static void DrawCommandChain(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int target)
    {
        if (target < 0)
        {
            return;
        }

        Entity current = ctx.SimActors[target];
        while (ctx.Ownership!.TryGetDirectOwner(current, out Entity owner) && owner != Entity.Null)
        {
            int ownerIndex = GraphOpsNodeActorBinding.IndexOf(ctx, owner);
            int ownedIndex = GraphOpsNodeActorBinding.IndexOf(ctx, current);
            if (ownerIndex < 0 || ownedIndex < 0)
            {
                break;
            }

            GraphOpsNodeActor ownerActor = ctx.Vignette.Actors[ownerIndex];
            GraphOpsNodeActor ownedActor = ctx.Vignette.Actors[ownedIndex];
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                ownerActor.X,
                ownerActor.Y,
                ownedActor.X,
                ownedActor.Y,
                0.12f,
                CommandWhite);
            current = owner;
        }

        int captainIndex = GraphOpsNodeActorBinding.IndexOf(ctx, current);
        if (captainIndex < 0 || current != ctx.Caster)
        {
            return;
        }

        GraphOpsNodeActor captain = ctx.Vignette.Actors[captainIndex];
        GraphOpsNodeActor soldier = ctx.Vignette.Actors[target];
        GraphShowcaseStagePresenter.DrawDashedDirectedLine(
            debugDraw,
            soldier.X,
            soldier.Y + 0.7f,
            captain.X,
            captain.Y + 0.7f,
            0.06f,
            GraphShowcaseStagePresenter.CasterColor);
        DrawChip(debugDraw, captain.X + 0.4f, captain.Y + 0.7f, GraphShowcaseStagePresenter.CasterColor);
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw,
            captain.X,
            captain.Y + BadgeLift,
            GraphShowcaseStagePresenter.BadgeKind.Flag,
            CommandWhite);
    }

    private static void DrawPresetCardFan(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        float cardX = casterActor.X + 0.9f;
        float cardY = casterActor.Y - 0.9f;
        DrawCard(debugDraw, cardX, cardY, GraphShowcaseStagePresenter.CasterColor, filled: true);
        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            int index = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[i]);
            if (index < 0)
            {
                continue;
            }

            GraphOpsNodeActor hit = ctx.Vignette.Actors[index];
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                cardX,
                cardY,
                hit.X,
                hit.Y,
                0.08f,
                GraphShowcaseStagePresenter.CasterColor);
            float healthMax = hit.HealthMax > 0f ? hit.HealthMax : hit.Health;
            float damage = healthMax - ctx.ActorHealth[index];
            if (damage > 0.5f)
            {
                GraphShowcaseStagePresenter.DrawNumber(
                    debugDraw,
                    hit.X + 0.55f,
                    hit.Y + 1.0f,
                    -(int)MathF.Round(damage),
                    0.42f,
                    GraphShowcaseStagePresenter.EnemyColor);
            }
        }
    }

    private void DrawCardSlotFan(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        float slotX = casterActor.X + 0.9f;
        float slotY = casterActor.Y - 0.9f;
        bool chipArriving = ctx.Wave % 2 == 0;
        if (chipArriving)
        {
            GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, slotX, slotY, 1.2f, 0.7f, 1, CommandWhite);
            float edgeX = casterActor.X - 3.4f;
            float edgeY = casterActor.Y + 2.8f;
            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw,
                edgeX,
                edgeY,
                slotX,
                slotY,
                0.06f,
                LetterSeal);
            DrawChip(debugDraw, edgeX + 0.5f, edgeY - 0.4f, LetterSeal);
            return;
        }

        DrawCard(debugDraw, slotX, slotY, GraphShowcaseStagePresenter.CasterColor, filled: true);
        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            Entity hit = ctx.HitTargets[i];
            int index = GraphOpsNodeActorBinding.IndexOf(ctx, hit);
            if (index < 0)
            {
                continue;
            }

            GraphOpsNodeActor hitActor = ctx.Vignette.Actors[index];
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                slotX,
                slotY,
                hitActor.X,
                hitActor.Y,
                0.08f,
                GraphShowcaseStagePresenter.CasterColor);
            if (HasLiveMark(ctx, hit))
            {
                GraphShowcaseStagePresenter.DrawBadge(
                    debugDraw,
                    hitActor.X,
                    hitActor.Y + BadgeLift,
                    GraphShowcaseStagePresenter.BadgeKind.Bell,
                    GraphShowcaseStagePresenter.SentryAlert);
            }
        }
    }

    private static void DrawKnowledgeContrast(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        int viewerIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "viewer");
        if (viewerIndex < 0 || target < 0)
        {
            return;
        }

        GraphOpsNodeActor viewer = ctx.Vignette.Actors[viewerIndex];
        GraphOpsNodeActor stake = ctx.Vignette.Actors[target];
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            viewer.X,
            viewer.Y,
            stake.X,
            stake.Y,
            0.07f,
            CommandWhite);
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw,
            stake.X,
            stake.Y + BadgeLift,
            GraphShowcaseStagePresenter.BadgeKind.Eye,
            GraphShowcaseStagePresenter.GuardColor);
        if (GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster") is int stranger && stranger >= 0)
        {
            GraphOpsNodeActor strangerActor = ctx.Vignette.Actors[stranger];
            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw,
                viewer.X,
                viewer.Y,
                strangerActor.X,
                strangerActor.Y,
                0.06f,
                GraphShowcaseStagePresenter.EnemyColor);
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw,
                strangerActor.X,
                strangerActor.Y + BadgeLift,
                GraphShowcaseStagePresenter.BadgeKind.Cross,
                GraphShowcaseStagePresenter.EnemyColor);
        }
    }

    private void DrawLetterBoard(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target, int slot, bool fractionalChip)
    {
        if (target < 0)
        {
            return;
        }

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            casterActor.X,
            casterActor.Y,
            targetActor.X,
            targetActor.Y + 0.5f,
            0.08f,
            GraphShowcaseStagePresenter.CasterColor);
        float boardX = targetActor.X + 2.6f;
        float boardY = targetActor.Y + 1.2f;
        GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, boardX, boardY, 1.8f, 1.1f, 2, CommandWhite);
        float slotY = boardY + 0.28f - slot * 0.55f;
        GraphShowcaseStagePresenter.DrawDashedDirectedLine(
            debugDraw,
            targetActor.X,
            targetActor.Y + 0.9f,
            boardX,
            slotY,
            0.06f,
            LetterSeal);
        if (fractionalChip)
        {
            GraphShowcaseStagePresenter.DrawNumber(
                debugDraw,
                boardX - 0.2f,
                slotY,
                (int)LastBusEvent.Magnitude,
                0.34f,
                GraphShowcaseStagePresenter.CasterColor);
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new System.Numerics.Vector2(boardX - 0.05f, slotY - 0.12f),
                HalfWidth = 0.04f,
                HalfHeight = 0.04f,
                Thickness = 0.04f,
                Color = GraphShowcaseStagePresenter.CasterColor
            });
            int fraction = (int)MathF.Round((LastBusEvent.Magnitude - MathF.Truncate(LastBusEvent.Magnitude)) * 10f);
            GraphShowcaseStagePresenter.DrawNumber(
                debugDraw,
                boardX + 0.25f,
                slotY,
                fraction,
                0.34f,
                GraphShowcaseStagePresenter.CasterColor);
        }
        else
        {
            GraphShowcaseStagePresenter.DrawNumber(
                debugDraw,
                boardX + 0.3f,
                slotY,
                LastBusEvent.TagId,
                0.34f,
                GraphShowcaseStagePresenter.CasterColor);
        }
    }

    private void DrawPosRulers(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int target)
    {
        if (target < 0)
        {
            return;
        }

        GraphOpsNodeActor marker = ctx.Vignette.Actors[target];
        DrawRulerAxes(debugDraw);
        if (ctx.Vignette.Op == "LoadTargetPosX")
        {
            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw,
                marker.X,
                marker.Y,
                marker.X,
                0f,
                0.07f,
                GraphShowcaseStagePresenter.SentryAlert);
            GraphShowcaseStagePresenter.DrawNumber(
                debugDraw,
                marker.X + 0.4f,
                0.42f,
                _posReadoutX,
                0.44f,
                GraphShowcaseStagePresenter.SentryAlert);
        }
        else
        {
            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw,
                marker.X,
                marker.Y,
                0f,
                marker.Y,
                0.07f,
                GraphShowcaseStagePresenter.SentryAlert);
            GraphShowcaseStagePresenter.DrawNumber(
                debugDraw,
                0.45f,
                marker.Y + 0.42f,
                _posReadoutY,
                0.44f,
                GraphShowcaseStagePresenter.SentryAlert);
        }
    }

    private static void DrawRulerAxes(DebugDrawCommandBuffer debugDraw)
    {
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            0f,
            -0.6f,
            0f,
            3.6f,
            0.1f,
            CommandWhite,
            arrowStart: false,
            arrowEnd: false);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            -0.6f,
            0f,
            4.6f,
            0f,
            0.1f,
            CommandWhite,
            arrowStart: false,
            arrowEnd: false);
        for (int meter = 0; meter <= 4; meter++)
        {
            debugDraw.Lines.Add(new DebugDrawLine2D
            {
                A = new System.Numerics.Vector2(-0.18f, meter),
                B = new System.Numerics.Vector2(0.18f, meter),
                Thickness = 0.05f,
                Color = CommandWhite
            });
            GraphShowcaseStagePresenter.DrawNumber(
                debugDraw,
                -0.45f,
                meter + 0.22f,
                meter * 100,
                0.22f,
                GraphShowcaseStagePresenter.GhostColor);
            debugDraw.Lines.Add(new DebugDrawLine2D
            {
                A = new System.Numerics.Vector2(meter, -0.18f),
                B = new System.Numerics.Vector2(meter, 0.18f),
                Thickness = 0.05f,
                Color = CommandWhite
            });
            GraphShowcaseStagePresenter.DrawNumber(
                debugDraw,
                meter + 0.25f,
                -0.45f,
                meter * 100,
                0.22f,
                GraphShowcaseStagePresenter.GhostColor);
        }
    }

    private void DrawCircleVerdict(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        if (target < 0)
        {
            return;
        }

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor inside = ctx.Vignette.Actors[target];
        bool insideBeat = ctx.Wave % 2 == 0;
        GraphShowcaseStagePresenter.DrawThickOutlineCircle(
            debugDraw,
            inside.X,
            inside.Y,
            SnapDotRadius,
            GraphShowcaseStagePresenter.OutlineDark,
            insideBeat ? GraphShowcaseStagePresenter.GuardColor : GraphShowcaseStagePresenter.GhostColor);
        float outX = OutPointXCm / 100f;
        GraphShowcaseStagePresenter.DrawThickOutlineCircle(
            debugDraw,
            outX,
            casterActor.Y,
            SnapDotRadius,
            GraphShowcaseStagePresenter.OutlineDark,
            insideBeat ? GraphShowcaseStagePresenter.GhostColor : GraphShowcaseStagePresenter.EnemyColor);
        if (insideBeat && _pointInside)
        {
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw,
                inside.X,
                inside.Y + 0.7f,
                GraphShowcaseStagePresenter.BadgeKind.Check,
                GraphShowcaseStagePresenter.GuardColor);
        }

        if (!insideBeat && _pointOutsideSeen)
        {
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw,
                outX,
                casterActor.Y + 0.7f,
                GraphShowcaseStagePresenter.BadgeKind.Cross,
                GraphShowcaseStagePresenter.EnemyColor);
        }
    }

    private static void DrawControlsContrast(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        if (target < 0)
        {
            return;
        }

        GraphOpsNodeActor captain = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor member = ctx.Vignette.Actors[target];
        bool allyBeat = ctx.Wave % 2 == 0;
        if (allyBeat)
        {
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw,
                captain.X,
                captain.Y,
                member.X,
                member.Y,
                0.1f,
                GraphShowcaseStagePresenter.GuardColor);
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw,
                member.X,
                member.Y + BadgeLift,
                GraphShowcaseStagePresenter.BadgeKind.Diamond,
                GraphShowcaseStagePresenter.GuardColor);
        }
        else
        {
            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw,
                member.X,
                member.Y,
                captain.X,
                captain.Y,
                0.08f,
                GraphShowcaseStagePresenter.EnemyColor);
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw,
                captain.X,
                captain.Y + BadgeLift,
                GraphShowcaseStagePresenter.BadgeKind.Cross,
                GraphShowcaseStagePresenter.EnemyColor);
        }
    }

    private static void DrawCard(DebugDrawCommandBuffer debugDraw, float x, float y, DebugDrawColor color, bool filled)
    {
        debugDraw.Boxes.Add(new DebugDrawBox2D
        {
            Center = new System.Numerics.Vector2(x, y),
            HalfWidth = 0.5f,
            HalfHeight = 0.32f,
            Thickness = filled ? 0.09f : 0.05f,
            Color = color
        });
    }

    private static void DrawChip(DebugDrawCommandBuffer debugDraw, float x, float y, DebugDrawColor color)
    {
        debugDraw.Boxes.Add(new DebugDrawBox2D
        {
            Center = new System.Numerics.Vector2(x, y),
            HalfWidth = ChipSize,
            HalfHeight = ChipSize * 0.7f,
            Thickness = 0.07f,
            Color = color
        });
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

    private static void SeedOwnershipAndKnowledge(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            Entity entity = ctx.SimActors[i];
            if (entity == ctx.Caster || entity == ctx.Viewer)
            {
                continue;
            }

            if (!ctx.Ownership!.TryGetDirectOwner(entity, out _))
            {
                ctx.Ownership.EnsureOwnership(ctx.Caster, entity);
            }

            ctx.Knowledge!.Upsert(ctx.Viewer, entity, CreateDisclosure(ctx.Viewer));
        }

        if (ctx.Target != Entity.Null && ctx.Target != ctx.Viewer)
        {
            ctx.Knowledge!.Upsert(ctx.Viewer, ctx.Target, CreateDisclosure(ctx.Viewer));
        }
    }

    private static void PrefillFanOut(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Vignette.Op is not ("FanOutDispatchEffect" or "FanOutDispatchEffectDynamic"))
        {
            ctx.PrefillTargetCount = 0;
            return;
        }

        if (!PlacementValidation.TryGetEntityWorldPositionCm(ctx.SimWorld, ctx.Caster, out Fix64Vec2 originCm))
        {
            throw new InvalidOperationException("Fan-out gallery caster has no WorldPositionCm.");
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        var targets = new List<Entity>();
        for (int i = 0; i < actors.Length; i++)
        {
            if (string.Equals(actors[i].Role, "caster", StringComparison.Ordinal) ||
                string.Equals(actors[i].Role, "viewer", StringComparison.Ordinal))
            {
                continue;
            }

            if (!PlacementValidation.TryGetEntityWorldPositionCm(ctx.SimWorld, ctx.SimActors[i], out Fix64Vec2 memberCm))
            {
                continue;
            }

            if (PlacementValidation.IsPointInCircle(in memberCm, in originCm, Fix64.FromFloat(FanOutRadiusCm)))
            {
                targets.Add(ctx.SimActors[i]);
            }
        }

        ctx.PrefillTargets = targets.ToArray();
        ctx.PrefillTargetCount = targets.Count;
    }

    private void RunPayloadProducer(GraphOpsNodeDriverContext ctx)
    {
        ExecuteVoidProgram(ctx, _producerProgram!, ctx.Caster, ctx.Target, ctx.TargetPosCm);
        ctx.EventBus!.Update();
        var events = ctx.EventBus.Events;
        LastBusEventCount = events.Count;
        if (events.Count != 1)
        {
            throw new InvalidOperationException(
                $"Event payload producer for {ctx.Vignette.Op} must leave exactly one bus event, got {events.Count}.");
        }

        LastBusEvent = events[0];
        ctx.EventPayload = new GraphEventPayload
        {
            PayloadA = LastBusEvent.TagId,
            PayloadB = LastBusEvent.TagId,
            FloatA = LastBusEvent.Magnitude
        };
    }

    private void DispatchSendEventListener(GraphOpsNodeDriverContext ctx)
    {
        var events = ctx.EventBus!.Events;
        LastBusEventCount = events.Count;
        if (events.Count != 1)
        {
            throw new InvalidOperationException(
                $"SendEvent gallery must broadcast exactly one event per beat, got {events.Count}.");
        }

        LastBusEvent = events[0];
        ExecuteVoidProgram(ctx, _listenerProgram!, LastBusEvent.Source, LastBusEvent.Target, ctx.TargetPosCm);
    }

    private void RunSecondPasses(GraphOpsNodeDriverContext ctx)
    {
        switch (ctx.Vignette.Op)
        {
            case "IsPointInCircle":
                _pointOutsideSeen = ExecuteBoolProgram(
                    ctx,
                    ctx.Compiled.Program,
                    ctx.Caster,
                    ctx.Target,
                    new IntVector2(OutPointXCm, 0));
                if (_pointOutsideSeen)
                {
                    throw new InvalidOperationException("IsPointInCircle gallery outside point must land outside the ring.");
                }

                ctx.CaptionValues["resultOut"] = "在圈外";
                break;
            case "ControlDomainControls":
                bool reversed = ExecuteBoolProgram(
                    ctx,
                    ctx.Compiled.Program,
                    ctx.Target,
                    ctx.Caster,
                    ctx.TargetPosCm);
                if (reversed)
                {
                    throw new InvalidOperationException("ControlDomainControls gallery reverse order must fail close.");
                }

                ctx.CaptionValues["resultFoe"] = "管不动";
                break;
        }
    }

    private void RunStrangerPass(GraphOpsNodeDriverContext ctx)
    {
        int strangerIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        if (strangerIndex < 0)
        {
            return;
        }

        Entity stranger = ctx.SimActors[strangerIndex];
        ctx.Knowledge!.Remove(_viewer, stranger);
        bool seen = ExecuteBoolProgram(ctx, ctx.Compiled.Program, ctx.Caster, stranger, ctx.TargetPosCm);
        if (seen)
        {
            throw new InvalidOperationException(
                "KnowledgeHasProjection gallery stranger must stay invisible without a disclosure.");
        }
    }

    private void ExecuteVoidProgram(
        GraphOpsNodeDriverContext ctx,
        GraphInstruction[] program,
        Entity caster,
        Entity explicitTarget,
        IntVector2 targetPosCm)
    {
        var targetList = new GraphTargetList(_targets);
        var state = NewState(ctx, program, caster, explicitTarget, targetPosCm, targetList);
        GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
        if (state.Status != GraphExecutionStatus.Halted)
        {
            throw new InvalidOperationException($"Aux graph for {ctx.Vignette.Op} ended with status {state.Status}.");
        }
    }

    private bool ExecuteBoolProgram(
        GraphOpsNodeDriverContext ctx,
        GraphInstruction[] program,
        Entity caster,
        Entity explicitTarget,
        IntVector2 targetPosCm)
    {
        var targetList = new GraphTargetList(_targets);
        var state = NewState(ctx, program, caster, explicitTarget, targetPosCm, targetList);
        GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
        if (state.Status != GraphExecutionStatus.Halted)
        {
            throw new InvalidOperationException($"Second-pass graph for {ctx.Vignette.Op} ended with status {state.Status}.");
        }

        return _bools[ctx.FeaturedDest] != 0;
    }

    private GraphExecutionState NewState(
        GraphOpsNodeDriverContext ctx,
        GraphInstruction[] program,
        Entity caster,
        Entity explicitTarget,
        IntVector2 targetPosCm,
        GraphTargetList targetList)
    {
        return new GraphExecutionState
        {
            World = ctx.SimWorld,
            Caster = caster,
            ExplicitTarget = explicitTarget,
            TargetContext = explicitTarget,
            Viewer = _viewer,
            EventPayload = ctx.EventPayload,
            TargetPosCm = targetPosCm,
            Api = ctx.Api,
            Programs = ctx.Programs,
            F = _floats,
            I = _ints,
            B = _bools,
            E = _entities,
            Targets = _targets,
            TargetList = targetList,
            CallStack = _callStack,
            RandomSeed = (uint)(0xA5A5A5A5u ^ (uint)ctx.Wave),
            Status = GraphExecutionStatus.Running
        };
    }

    private static GraphInstruction[] CompileAuxGraph(GraphOpsNodeDriverContext ctx, string file)
    {
        string path = Path.Combine(ctx.AssetsRoot, "GAS", "graphs", file);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Event gallery '{ctx.Vignette.Op}' requires aux graph '{file}'.", path);
        }

        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        JsonObject obj = GraphOpsNodeGraphCompiler.ParseSingleGraphShard(path);
        string graphId = "showcase.graph_op." + Path.GetFileNameWithoutExtension(file);
        GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, graphId, options);
        if (!compiled.Succeeded)
        {
            string message = string.Join("; ", compiled.Diagnostics.Select(d => d.Message));
            throw new InvalidOperationException($"FrontDoor compile failed for '{graphId}': {message}");
        }

        if (!compiled.Package.HasValue)
        {
            throw new InvalidOperationException($"FrontDoor compile for '{graphId}' produced no package.");
        }

        GraphProgramPackage package = compiled.Package.Value;
        var resolver = GraphOpsNodeGallerySymbolResolver.CreateStandalone(ctx.AssetsRoot);
        var builtinHandlers = new Ludots.Core.Gameplay.GAS.BuiltinHandlerRegistry();
        Ludots.Core.Gameplay.GAS.BuiltinHandlers.RegisterAll(builtinHandlers);
        GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, resolver, ctx.Collections, builtinHandlers);
        GraphKindOperationPolicy.RequireAllowed(GraphKind.Effect, compiled.Program, GasGraphOpHandlerTable.Instance);
        return compiled.Program;
    }

    private void ApplyBeat(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result, float healthBefore)
    {
        string op = ctx.Vignette.Op;
        int targetIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        ctx.CaptionValues.Clear();
        bool featuredBool = ReadFeaturedBool(ctx);

        switch (op)
        {
            case "FanOutDispatchEffect":
                RequireDispatchTargets(ctx, result.TargetCount);
                RequireDispatched(ctx);
                ctx.CaptionValues["count"] = result.TargetCount.ToString();
                ctx.CaptionValues["damage"] = ((int)StrikeDamage).ToString();
                _overlayArmed = true;
                break;
            case "FanOutDispatchEffectDynamic":
                RequireDispatchTargets(ctx, result.TargetCount);
                RequireDispatched(ctx);
                ctx.CaptionValues["count"] = result.TargetCount.ToString();
                _overlayArmed = true;
                break;
            case "SendEvent":
                ctx.CaptionValues["result"] = "广播";
                break;
            case "LoadTargetPosX":
                MoveMarker(ctx, _aimX / 100f, ctx.Vignette.Actors[Math.Max(targetIndex, 0)].Y);
                _posReadoutX = result.IntValue;
                ctx.CaptionValues["result"] = result.IntValue.ToString();
                break;
            case "LoadTargetPosY":
                MoveMarker(ctx, ctx.Vignette.Actors[Math.Max(targetIndex, 0)].X, _aimY / 100f);
                _posReadoutY = result.IntValue;
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
                _pointInside = featuredBool;
                if (!_pointInside)
                {
                    throw new InvalidOperationException("IsPointInCircle gallery in-circle point must land inside.");
                }

                ctx.CaptionValues["resultIn"] = "在圈里";
                _overlayArmed = true;
                break;
            case "SnapToNearestInCollection":
                RequireSnapSucceeded(ctx, featuredBool, "吸到");
                RequireSnapEntity(ctx, result.EntityValue);
                MoveMarker(ctx, _aimX / 100f, _aimY / 100f);
                ctx.CaptionValues["result"] = ctx.Vignette.Actors[GraphOpsNodeActorBinding.IndexOf(ctx, result.EntityValue)].Name;
                _overlayArmed = true;
                break;
            case "SnapToNearestGraphEdge":
                RequireSnapSucceeded(ctx, featuredBool, "路边");
                MoveMarker(ctx, _aimX / 100f, _aimY / 100f);
                ctx.CaptionValues["result"] = "路边";
                ctx.CaptionValues["x"] = _aimX.ToString();
                ctx.CaptionValues["y"] = _aimY.ToString();
                _overlayArmed = true;
                break;
            case "LoadViewer":
                if (result.EntityValue != ctx.Viewer)
                {
                    throw new InvalidOperationException("LoadViewer gallery did not read the viewer entity.");
                }

                ctx.CaptionValues["result"] = ctx.Vignette.Actors[GraphOpsNodeActorBinding.IndexOf(ctx, result.EntityValue)].Name;
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

                ctx.CaptionValues["result"] = ctx.Vignette.Actors[GraphOpsNodeActorBinding.IndexOf(ctx, result.EntityValue)].Name;
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

                ctx.CaptionValues["result"] = "木桩看得见";
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

    private static GraphEventPayload BuildPayload(string op)
    {
        if (op != "FanOutDispatchEffectDynamic")
        {
            return default;
        }

        int templateId = EffectTemplateIdRegistry.GetId(MarkEffect);
        if (templateId <= 0)
        {
            throw new InvalidOperationException($"Event gallery requires '{MarkEffect}' loaded through EffectTemplateLoader.");
        }

        return new GraphEventPayload { PayloadA = templateId, PayloadB = templateId };
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
        return op is "FanOutDispatchEffect" or "FanOutDispatchEffectDynamic" ? FanOutRadiusCm / 100f : RangeCm / 100f;
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

    private static void RequireSnapEntity(GraphOpsNodeDriverContext ctx, Entity snapped)
    {
        if (snapped == Entity.Null || GraphOpsNodeActorBinding.IndexOf(ctx, snapped) < 0)
        {
            throw new InvalidOperationException($"{ctx.Vignette.Op} gallery snap did not land on a roster member.");
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

    private bool HasLiveMark(GraphOpsNodeDriverContext ctx, Entity target)
    {
        if (target == Entity.Null ||
            !ctx.SimWorld.IsAlive(target) ||
            !ctx.SimWorld.Has<ActiveEffectContainer>(target))
        {
            return false;
        }

        ActiveEffectContainer container = ctx.SimWorld.Get<ActiveEffectContainer>(target);
        for (int i = 0; i < container.Count; i++)
        {
            Entity effect = container.GetEntity(i);
            if (ctx.SimWorld.IsAlive(effect) &&
                ctx.SimWorld.Has<GameplayEffect>(effect) &&
                !ctx.SimWorld.Get<GameplayEffect>(effect).CancelRequested &&
                ctx.SimWorld.Has<EffectTemplateRef>(effect) &&
                ctx.SimWorld.Get<EffectTemplateRef>(effect).TemplateId == _markTemplateId)
            {
                return true;
            }
        }

        return false;
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
