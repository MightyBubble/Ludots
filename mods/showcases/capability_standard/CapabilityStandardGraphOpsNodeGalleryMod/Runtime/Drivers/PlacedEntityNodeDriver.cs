using System;
using System.Globalization;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

/// <summary>
/// Hosts the TriggerGraph-only LoadPlacedEntity vignette (#1108). The entry re-fires
/// every think wave against the real map's placed-instance index: wave one resolves the
/// placed boss (hit); the driver then destroys it so later waves read Entity.Null through
/// the same graph — the stale catalog handle is exactly what World.IsAlive insurance
/// covers. The graph also mirrors the placed entity's health into a map variable so the
/// read chain (LoadPlacedEntity → LoadAttribute → WriteMapVarFloat) is observable.
/// </summary>
public sealed class PlacedEntityNodeDriver : IGraphOpsNodeDriver
{
    private const int SliceBudget = 64;

    private readonly float[] _floats = new float[GraphVmLimits.MaxFloatRegisters];
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly Entity[] _entities = new Entity[GraphVmLimits.MaxEntityRegisters];
    private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private Entity _boss;
    private string _bossName = string.Empty;
    private string _healthVarName = string.Empty;
    private bool _killed;

    public GraphOpsNodeExecuteResult LastResult { get; private set; }

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        _ = GraphOpsNodeActorBinding.RequireRole(ctx, "caster");
        _boss = GraphOpsNodeActorBinding.RequireRole(ctx, "target");
        _bossName = ctx.Vignette.Actors[GraphOpsNodeActorBinding.IndexOf(ctx, _boss)].Name;
        _healthVarName = RequireHealthVarName(ctx);
        _killed = false;
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        RunEntry(ctx);

        if (!_killed)
        {
            if (LastResult.EntityValue != _boss)
            {
                throw new InvalidOperationException(
                    $"Placed entity gallery '{ctx.Vignette.Op}' wave {ctx.Wave} must resolve the live placed instance '{_bossName}'.");
            }

            ApplyBeat(ctx);
            GraphOpsNodeActorBinding.SyncHud(ctx);
            // KillOneTeamEntity手法：真实销毁放置实体，让下一波读到陈旧目录句柄 → Entity.Null。
            ctx.SimWorld.Destroy(_boss);
            _killed = true;
        }
        else
        {
            if (LastResult.EntityValue != Entity.Null)
            {
                throw new InvalidOperationException(
                    $"Placed entity gallery '{ctx.Vignette.Op}' wave {ctx.Wave} must read Entity.Null after the placed instance died.");
            }

            ApplyBeat(ctx);
        }
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
                $"Placed entity gallery '{ctx.Vignette.Op}' must compile to exactly one TriggerGraph entry.");
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
                $"Placed entity gallery '{ctx.Vignette.Op}' ended with status {result.Status}; the roll call must halt in one slice.");
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
                $"Placed entity gallery '{ctx.Vignette.Op}' caster must anchor the gallery map.");
        }

        return mapEntity.MapId;
    }

    private void ApplyBeat(GraphOpsNodeDriverContext ctx)
    {
        float health = ctx.Api.ReadMapVarFloat(
            Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(_healthVarName),
            RequireMapId(ctx));
        var values = ctx.CaptionValues;
        values["name"] = _bossName;
        values["state"] = _killed ? "已倒下，位置空缺" : "在岗应答";
        values["health"] = health.ToString("0.#", CultureInfo.InvariantCulture);
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, values);
    }

    private static string RequireHealthVarName(GraphOpsNodeDriverContext ctx)
    {
        for (int i = 0; i < ctx.Vignette.Variables.Length; i++)
        {
            GraphOpsNodeVignetteVariable variable = ctx.Vignette.Variables[i];
            if (string.Equals(variable.Type, "float", StringComparison.OrdinalIgnoreCase))
            {
                return variable.Name;
            }
        }

        throw new InvalidOperationException(
            $"Placed entity gallery '{ctx.Vignette.Op}' requires a float map variable for the mirrored health read.");
    }
}
