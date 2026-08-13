using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class LinearNodeDriver : IGraphOpsNodeDriver
{
    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        if (ctx.SimActors.Length == 0)
        {
            ctx.SimActors = new Entity[actors.Length];
            ctx.ActorHealth = new float[actors.Length];
            for (int i = 0; i < actors.Length; i++)
            {
                Entity entity = ctx.SimWorld.Create();
                ctx.SimActors[i] = entity;
                ctx.ActorHealth[i] = actors[i].Health;
                if (string.Equals(actors[i].Role, "caster", StringComparison.Ordinal))
                {
                    ctx.Caster = entity;
                }
                else if (string.Equals(actors[i].Role, "target", StringComparison.Ordinal))
                {
                    ctx.Target = entity;
                }
            }

            if (ctx.Caster == Entity.Null)
            {
                throw new InvalidOperationException($"Linear vignette {ctx.Vignette.Op} requires a caster actor.");
            }

            ctx.Metrics.AgentCount = actors.Length;
            ctx.Metrics.Detail = ctx.Vignette.Beat;
        }

        SpawnStage(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeLinearOptions linear = ctx.Vignette.Linear
            ?? throw new InvalidOperationException($"Linear vignette {ctx.Vignette.Op} requires a linear block.");

        int targetIndex = FindRole(ctx, "target");
        float healthBefore = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        GraphOpsNodeExecuteResult result = ctx.ExecuteFeaturedGraph();
        string resultText = FormatResult(linear.ResultKind, result);
        ApplyResult(ctx, linear, result, targetIndex);

        float healthAfter = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        ctx.CaptionValues["result"] = resultText;
        ctx.CaptionValues["healthBefore"] = healthBefore.ToString("0");
        ctx.CaptionValues["healthAfter"] = healthAfter.ToString("0");
        ctx.Metrics.Detail = FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        SyncStage(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int caster = FindRole(ctx, "caster");
        int target = FindRole(ctx, "target");
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

        float opening = ctx.Vignette.Actors[targetIndex].Health;
        float next = ctx.ActorHealth[targetIndex];
        if (string.Equals(linear.ApplyTo, "targetHealthSet", StringComparison.Ordinal))
        {
            next = RequireFloat(linear, result);
        }
        else if (string.Equals(linear.ApplyTo, "targetHealthSubtract", StringComparison.Ordinal))
        {
            next -= RequireFloat(linear, result);
            if (next <= 0f)
            {
                next = opening;
            }
        }
        else
        {
            throw new InvalidOperationException($"Unknown linear.applyTo '{linear.ApplyTo}'.");
        }

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

    private static string FormatDetail(string template, Dictionary<string, string> values)
    {
        string text = template;
        foreach (KeyValuePair<string, string> pair in values)
        {
            text = text.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        }

        if (text.Contains('{', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Detail template still has unsubstituted placeholders: {text}");
        }

        return text;
    }

    private static int FindRole(GraphOpsNodeDriverContext ctx, string role)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            if (string.Equals(actors[i].Role, role, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static void SpawnStage(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Stage == null || ctx.StageProxies.Length > 0)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        ctx.StageProxies = new Entity[actors.Length];
        for (int i = 0; i < actors.Length; i++)
        {
            GraphOpsNodeActor actor = actors[i];
            ctx.StageProxies[i] = ctx.Stage.Spawn(
                actor.Template,
                actor.Name,
                actor.X,
                actor.Y,
                ctx.ActorHealth[i],
                actor.HealthMax);
        }
    }

    private static void SyncStage(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Stage == null || ctx.StageProxies.Length == 0)
        {
            return;
        }

        for (int i = 0; i < ctx.StageProxies.Length; i++)
        {
            GraphOpsNodeActor actor = ctx.Vignette.Actors[i];
            ctx.Stage.SetPosition(ctx.StageProxies[i], actor.X, actor.Y);
            ctx.Stage.SetHealth(ctx.StageProxies[i], ctx.ActorHealth[i], actor.HealthMax);
        }
    }
}
