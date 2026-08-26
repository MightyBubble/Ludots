using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

/// <summary>
/// Hosts the TriggerGraph-only #1116/#1115 vignettes: StoreArg* staging → InvokeGraph
/// subgraph round-trips (int return, float echo through a map variable, entity moved by
/// the callee), the InvokeGraph entry-label call, and DispatchMapEvent firing a schema
/// MapHeartbeat through a locally bound TriggerManager. The shared callee is a real
/// TriggerGraph with one entry per demo; the caller is registered into a local
/// GraphProgramRegistry so load-time validation (kind, entry label) runs for real.
/// </summary>
public sealed class InvokeGraphNodeDriver : IGraphOpsNodeDriver
{
    public const string CalleeFile = "_invokeArgCallee.json";
    public const string CalleeGraphKey = "showcase.graph_op._invokeArgCallee";
    private const int SliceBudget = 96;
    private const string StageArgKey = "GraphOps.Stage";
    private const string RatioArgKey = "GraphOps.Ratio";
    private const string WhoArgKey = "GraphOps.Who";
    private const string EchoVarName = "gallery.echo";

    private readonly float[] _floats = new float[GraphVmLimits.MaxFloatRegisters];
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly Entity[] _entities = new Entity[GraphVmLimits.MaxEntityRegisters];
    private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private readonly GraphEntryPayloadTable _invokeArgs = new();
    private readonly GraphProgramRegistry _programs = new();
    private readonly EventSchemaRegistry _schemas = new();
    private readonly TriggerManager _triggerManager = new();
    private readonly HeartbeatProbeTrigger _heartbeatProbe = new() { EventKey = new EventKey("MapHeartbeat") };
    private GraphExecutionCursor _cursor;
    private bool _halted;
    private string _assetsRoot = "";

    public GraphOpsNodeExecuteResult LastResult { get; private set; }

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        _ = GraphOpsNodeActorBinding.RequireRole(ctx, "caster");
        _ = GraphOpsNodeActorBinding.RequireRole(ctx, "target");
        _assetsRoot = ctx.AssetsRoot;

        _triggerManager.EventSchemas = _schemas;
        _triggerManager.RegisterMapTriggers(CasterMapId(ctx), new Trigger[] { _heartbeatProbe });
        ctx.Api.BindTriggerManager(_triggerManager);

        RegisterCallee();
        RegisterCaller(ctx);
        _cursor = new GraphExecutionCursor(CallerEntryStartPc(ctx));
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
                _programs,
                _floats,
                _ints,
                _bools,
                _entities,
                _targets,
                _callStack,
                ref _cursor,
                SliceBudget,
                GraphKind.Script,
                invokeArgs: _invokeArgs);
            if (!result.Halted)
            {
                throw new InvalidOperationException(
                    $"Invoke gallery '{ctx.Vignette.Op}' ended with status {result.Status}; the staged call must halt in one slice.");
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

        if (ctx.Vignette.Op != nameof(GraphNodeOp.StoreArgEntity))
        {
            return;
        }

        if (Ludots.Core.Gameplay.Placement.PlacementValidation.TryGetEntityWorldPositionCm(
                ctx.SimWorld, ctx.Target, out Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 moved))
        {
            debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = new System.Numerics.Vector2(moved.X.ToFloat(), moved.Y.ToFloat()),
                Radius = 80f,
                Thickness = 2f,
                Color = DebugDrawColor.Yellow,
            });
        }
    }

    private void RegisterCallee()
    {
        int calleeId = GraphIdRegistry.GetId(CalleeGraphKey);
        if (calleeId <= 0)
        {
            calleeId = GraphIdRegistry.Register(CalleeGraphKey);
        }

        if (_programs.TryGetRegistration(calleeId, out _))
        {
            return;
        }

        string path = Path.Combine(_assetsRoot, "GAS", "graphs", CalleeFile);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"InvokeGraph gallery requires callee graph {CalleeFile}.", path);
        }

        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        JsonObject obj = GraphOpsNodeGraphCompiler.ParseSingleGraphShard(path);
        GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(
            obj,
            CalleeGraphKey,
            options,
            _schemas);
        if (!compiled.Succeeded)
        {
            string message = string.Join("; ", compiled.Diagnostics.Select(d => d.Message));
            throw new InvalidOperationException($"FrontDoor compile failed for InvokeGraph callee: {message}");
        }

        GraphProgramPackage package = compiled.Package!.Value;
        GraphProgramSymbolPatcher.Patch(
            package.Symbols,
            package.Program,
            GraphOpsNodeGallerySymbolResolver.CreateStandalone(_assetsRoot));
        _programs.Register(
            calleeId,
            package.Program,
            GraphKind.TriggerGraph,
            compiled.SourceMap,
            package.Symbols,
            package.TriggerGraphEntries);
    }

    private void RegisterCaller(GraphOpsNodeDriverContext ctx)
    {
        GraphProgramPackage caller = ctx.Compiled.Package!.Value;
        for (int i = 0; i < caller.Program.Length; i++)
        {
            if (caller.Program[i].Op != (ushort)GraphNodeOp.InvokeGraph)
            {
                continue;
            }

            if ((caller.Program[i].Flags & GraphInstructionFlags.FuncLibName) != 0)
            {
                string graphKey = caller.Symbols[caller.Program[i].Imm];
                int calleeId = GraphIdRegistry.GetId(graphKey);
                if (calleeId <= 0)
                {
                    throw new InvalidOperationException(
                        $"InvokeGraph gallery callee '{graphKey}' is not registered in the GraphIdRegistry.");
                }

                caller.Program[i].Imm = calleeId;
                caller.Program[i].Flags = (byte)(caller.Program[i].Flags & ~GraphInstructionFlags.FuncLibName);
            }
            else
            {
                caller.Program[i].Imm = GraphIdRegistry.GetId(CalleeGraphKey);
            }
        }

        int callerId = GraphIdRegistry.GetId(GraphOpsNodeIds.GraphId(ctx.Vignette.Op));
        if (callerId <= 0)
        {
            callerId = GraphIdRegistry.Register(GraphOpsNodeIds.GraphId(ctx.Vignette.Op));
        }

        _programs.Register(
            callerId,
            caller.Program,
            GraphKind.TriggerGraph,
            ctx.Compiled.SourceMap,
            caller.Symbols,
            caller.TriggerGraphEntries);
    }

    private static int CallerEntryStartPc(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Compiled.Package!.Value.TriggerGraphEntries is not { Length: 1 } entries)
        {
            throw new InvalidOperationException(
                $"Invoke gallery '{ctx.Vignette.Op}' must compile to exactly one TriggerGraph entry.");
        }

        return entries[0].StartPc;
    }

    private static MapId CasterMapId(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.SimWorld.TryGet<MapEntity>(ctx.Caster, out MapEntity mapEntity))
        {
            return mapEntity.MapId;
        }

        throw new InvalidOperationException(
            $"Invoke gallery '{ctx.Vignette.Op}' caster must anchor the gallery map for map-domain dispatch.");
    }

    private void ApplyBeat(GraphOpsNodeDriverContext ctx)
    {
        var values = ctx.CaptionValues;
        switch (ctx.Vignette.Op)
        {
            case nameof(GraphNodeOp.InvokeGraph):
                values["entry"] = "boost";
                values["result"] = LastResult.ReturnInt.ToString(CultureInfo.InvariantCulture);
                break;
            case nameof(GraphNodeOp.StoreArgInt):
                values["result"] = LastResult.ReturnInt.ToString(CultureInfo.InvariantCulture);
                break;
            case nameof(GraphNodeOp.StoreArgFloat):
                values["echo"] = ctx.Api.ReadMapVarFloat(
                    Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(EchoVarName),
                    CasterMapId(ctx)).ToString("0.0##", CultureInfo.InvariantCulture);
                break;
            case nameof(GraphNodeOp.StoreArgEntity):
                if (!Ludots.Core.Gameplay.Placement.PlacementValidation.TryGetEntityWorldPositionCm(
                        ctx.SimWorld, ctx.Target, out Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 moved))
                {
                    throw new InvalidOperationException(
                        $"Invoke gallery '{ctx.Vignette.Op}' target lost its position after the callee move.");
                }

                values["x"] = ((int)moved.X).ToString(CultureInfo.InvariantCulture);
                values["y"] = ((int)moved.Y).ToString(CultureInfo.InvariantCulture);
                break;
            case nameof(GraphNodeOp.DispatchMapEvent):
                values["fires"] = _heartbeatProbe.FireCount.ToString(CultureInfo.InvariantCulture);
                values["beat"] = _heartbeatProbe.LastHeartbeatIndex.ToString(CultureInfo.InvariantCulture);
                break;
            default:
                throw new InvalidOperationException($"Invoke driver does not host op '{ctx.Vignette.Op}'.");
        }

        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, values);
    }

    private sealed class HeartbeatProbeTrigger : Trigger
    {
        public int FireCount { get; private set; }
        public int LastHeartbeatIndex { get; private set; }

        public override Task ExecuteAsync(ScriptContext context)
        {
            FireCount++;
            LastHeartbeatIndex = context.Get<int>(MapTriggerEventPayloadKeys.HeartbeatIndex);
            return Task.CompletedTask;
        }
    }
}
