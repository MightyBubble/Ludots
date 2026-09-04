using System;
using System.Threading.Tasks;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

/// <summary>
/// Hosts the WriteCollection vignette: the TriggerGraph computes the set semantics in-graph
/// and writes the owned collection directly through the graph-side write primitive; this
/// driver binds the engine EntityCollectionStore, pre-seeds the run's TargetList with the
/// gallery target, and captions from what the store actually holds.
/// </summary>
public sealed class CollectionWriteNodeDriver : IGraphOpsNodeDriver
{
    public const string CollectionKey = "gallery.selected";
    private const int SliceBudget = 96;

    private readonly float[] _floats = new float[GraphVmLimits.MaxFloatRegisters];
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly Entity[] _entities = new Entity[GraphVmLimits.MaxEntityRegisters];
    private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
    private readonly int[] _intIds = new int[GraphVmLimits.MaxIntIds];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private GraphInstruction[] _program = Array.Empty<GraphInstruction>();
    private GraphExecutionCursor _cursor;
    private bool _halted;
    private EntityCollectionStore _store = null!;
    private int _collectionKeyId;

    public int CommittedCount { get; private set; }

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        _ = GraphOpsNodeActorBinding.RequireRole(ctx, "caster");
        _ = GraphOpsNodeActorBinding.RequireRole(ctx, "target");

        _store = ctx.Collections
            ?? throw new InvalidOperationException(
                $"Collection gallery '{ctx.Vignette.Op}' requires the engine EntityCollectionStore.");
        _collectionKeyId = _store.KeyRegistry.Register(CollectionKey);

        GraphProgramPackage package = ctx.Compiled.Package!.Value;
        if (package.TriggerGraphEntries is not { Length: 1 } entries)
        {
            throw new InvalidOperationException(
                $"Collection gallery '{ctx.Vignette.Op}' must compile to exactly one TriggerGraph entry.");
        }

        _program = package.Program;
        _cursor = new GraphExecutionCursor(entries[0].StartPc);
        _halted = false;
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
            frame.Targets[0] = ctx.Target;
            frame.TargetList.SetCount(1);
            GraphSliceResult result = GraphExecutor.ExecuteSlice(ref frame, _program, SliceBudget);
            _cursor = frame.Cursor;
            if (!result.Halted)
            {
                throw new InvalidOperationException(
                    $"Collection gallery '{ctx.Vignette.Op}' ended with status {result.Status}; the write must halt in one slice.");
            }

            _halted = true;
        }

        ApplyBeat(ctx);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
    }

    private void ApplyBeat(GraphOpsNodeDriverContext ctx)
    {
        bool exists = _store.TryGet(ctx.Caster, _collectionKeyId, out EntityCollectionHandle handle);
        EntityCollectionView view = default;
        exists = exists && _store.TryGetView(handle, out view);
        CommittedCount = exists ? view.Count : 0;
        if (CommittedCount != 1)
        {
            throw new InvalidOperationException(
                $"Collection gallery '{ctx.Vignette.Op}' expected the write to commit exactly one member, got {CommittedCount}.");
        }

        var values = ctx.CaptionValues;
        values["count"] = CommittedCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, values);
    }
}
