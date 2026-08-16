using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class LinearNodeDriver : IGraphOpsNodeDriver
{
    private const string GraphSettled = "graphSettled";

    // Settle bench scale: the 4.8m track is the same 100-point ruler as the health bar.
    private const float TrackMeters = 4.8f;
    private const float TrackPoints = 100f;
    private const float TrackY = 1.6f;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeLinearOptions linear = ctx.Vignette.Linear
            ?? throw new InvalidOperationException($"Linear vignette {ctx.Vignette.Op} requires a linear block.");

        bool settledByGraph = IsGraphSettled(linear);
        if (settledByGraph)
        {
            GraphOpsNodeActorBinding.RestoreVignetteHealth(ctx);
        }

        int targetIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        float healthBefore = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        GraphOpsNodeExecuteResult result = ctx.ExecuteFeaturedGraph();
        string resultText = FormatResult(linear.ResultKind, result);
        ApplyResult(ctx, linear, result, targetIndex);
        GraphOpsNodeActorBinding.SyncActorHealthFromWorld(ctx);

        float healthAfter = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        if (settledByGraph && targetIndex >= 0 && healthAfter >= healthBefore)
        {
            throw new InvalidOperationException(
                $"Linear apply '{GraphSettled}' expected {ctx.Vignette.Op} to settle real damage on the target via its graph tail.");
        }

        ctx.CaptionValues["result"] = resultText;
        ctx.CaptionValues["healthBefore"] = healthBefore.ToString("0");
        ctx.CaptionValues["healthAfter"] = healthAfter.ToString("0");
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        int target = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        if (caster < 0 || target < 0)
        {
            return;
        }

        if (ctx.Vignette.Linear != null && IsGraphSettled(ctx.Vignette.Linear))
        {
            DrawSettleBench(ctx, debugDraw, caster, target);
            return;
        }

        GraphShowcaseStagePresenter.DrawAggroLine(
            debugDraw,
            ctx.Vignette.Actors[caster].X,
            ctx.Vignette.Actors[caster].Y,
            ctx.Vignette.Actors[target].X,
            ctx.Vignette.Actors[target].Y);
    }

    private static bool IsGraphSettled(GraphOpsNodeLinearOptions linear)
    {
        return string.Equals(linear.ApplyTo, GraphSettled, StringComparison.Ordinal);
    }

    /// <summary>
    /// Damage bench between caster and target: a 100-point track where the yellow input segment is
    /// stretched by the multiplier badge (15 over 10) into the red result segment aimed at the target.
    /// </summary>
    private static void DrawSettleBench(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        (float input, float multiplier) = ReadMulFeeds(ctx);
        float scaled = input * multiplier;
        float metersPerPoint = TrackMeters / TrackPoints;

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        float midX = (casterActor.X + targetActor.X) * 0.5f;
        float trackLeft = midX - TrackMeters * 0.5f;
        float inputEnd = trackLeft + input * metersPerPoint;
        float resultEnd = inputEnd + scaled * metersPerPoint;

        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(trackLeft, TrackY),
            B = new Vector2(trackLeft + TrackMeters, TrackY),
            Thickness = 0.06f,
            Color = GraphShowcaseStagePresenter.GhostColor
        });
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, trackLeft, TrackY, inputEnd, TrackY, 0.22f, GraphShowcaseStagePresenter.CasterColor, arrowEnd: false);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, inputEnd, TrackY, resultEnd, TrackY, 0.22f, GraphShowcaseStagePresenter.EnemyColor);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, resultEnd, TrackY, targetActor.X, targetActor.Y, 0.1f, GraphShowcaseStagePresenter.EnemyColor);

        DrawMultiplierBadge(debugDraw, midX);
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, (trackLeft + inputEnd) * 0.5f + 0.25f, TrackY - 0.5f, (int)input, 0.45f, GraphShowcaseStagePresenter.CasterColor);
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, (inputEnd + resultEnd) * 0.5f + 0.3f, TrackY - 0.5f, (int)scaled, 0.45f, GraphShowcaseStagePresenter.EnemyColor);
    }

    /// <summary>Ring over the track midpoint plus 15-over-10 fraction glyphs reading as ×1.5.</summary>
    private static void DrawMultiplierBadge(DebugDrawCommandBuffer debugDraw, float midX)
    {
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw, midX, TrackY + 0.75f, GraphShowcaseStagePresenter.BadgeKind.Ring, GraphShowcaseStagePresenter.GateColor, scale: 1.2f);

        float numberRightEdge = midX + 1.5f;
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, numberRightEdge, TrackY + 1.25f, 15, 0.5f, GraphShowcaseStagePresenter.CasterColor);
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(midX + 0.75f, TrackY + 0.95f),
            B = new Vector2(numberRightEdge, TrackY + 0.95f),
            Thickness = 0.06f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, numberRightEdge, TrackY + 0.55f, 10, 0.5f, GraphShowcaseStagePresenter.GateColor);
    }

    /// <summary>The bench numbers are the graph's own ConstFloat immediates: base damage, then multiplier.</summary>
    private static (float Input, float Multiplier) ReadMulFeeds(GraphOpsNodeDriverContext ctx)
    {
        Span<float> consts = stackalloc float[2];
        int count = 0;
        foreach (GraphInstruction ins in ctx.Compiled.Program)
        {
            if (ins.Op != (ushort)GraphNodeOp.ConstFloat)
            {
                continue;
            }

            if (count >= consts.Length)
            {
                throw new InvalidOperationException(
                    $"Linear '{GraphSettled}' bench for {ctx.Vignette.Op} expects exactly two ConstFloat feeds (base, multiplier).");
            }

            consts[count++] = ins.ImmF;
        }

        if (count != consts.Length)
        {
            throw new InvalidOperationException(
                $"Linear '{GraphSettled}' bench for {ctx.Vignette.Op} expects exactly two ConstFloat feeds (base, multiplier).");
        }

        return (consts[0], consts[1]);
    }

    private static void ApplyResult(
        GraphOpsNodeDriverContext ctx,
        GraphOpsNodeLinearOptions linear,
        GraphOpsNodeExecuteResult result,
        int targetIndex)
    {
        if (string.Equals(linear.ApplyTo, "none", StringComparison.Ordinal) || IsGraphSettled(linear))
        {
            return;
        }

        if (targetIndex < 0)
        {
            throw new InvalidOperationException($"Linear apply '{linear.ApplyTo}' requires a target actor.");
        }

        float next = ctx.ActorHealth[targetIndex];
        if (string.Equals(linear.ApplyTo, "targetHealthSet", StringComparison.Ordinal))
        {
            next = RequireFloat(linear, result);
        }
        else if (string.Equals(linear.ApplyTo, "targetHealthSubtract", StringComparison.Ordinal))
        {
            next = Math.Max(0f, next - RequireFloat(linear, result));
        }
        else
        {
            throw new InvalidOperationException($"Unknown linear.applyTo '{linear.ApplyTo}'.");
        }

        GraphOpsNodeActor actor = ctx.Vignette.Actors[targetIndex];
        GraphOpsNodeActorBinding.WriteHealth(
            ctx.SimWorld,
            ctx.SimActors[targetIndex],
            next,
            actor.HealthMax,
            GraphOpsNodeActorBinding.RequireTagOps(ctx));
        ctx.ActorHealth[targetIndex] = next;
    }

    private static float RequireFloat(GraphOpsNodeLinearOptions linear, GraphOpsNodeExecuteResult result)
    {
        return linear.ResultKind switch
        {
            "float" => result.FloatValue,
            "int" => result.IntValue,
            "bool" => result.BoolValue ? 1f : 0f,
            _ => throw new InvalidOperationException($"Unknown linear.resultKind '{linear.ResultKind}'.")
        };
    }

    private static string FormatResult(string resultKind, GraphOpsNodeExecuteResult result)
    {
        return resultKind switch
        {
            "float" => result.FloatValue.ToString("0.#"),
            "int" => result.IntValue.ToString(),
            "bool" => result.BoolValue ? "成立" : "不成立",
            _ => throw new InvalidOperationException($"Unknown linear.resultKind '{resultKind}'.")
        };
    }
}
