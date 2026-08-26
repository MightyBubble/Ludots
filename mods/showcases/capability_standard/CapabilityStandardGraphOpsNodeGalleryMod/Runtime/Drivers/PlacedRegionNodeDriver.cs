using System;
using System.Globalization;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

/// <summary>
/// Hosts the TriggerGraph-only LoadPlacedRegion vignette (#1108). Executes the roll-call
/// entry directly (gallery does not mount the graph — mounting would fail-closed on the
/// intentional ghost region miss). Asserts authored yard=1 and ghost=0 via map variables.
/// </summary>
public sealed class PlacedRegionNodeDriver : IGraphOpsNodeDriver
{
    private const int SliceBudget = 64;

    private readonly float[] _floats = new float[GraphVmLimits.MaxFloatRegisters];
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly Entity[] _entities = new Entity[GraphVmLimits.MaxEntityRegisters];
    private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private string _yardVarName = string.Empty;
    private string _ghostVarName = string.Empty;

    public GraphOpsNodeExecuteResult LastResult { get; private set; }

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        _ = GraphOpsNodeActorBinding.RequireRole(ctx, "caster");
        (_yardVarName, _ghostVarName) = RequireRegionVarNames(ctx);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        RunEntry(ctx);
        if (LastResult.IntValue != 1)
        {
            throw new InvalidOperationException(
                $"Placed region gallery '{ctx.Vignette.Op}' wave {ctx.Wave} must report yard presence as 1 (featured LoadPlacedRegion).");
        }

        Ludots.Core.Map.MapId mapId = RequireMapId(ctx);
        int yard = ctx.Api.ReadMapVarInt(
            Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(_yardVarName),
            mapId);
        int ghost = ctx.Api.ReadMapVarInt(
            Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(_ghostVarName),
            mapId);
        if (yard != 1 || ghost != 0)
        {
            throw new InvalidOperationException(
                $"Placed region gallery '{ctx.Vignette.Op}' expected yard=1 ghost=0, got yard={yard} ghost={ghost}.");
        }

        var values = ctx.CaptionValues;
        values["yard"] = yard.ToString(CultureInfo.InvariantCulture);
        values["ghost"] = ghost.ToString(CultureInfo.InvariantCulture);
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, values);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
    }

    private void RunEntry(GraphOpsNodeDriverContext ctx)
    {
        if (!ctx.Compiled.Package.HasValue ||
            ctx.Compiled.Package.Value.TriggerGraphEntries is not { Length: 1 } entries)
        {
            throw new InvalidOperationException(
                $"Placed region gallery '{ctx.Vignette.Op}' must compile to exactly one TriggerGraph entry.");
        }

        var cursor = new GraphExecutionCursor(entries[0].StartPc);
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
            ref cursor,
            SliceBudget,
            GraphKind.Script,
            mapScope: RequireMapId(ctx));
        if (!result.Halted)
        {
            throw new InvalidOperationException(
                $"Placed region gallery '{ctx.Vignette.Op}' ended with status {result.Status}; the roll call must halt in one slice.");
        }

        LastResult = new GraphOpsNodeExecuteResult(
            _floats[ctx.FeaturedDest],
            _ints[ctx.FeaturedDest],
            _bools[ctx.FeaturedDest] != 0,
            _entities[ctx.FeaturedDest],
            cursor.ReturnInt,
            0);
    }

    private static Ludots.Core.Map.MapId RequireMapId(GraphOpsNodeDriverContext ctx)
    {
        if (!ctx.SimWorld.TryGet<Ludots.Core.Components.MapEntity>(ctx.Caster, out Ludots.Core.Components.MapEntity mapEntity))
        {
            throw new InvalidOperationException(
                $"Placed region gallery '{ctx.Vignette.Op}' caster must anchor the gallery map.");
        }

        return mapEntity.MapId;
    }

    private static (string Yard, string Ghost) RequireRegionVarNames(GraphOpsNodeDriverContext ctx)
    {
        string? yard = null;
        string? ghost = null;
        for (int i = 0; i < ctx.Vignette.Variables.Length; i++)
        {
            GraphOpsNodeVignetteVariable variable = ctx.Vignette.Variables[i];
            if (!string.Equals(variable.Type, "int", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (yard == null)
            {
                yard = variable.Name;
            }
            else if (ghost == null)
            {
                ghost = variable.Name;
            }
        }

        if (yard == null || ghost == null)
        {
            throw new InvalidOperationException(
                $"Placed region gallery '{ctx.Vignette.Op}' requires two int map variables (yard then ghost).");
        }

        return (yard, ghost);
    }
}
