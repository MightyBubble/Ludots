using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class AttrNodeDriver : IGraphOpsNodeDriver
{
    public const string MarkEffectId = "Effect.GraphOpsAttr.Mark";

    private int _markTemplateId;
    private Entity _seededMark = Entity.Null;

    public int PendingEffectRequests => ctxRequests?.Count ?? 0;
    private EffectRequestQueue? ctxRequests;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        _markTemplateId = EffectTemplateIdRegistry.GetId(MarkEffectId);
        if (_markTemplateId <= 0)
        {
            throw new InvalidOperationException(
                $"Attr gallery requires '{MarkEffectId}' loaded through EffectTemplateLoader.");
        }
        ctxRequests = ctx.EffectRequests
            ?? throw new InvalidOperationException($"Attr gallery '{ctx.Vignette.Op}' requires EffectRequestQueue.");
        if (string.Equals(ctx.Vignette.Op, "RemoveEffectTemplate", StringComparison.Ordinal))
        {
            EnsureMarkOnTarget(ctx);
        }

        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.EffectRequests == null)
        {
            throw new InvalidOperationException(
                $"AttrNodeDriver requires EffectRequestQueue before Tick. Op={ctx.Vignette.Op}");
        }

        ctxRequests = ctx.EffectRequests;
        if (string.Equals(ctx.Vignette.Op, "RemoveEffectTemplate", StringComparison.Ordinal))
        {
            EnsureMarkOnTarget(ctx);
        }

        GraphOpsNodeActorBinding.RestoreVignetteHealth(ctx);
        int casterIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        int targetIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        float targetBefore = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        float casterBefore = casterIndex >= 0 ? ctx.ActorHealth[casterIndex] : 0f;
        GraphOpsNodeExecuteResult result = ctx.ExecuteFeaturedGraph();
        GraphOpsNodeActorBinding.SyncActorHealthFromWorld(ctx);

        float targetAfter = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        float casterAfter = casterIndex >= 0 ? ctx.ActorHealth[casterIndex] : 0f;
        FillCaptions(ctx, result, targetBefore, targetAfter, casterBefore, casterAfter);
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        FailClose(ctx, result, targetBefore, targetAfter, casterBefore, casterAfter);
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

    private void EnsureMarkOnTarget(GraphOpsNodeDriverContext ctx)
    {
        Entity target = GraphOpsNodeActorBinding.RequireRole(ctx, "target");
        ctx.Target = target;
        if (_seededMark != Entity.Null &&
            ctx.SimWorld.IsAlive(_seededMark) &&
            ctx.SimWorld.Has<GameplayEffect>(_seededMark) &&
            !ctx.SimWorld.Get<GameplayEffect>(_seededMark).CancelRequested)
        {
            return;
        }

        if (ctx.EffectTemplates == null || !ctx.EffectTemplates.TryGet(_markTemplateId, out EffectTemplateData mark))
        {
            throw new InvalidOperationException(
                $"Attr gallery requires '{MarkEffectId}' in the production EffectTemplateRegistry.");
        }

        if (mark.LifetimeKind == EffectLifetimeKind.Instant)
        {
            throw new InvalidOperationException(
                $"'{MarkEffectId}' cannot seed a lasting mark with Instant lifetime.");
        }

        _seededMark = GameplayEffectFactory.CreateEffect(
            ctx.SimWorld,
            ctx.Caster,
            target,
            mark.DurationTicks,
            mark.LifetimeKind,
            mark.PeriodTicks,
            ctx.TargetContext,
            mark.ClockId,
            mark.ExpireCondition);
        ctx.SimWorld.Add(_seededMark, new EffectTemplateRef { TemplateId = _markTemplateId });
        ref GameplayEffect gameplayEffect = ref ctx.SimWorld.Get<GameplayEffect>(_seededMark);
        gameplayEffect.AggregatesModifiers = mark.PresetType == EffectPresetType.Buff;
        if (!ctx.SimWorld.Has<ActiveEffectContainer>(target))
        {
            ctx.SimWorld.Add(target, new ActiveEffectContainer());
        }

        ref ActiveEffectContainer container = ref ctx.SimWorld.Get<ActiveEffectContainer>(target);
        if (!container.Add(_seededMark))
        {
            throw new InvalidOperationException("Failed to attach gallery mark effect for RemoveEffectTemplate.");
        }
    }

    private static void FillCaptions(
        GraphOpsNodeDriverContext ctx,
        GraphOpsNodeExecuteResult result,
        float targetBefore,
        float targetAfter,
        float casterBefore,
        float casterAfter)
    {
        ctx.CaptionValues["layers"] = result.IntValue.ToString();
        ctx.CaptionValues["combo"] = result.IntValue.ToString();
        ctx.CaptionValues["hp"] = result.FloatValue.ToString("0");
        ctx.CaptionValues["style"] = result.BoolValue ? "全力" : "轻击";
        ctx.CaptionValues["healthBefore"] = targetBefore.ToString("0");
        ctx.CaptionValues["healthAfter"] = targetAfter.ToString("0");
        ctx.CaptionValues["casterBefore"] = casterBefore.ToString("0");
        ctx.CaptionValues["casterAfter"] = casterAfter.ToString("0");
    }

    private void FailClose(
        GraphOpsNodeDriverContext ctx,
        GraphOpsNodeExecuteResult result,
        float targetBefore,
        float targetAfter,
        float casterBefore,
        float casterAfter)
    {
        string op = ctx.Vignette.Op;
        if (string.Equals(op, "ConstInt", StringComparison.Ordinal) && result.IntValue != 3)
        {
            throw new InvalidOperationException($"ConstInt gallery expected 层数 3, got {result.IntValue}.");
        }

        if (string.Equals(op, "AddInt", StringComparison.Ordinal) && result.IntValue != 3)
        {
            throw new InvalidOperationException($"AddInt gallery expected 连击 3, got {result.IntValue}.");
        }

        if (string.Equals(op, "LoadCaster", StringComparison.Ordinal) && result.EntityValue != ctx.Caster)
        {
            throw new InvalidOperationException("LoadCaster gallery did not resolve the caster.");
        }

        if (string.Equals(op, "LoadExplicitTarget", StringComparison.Ordinal) ||
            string.Equals(op, "LoadContextTarget", StringComparison.Ordinal))
        {
            if (result.EntityValue != ctx.Target)
            {
                throw new InvalidOperationException($"{op} gallery must resolve 木桩.");
            }
        }

        if (string.Equals(op, "LoadAttribute", StringComparison.Ordinal) && result.FloatValue <= 0f)
        {
            throw new InvalidOperationException("LoadAttribute gallery read no health.");
        }

        if (string.Equals(op, "LoadSelfAttribute", StringComparison.Ordinal) && result.FloatValue <= 0f)
        {
            throw new InvalidOperationException("LoadSelfAttribute gallery read no caster health.");
        }

        if (string.Equals(op, "CompareLtInt", StringComparison.Ordinal) && !result.BoolValue)
        {
            throw new InvalidOperationException("CompareLtInt gallery seeded below the line must 全力.");
        }

        if (string.Equals(op, "CompareEqInt", StringComparison.Ordinal) && !result.BoolValue)
        {
            throw new InvalidOperationException("CompareEqInt gallery expected 叠满.");
        }

        if (string.Equals(op, "CompareEqEntity", StringComparison.Ordinal) && result.BoolValue)
        {
            throw new InvalidOperationException("CompareEqEntity gallery expected 打的不是自己.");
        }

        if (string.Equals(op, "SelectEntity", StringComparison.Ordinal) && result.EntityValue != ctx.Target)
        {
            throw new InvalidOperationException("SelectEntity gallery expected 打向木桩.");
        }

        if (string.Equals(op, "ModifyAttributeAdd", StringComparison.Ordinal) && targetAfter >= targetBefore)
        {
            throw new InvalidOperationException("ModifyAttributeAdd gallery must actually 扣血.");
        }

        if (string.Equals(op, "WriteSelfAttribute", StringComparison.Ordinal) && casterAfter <= casterBefore)
        {
            throw new InvalidOperationException("WriteSelfAttribute gallery must 回 health on the caster.");
        }

        if (string.Equals(op, "ApplyEffectTemplate", StringComparison.Ordinal) && PendingEffectRequests <= 0)
        {
            throw new InvalidOperationException("ApplyEffectTemplate gallery enqueued zero effect requests.");
        }

        if (string.Equals(op, "RemoveEffectTemplate", StringComparison.Ordinal))
        {
            if (_seededMark == Entity.Null ||
                !ctx.SimWorld.IsAlive(_seededMark) ||
                !ctx.SimWorld.Get<GameplayEffect>(_seededMark).CancelRequested)
            {
                throw new InvalidOperationException("RemoveEffectTemplate gallery did not 卸 the seeded mark.");
            }
        }
    }
}
