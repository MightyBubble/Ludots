using System;
using System.Globalization;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

/// <summary>
/// Hosts the SubmitResponseChainOrder vignette as a three-wave loop on the engine's own response
/// window: the caster's opening spell stops the window on its owner's prompt, the featured
/// TriggerGraph answers that prompt once, and the settled window shows up as damage on the target.
/// The driver only stages the spell and reads results; every answer comes from the graph.
/// </summary>
public sealed class ResponseChainNodeDriver : IGraphOpsNodeDriver
{
    private const int SliceBudget = 96;
    private const int PromptedPlayerId = 1;
    private const string OpeningEffectId = "Effect.GraphOps.ChainOpening";
    private const string OfferedEffectId = "Effect.GraphOps.ChainCounter";
    private const string OpeningCategoryId = "Effect.GraphOps.ChainSpell";

    private readonly float[] _floats = new float[GraphVmLimits.MaxFloatRegisters];
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly Entity[] _entities = new Entity[GraphVmLimits.MaxEntityRegisters];
    private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
    private readonly int[] _intIds = new int[GraphVmLimits.MaxIntIds];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private GraphInstruction[] _program = Array.Empty<GraphInstruction>();
    private int _entryPc;
    private ResponseChainPromptState _prompt = null!;
    private int _openingTemplateId;
    private Wave _wave = Wave.Cast;
    private float _targetHealthBeforeCast;

    private enum Wave
    {
        Cast,
        Answer,
        Settle,
    }

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        Entity caster = GraphOpsNodeActorBinding.RequireRole(ctx, "caster");
        _ = GraphOpsNodeActorBinding.RequireRole(ctx, "target");
        _prompt = ctx.ResponseChainPrompt
            ?? throw new InvalidOperationException(
                $"Response-chain gallery '{ctx.Vignette.Op}' requires the engine response-chain prompt state.");
        EffectRequestQueue effectRequests = ctx.EffectRequests
            ?? throw new InvalidOperationException(
                $"Response-chain gallery '{ctx.Vignette.Op}' requires the engine EffectRequestQueue.");

        _openingTemplateId = RequireEffectTemplateId(OpeningEffectId);
        int offeredTemplateId = RequireEffectTemplateId(OfferedEffectId);
        int openingCategory = EffectCategoryRegistry.GetId(OpeningCategoryId);
        if (openingCategory == EffectCategoryRegistry.InvalidId)
        {
            throw new InvalidOperationException(
                $"Response-chain gallery requires effect category '{OpeningCategoryId}' declared on the gallery effects.");
        }

        if (!ctx.SimWorld.Has<PlayerOwner>(caster))
        {
            ctx.SimWorld.Add(caster, new PlayerOwner { PlayerId = PromptedPlayerId });
        }

        if (!ctx.SimWorld.Has<ResponseChainListener>(caster))
        {
            var listener = default(ResponseChainListener);
            listener.Add(openingCategory, ResponseType.PromptInput, priority: 100, effectTemplateId: offeredTemplateId);
            ResponseChainListenerOps.Add(ctx.SimWorld, caster, in listener, effectRequests);
        }

        GraphProgramPackage package = ctx.Compiled.Package!.Value;
        if (package.TriggerGraphEntries is not { Length: 1 } entries)
        {
            throw new InvalidOperationException(
                $"Response-chain gallery '{ctx.Vignette.Op}' must compile to exactly one TriggerGraph entry.");
        }

        _program = package.Program;
        _entryPc = entries[0].StartPc;
        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        switch (_wave)
        {
            case Wave.Cast:
                GraphOpsNodeActorBinding.RestoreVignetteHealth(ctx);
                _targetHealthBeforeCast = GraphOpsNodeActorBinding.ReadHealth(ctx.SimWorld, ctx.Target);
                ctx.EffectRequests!.Publish(new EffectRequest
                {
                    Source = ctx.Caster,
                    Target = ctx.Target,
                    TargetContext = Entity.Null,
                    TemplateId = _openingTemplateId,
                });
                ctx.Metrics.Detail = ctx.Vignette.Beat;
                _wave = Wave.Answer;
                break;

            case Wave.Answer:
                if (!_prompt.IsOpen || _prompt.PlayerId != PromptedPlayerId || _prompt.Responder != ctx.Caster)
                {
                    throw new InvalidOperationException(
                        $"Response-chain gallery expected the opening spell to leave the window waiting on player {PromptedPlayerId}'s caster, whose listener asked to respond.");
                }

                RunFeaturedGraph(ctx);
                if (!_prompt.Answered || !OrderSubmitResultSemantics.IsAccepted(_prompt.LastSubmissionResult))
                {
                    throw new InvalidOperationException(
                        $"Response-chain gallery answer was rejected: {_prompt.LastSubmissionResult}.");
                }

                ctx.EffectSettlementRequested = true;
                ctx.Metrics.Detail = ctx.Vignette.Beat;
                _wave = Wave.Settle;
                break;

            case Wave.Settle:
                if (_prompt.IsOpen)
                {
                    throw new InvalidOperationException(
                        "Response-chain gallery expected the graph's answer to close the window.");
                }

                float damage = _targetHealthBeforeCast - GraphOpsNodeActorBinding.ReadHealth(ctx.SimWorld, ctx.Target);
                ctx.CaptionValues["damage"] = damage.ToString("0", CultureInfo.InvariantCulture);
                ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
                _wave = Wave.Cast;
                break;
        }

        GraphOpsNodeActorBinding.SyncActorHealthFromWorld(ctx);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
    }

    private void RunFeaturedGraph(GraphOpsNodeDriverContext ctx)
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
                $"Response-chain gallery graph ended with status {result.Status}; the answers must halt in one slice.");
        }
    }

    private static int RequireEffectTemplateId(string effectId)
    {
        int id = EffectTemplateIdRegistry.GetId(effectId);
        if (id <= 0)
        {
            throw new InvalidOperationException(
                $"Response-chain gallery requires effect template '{effectId}' declared on the gallery effects.");
        }

        return id;
    }
}
