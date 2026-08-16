using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class LinearNodeDriver : IGraphOpsNodeDriver
{
    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeLinearOptions linear = ctx.Vignette.Linear
            ?? throw new InvalidOperationException($"Linear vignette {ctx.Vignette.Op} requires a linear block.");

        int targetIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        float healthBefore = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        GraphOpsNodeExecuteResult result = ctx.ExecuteFeaturedGraph();
        string resultText = FormatResult(linear.ResultKind, result);
        ApplyResult(ctx, linear, result, targetIndex);
        GraphOpsNodeActorBinding.SyncActorHealthFromWorld(ctx);

        float healthAfter = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
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

        GraphShowcaseStagePresenter.DrawAggroLine(
            debugDraw,
            ctx.Vignette.Actors[caster].X,
            ctx.Vignette.Actors[caster].Y,
            ctx.Vignette.Actors[target].X,
            ctx.Vignette.Actors[target].Y);
    }

    private static void ApplyResult(
        GraphOpsNodeDriverContext ctx,
        GraphOpsNodeLinearOptions linear,
        GraphOpsNodeExecuteResult result,
        int targetIndex)
    {
        if (string.Equals(linear.ApplyTo, "none", StringComparison.Ordinal))
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
