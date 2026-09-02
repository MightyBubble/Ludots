using System;
using System.Globalization;
using Arch.Core;
using Ludots.Core.Gameplay.Placement;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

/// <summary>
/// Hosts the TriggerGraph-only LoadEntryPayload* vignettes. The map trigger host fires
/// entries from real map events; the gallery stages the same firing context a real event
/// would carry, captures it through the production EventSchemaRegistry (identical
/// schema-driven capture loop), then runs the entry body from StartPc via
/// GraphExecutor.ExecuteScriptSlice with the capture table attached.
/// </summary>
public sealed class EntryPayloadNodeDriver : IGraphOpsNodeDriver
{
    private const int SliceBudget = 64;
    private const int AliveCount = 3;
    private const int AliveDelta = -1;
    private const int SourceTeamId = 2;
    private const float PointerScreenX = 360.5f;
    private const float PointerScreenY = 200f;
    private const string StagedInputAction = "GraphOps.Probe";
    private const int StagedModifiers = InputActionFiredModifiers.Queue;
    private const float SourceMarkRadiusCm = 90f;

    private static readonly DebugDrawColor SourceMark = DebugDrawColor.Cyan;
    private static readonly DebugDrawColor CaptureLink = DebugDrawColor.Yellow;

    private readonly float[] _floats = new float[GraphVmLimits.MaxFloatRegisters];
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly Entity[] _entities = new Entity[GraphVmLimits.MaxEntityRegisters];
    private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private readonly GraphEntryPayloadTable _entryPayload = new();
    private readonly EventSchemaRegistry _schemas = new();
    private GraphExecutionCursor _cursor;
    private bool _halted;

    public GraphOpsNodeExecuteResult LastResult { get; private set; }

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        _ = GraphOpsNodeActorBinding.RequireRole(ctx, "caster");
        _ = GraphOpsNodeActorBinding.RequireRole(ctx, "target");

        string eventName = RequireSingleEntry(ctx).EventName;
        ScriptContext firing = StageFiringContext(ctx, eventName);
        Capture(ctx, firing, eventName);
        _cursor = new GraphExecutionCursor(RequireSingleEntry(ctx).StartPc);
        _halted = false;
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (!_halted)
        {
            GraphSliceResult result = GraphExecutor.ExecuteScriptSlice(
                ctx.SimWorld,
                ctx.Caster,
                ctx.Target,
                default,
                ctx.Compiled.Program,
                ctx.Api,
                ctx.Programs,
                _floats,
                _ints,
                _bools,
                _entities,
                _targets,
                _callStack,
                ref _cursor,
                SliceBudget,
                GraphKind.TriggerGraph,
                entryPayload: _entryPayload);
            if (!result.Halted)
            {
                throw new InvalidOperationException(
                    $"Entry payload gallery '{ctx.Vignette.Op}' ended with status {result.Status}; the captured read must halt in one slice.");
            }

            _halted = true;
            LastResult = new GraphOpsNodeExecuteResult(
                _floats[ctx.FeaturedDest],
                _ints[ctx.FeaturedDest],
                _bools[ctx.FeaturedDest] != 0,
                _entities[ctx.FeaturedDest],
                _cursor.ReturnInt,
                0);
        }

        ApplyBeat(ctx);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        if (!_halted)
        {
            return;
        }

        Entity source = FeaturedSource(ctx);
        if (source == Entity.Null ||
            !PlacementValidation.TryGetEntityWorldPositionCm(ctx.SimWorld, source, out Fix64Vec2 sourceCm) ||
            !PlacementValidation.TryGetEntityWorldPositionCm(ctx.SimWorld, ctx.Caster, out Fix64Vec2 casterCm))
        {
            return;
        }

        debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = new System.Numerics.Vector2(sourceCm.X.ToFloat(), sourceCm.Y.ToFloat()),
            Radius = SourceMarkRadiusCm,
            Thickness = 2f,
            Color = SourceMark,
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new System.Numerics.Vector2(sourceCm.X.ToFloat(), sourceCm.Y.ToFloat()),
            B = new System.Numerics.Vector2(casterCm.X.ToFloat(), casterCm.Y.ToFloat()),
            Thickness = 1.5f,
            Color = CaptureLink,
        });
    }

    private static TriggerGraphEntry RequireSingleEntry(GraphOpsNodeDriverContext ctx)
    {
        if (!ctx.Compiled.Package.HasValue ||
            ctx.Compiled.Package.Value.TriggerGraphEntries is not { Length: 1 } entries)
        {
            throw new InvalidOperationException(
                $"Entry payload gallery '{ctx.Vignette.Op}' must compile to exactly one TriggerGraph entry.");
        }

        return entries[0];
    }

    private static ScriptContext StageFiringContext(GraphOpsNodeDriverContext ctx, string eventName)
    {
        var firing = new ScriptContext();
        switch (eventName)
        {
            case "EntityDied":
                firing.Set(MapTriggerEventPayloadKeys.SourceEntity, ctx.Target);
                firing.Set(MapTriggerEventPayloadKeys.SourceTeamId, SourceTeamId);
                break;
            case "EntityAliveCountChanged":
                firing.Set(MapTriggerEventPayloadKeys.SourceTeamId, SourceTeamId);
                firing.Set(MapTriggerEventPayloadKeys.Count, AliveCount);
                firing.Set(MapTriggerEventPayloadKeys.Delta, AliveDelta);
                break;
            case "InputAction":
                firing.Set(MapTriggerEventPayloadKeys.Rep, ctx.Caster);
                firing.Set(MapTriggerEventPayloadKeys.Action, StagedInputAction);
                firing.Set(MapTriggerEventPayloadKeys.PointerScreenX, PointerScreenX);
                firing.Set(MapTriggerEventPayloadKeys.PointerScreenY, PointerScreenY);
                firing.Set(MapTriggerEventPayloadKeys.Modifiers, StagedModifiers);
                break;
            default:
                throw new InvalidOperationException(
                    $"Entry payload gallery '{ctx.Vignette.Op}' stages event '{eventName}'; wire its schema values first.");
        }

        return firing;
    }

    private void Capture(GraphOpsNodeDriverContext ctx, ScriptContext firing, string eventName)
    {
        _entryPayload.Clear();
        if (!_schemas.TryGet(eventName, out EventSchema schema))
        {
            throw new InvalidOperationException($"Event '{eventName}' has no schema; entry capture is impossible.");
        }

        for (int i = 0; i < schema.Params.Count; i++)
        {
            EventParamSchema param = schema.Params[i];
            if (!firing.Contains(param.PayloadKey))
            {
                continue;
            }

            object raw = firing.Get<object>(param.PayloadKey);
            switch (param.Type)
            {
                case EventParamType.Entity:
                    _entryPayload.SetEntity(param.PayloadKey, (Entity)raw);
                    break;
                case EventParamType.Int:
                    _entryPayload.SetInt(param.PayloadKey, (int)raw);
                    break;
                case EventParamType.Float:
                    _entryPayload.SetFloat(param.PayloadKey, (float)raw);
                    break;
                case EventParamType.String:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Entry payload gallery '{ctx.Vignette.Op}' cannot capture key '{param.PayloadKey}' of type {param.Type}.");
            }
        }
    }

    private Entity FeaturedSource(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Vignette.Op != "LoadEntryPayloadEntity")
        {
            return Entity.Null;
        }

        return _entities[ctx.FeaturedDest];
    }

    private void ApplyBeat(GraphOpsNodeDriverContext ctx)
    {
        var values = ctx.CaptionValues;
        switch (ctx.Vignette.Op)
        {
            case "LoadEntryPayloadEntity":
                values["source"] = ActorNameOf(ctx, _entities[ctx.FeaturedDest]);
                break;
            case "LoadEntryPayloadInt":
                values["count"] = _ints[ctx.FeaturedDest].ToString(CultureInfo.InvariantCulture);
                break;
            case "LoadEntryPayloadFloat":
                values["x"] = _floats[ctx.FeaturedDest].ToString("0.0##", CultureInfo.InvariantCulture);
                break;
            default:
                throw new InvalidOperationException($"Entry payload driver does not host op '{ctx.Vignette.Op}'.");
        }

        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, values);
    }

    private static string ActorNameOf(GraphOpsNodeDriverContext ctx, Entity entity)
    {
        int index = GraphOpsNodeActorBinding.IndexOf(ctx, entity);
        return index >= 0
            ? ctx.Vignette.Actors[index].Name
            : "无名者";
    }
}
