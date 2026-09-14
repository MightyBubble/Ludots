using System;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

/// <summary>
/// Hosts the SubmitCommandIntent vignette: the TriggerGraph resolves a ground point through the
/// aim-source helper and hands one command intent to the order pipeline's submission buffer
/// (constitution §12); the graph never routes inline. This driver executes the compiled graph
/// against the engine's production API and captions from what the buffer actually holds —
/// the drain runs in the order kernel's own phase, which the gallery does not tick.
/// </summary>
public sealed class CommandIntentNodeDriver : IGraphOpsNodeDriver
{
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
    private Ludots.Core.Gameplay.GAS.Orders.CommandIntentSubmissionBuffer _submissions = null!;

    public int SubmittedCount { get; private set; }

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        _ = GraphOpsNodeActorBinding.RequireRole(ctx, "caster");

        _submissions = ctx.CommandIntents
            ?? throw new InvalidOperationException(
                $"Command intent gallery '{ctx.Vignette.Op}' requires the engine command intent submission buffer.");

        // Same gallery aim-source contract as the aimsource driver: a flat bounded ground so
        // the vignette's authored screen point resolves through the ray→ground chain.
        var worldBounds = new Ludots.Platform.Abstractions.WorldAabbCm(-10_000, -10_000, 20_000, 20_000);
        var galleryGlobals = new System.Collections.Generic.Dictionary<string, object>
        {
            [Ludots.Core.Scripting.CoreServiceKeys.ScreenRayProvider.Name] = new GalleryGroundRayProvider(),
            [Ludots.Core.Scripting.CoreServiceKeys.ContinuousHeightmap.Name] =
                new Ludots.Core.Presentation.Terrain.ContinuousHeightmapRuntime(
                    Ludots.Core.Presentation.Terrain.ContinuousHeightmapAsset.CreateSingleLayer(
                        worldBounds,
                        sampleColumns: 2,
                        sampleRows: 2,
                        new short[] { 0, 0, 0, 0 })),
            [Ludots.Core.Scripting.CoreServiceKeys.WorldSizeSpec.Name] =
                new Ludots.Core.Spatial.WorldSizeSpec(worldBounds, 100),
        };
        ctx.Api.BindAimSource(
            new Ludots.Core.Input.AimSource.GraphAimSourceRuntime(ctx.SimWorld, galleryGlobals));

        GraphProgramPackage package = ctx.Compiled.Package!.Value;
        if (package.TriggerGraphEntries is not { Length: 1 } entries)
        {
            throw new InvalidOperationException(
                $"Command intent gallery '{ctx.Vignette.Op}' must compile to exactly one TriggerGraph entry.");
        }

        _program = package.Program;
        _cursor = new GraphExecutionCursor(entries[0].StartPc);
        _halted = false;
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (!_halted)
        {
            bool castVariant = string.Equals(ctx.Vignette.Op, "SubmitCast", StringComparison.Ordinal);
            int before = castVariant ? _submissions.CastCount : _submissions.Count;
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
            GraphSliceResult result = GraphExecutor.ExecuteSlice(ref frame, _program, SliceBudget);
            _cursor = frame.Cursor;
            if (!result.Halted)
            {
                throw new InvalidOperationException(
                    $"Command intent gallery '{ctx.Vignette.Op}' ended with status {result.Status}; the submit must halt in one slice.");
            }

            _halted = true;
            SubmittedCount = (castVariant ? _submissions.CastCount : _submissions.Count) - before;
        }

        ApplyBeat(ctx);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
    }

    private void ApplyBeat(GraphOpsNodeDriverContext ctx)
    {
        if (SubmittedCount != 1)
        {
            throw new InvalidOperationException(
                $"Command intent gallery '{ctx.Vignette.Op}' expected the submit to push exactly one intent, got {SubmittedCount}.");
        }

        var values = ctx.CaptionValues;
        values["count"] = SubmittedCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, values);
    }

    private sealed class GalleryGroundRayProvider : Ludots.Platform.Abstractions.IScreenRayProvider
    {
        public Ludots.Platform.Abstractions.ScreenRay GetRay(System.Numerics.Vector2 screenPosition)
        {
            return new Ludots.Platform.Abstractions.ScreenRay(
                new System.Numerics.Vector3(screenPosition.X / 100f, 10f, screenPosition.Y / 100f),
                -System.Numerics.Vector3.UnitY);
        }
    }
}
