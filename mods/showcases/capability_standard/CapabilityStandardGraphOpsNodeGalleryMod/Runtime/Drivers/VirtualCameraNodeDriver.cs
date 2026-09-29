using System;
using System.Collections.Generic;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

/// <summary>
/// Hosts the ActivateVirtualCamera vignette: the TriggerGraph names a virtual camera and the op
/// leaves the camera request in the engine globals, where the camera runtime picks it up on its
/// next tick. The caption quotes the display name of the camera the request actually names.
/// </summary>
public sealed class VirtualCameraNodeDriver : IGraphOpsNodeDriver
{
    private const int SliceBudget = 32;

    private readonly float[] _floats = new float[GraphVmLimits.MaxFloatRegisters];
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly Entity[] _entities = new Entity[GraphVmLimits.MaxEntityRegisters];
    private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
    private readonly int[] _intIds = new int[GraphVmLimits.MaxIntIds];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private GraphInstruction[] _program = Array.Empty<GraphInstruction>();
    private VirtualCameraRegistry _cameras = null!;
    private Dictionary<string, object> _globals = null!;
    private int _entryPc;
    private string _cameraName = string.Empty;
    private bool _halted;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        _ = GraphOpsNodeActorBinding.RequireRole(ctx, "caster");
        _cameras = ctx.VirtualCameras
            ?? throw new InvalidOperationException(
                $"Virtual camera gallery '{ctx.Vignette.Op}' requires the engine virtual camera registry.");
        _globals = ctx.Globals
            ?? throw new InvalidOperationException(
                $"Virtual camera gallery '{ctx.Vignette.Op}' requires the engine globals.");

        GraphProgramPackage package = ctx.Compiled.Package!.Value;
        if (package.TriggerGraphEntries is not { Length: 1 } entries)
        {
            throw new InvalidOperationException(
                $"Virtual camera gallery '{ctx.Vignette.Op}' must compile to exactly one TriggerGraph entry.");
        }

        _program = package.Program;
        _entryPc = entries[0].StartPc;
        _halted = false;
        _globals.Remove(CoreServiceKeys.VirtualCameraRequest.Name);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (!_halted)
        {
            var frame = GraphFrame.Bind(
                GraphKind.TriggerGraph,
                GraphEntityPreset.None,
                ctx.SimWorld,
                ctx.Caster,
                ctx.Target,
                default,
                ctx.Api,
                programs: null,
                _floats,
                _ints,
                _bools,
                _entities,
                _targets,
                _intIds,
                _callStack);
            frame.Cursor = new GraphExecutionCursor(_entryPc);
            GraphSliceResult result = GraphExecutor.ExecuteSlice(ref frame, _program, SliceBudget);
            if (!result.Halted)
            {
                throw new InvalidOperationException(
                    $"Virtual camera gallery '{ctx.Vignette.Op}' ended with status {result.Status}; the switch must halt in one slice.");
            }

            _halted = true;
            if (!_globals.TryGetValue(CoreServiceKeys.VirtualCameraRequest.Name, out object? raw) ||
                raw is not VirtualCameraRequest request ||
                !_cameras.TryGet(request.Id, out VirtualCameraDefinition definition))
            {
                throw new InvalidOperationException(
                    $"Virtual camera gallery '{ctx.Vignette.Op}' ran the graph but left no request for a registered virtual camera.");
            }

            _cameraName = definition.DisplayName;
        }

        ctx.CaptionValues["camera"] = _cameraName;
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
    }
}
