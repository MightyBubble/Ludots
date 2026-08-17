using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class AttrNodeDriver : IGraphOpsNodeDriver
{
    public const string MarkEffectId = "Effect.GraphOpsAttr.Mark";
    public const string NewBodyTemplate = "GraphOps.Ally";
    public const string NewBodyName = "新身体";

    private const float MarkHaloRadius = 0.9f;
    private const float MarkBadgeLift = 2.4f;
    private static readonly DebugDrawColor MarkViolet = new(198, 0, 255);
    private static readonly DebugDrawColor LedgerSeal = DebugDrawColor.Cyan;
    private static readonly DebugDrawColor LedgerStamp = DebugDrawColor.Red;

    private int _markTemplateId;
    private Entity _seededMark = Entity.Null;
    private Entity _boundBody = Entity.Null;

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
        if (IsLifecycleOp(ctx.Vignette.Op))
        {
            BindMaterializedBody(ctx);
        }

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
        if (caster < 0)
        {
            return;
        }

        if (IsLifecycleOp(ctx.Vignette.Op))
        {
            DrawLifecycleLedger(ctx, debugDraw, caster);
            return;
        }

        if (target < 0)
        {
            return;
        }

        GraphShowcaseStagePresenter.DrawAggroLine(
            debugDraw,
            ctx.Vignette.Actors[caster].X,
            ctx.Vignette.Actors[caster].Y,
            ctx.Vignette.Actors[target].X,
            ctx.Vignette.Actors[target].Y);
        DrawLiveMark(ctx, debugDraw, target);
    }

    private static bool IsLifecycleOp(string op)
    {
        return string.Equals(op, "BeginLifecycleTransaction", StringComparison.Ordinal)
            || string.Equals(op, "InvokeBuiltin", StringComparison.Ordinal);
    }

    /// <summary>
    /// The materialized body is bound once as a formal stage actor so the recorder sees a named
    /// body (not a phantom). Headless galleries skip stage binding and only assert the body lives.
    /// </summary>
    private void BindMaterializedBody(GraphOpsNodeDriverContext ctx)
    {
        Entity body = ctx.LastMaterializedTarget;
        if (body == Entity.Null || !ctx.SimWorld.IsAlive(body))
        {
            throw new InvalidOperationException($"{ctx.Vignette.Op} must leave a live new body in the world.");
        }

        if (_boundBody != Entity.Null)
        {
            return;
        }

        if (ctx.Stage != null)
        {
            if (!ctx.SimWorld.Has<WorldPositionCm>(body))
            {
                throw new InvalidOperationException($"{ctx.Vignette.Op} new body is missing WorldPositionCm.");
            }

            WorldCmInt2 pos = ctx.SimWorld.Get<WorldPositionCm>(body).ToWorldCmInt2();
            float health = ctx.SimWorld.Has<AttributeBuffer>(body)
                ? GraphOpsNodeActorBinding.ReadHealth(ctx.SimWorld, body)
                : 0f;
            float healthMax = Math.Max(health, 1f);
            ctx.Stage.BindMapEntity(
                body,
                NewBodyTemplate,
                NewBodyName,
                pos.X / 100f,
                pos.Y / 100f,
                health,
                healthMax,
                bindAsViewer: false);
        }

        _boundBody = body;
    }

    /// <summary>Life-ledger at the caster's feet: open pages, then the cyan Begin seal and red 讫 stamp.</summary>
    private void DrawLifecycleLedger(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor actor = ctx.Vignette.Actors[caster];
        float lx = actor.X + 1.2f;
        float ly = actor.Y - 0.85f;
        float hw = 1.1f;
        float hh = 0.55f;

        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(lx - hw, ly + hh),
            B = new Vector2(lx + hw, ly + hh),
            Thickness = 0.07f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(lx - hw, ly - hh),
            B = new Vector2(lx + hw, ly - hh),
            Thickness = 0.07f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(lx - hw, ly + hh),
            B = new Vector2(lx - hw, ly - hh),
            Thickness = 0.07f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(lx + hw, ly + hh),
            B = new Vector2(lx + hw, ly - hh),
            Thickness = 0.07f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(lx, ly + hh),
            B = new Vector2(lx, ly - hh),
            Thickness = 0.06f,
            Color = GraphShowcaseStagePresenter.GateColor
        });

        DrawLedgerRow(debugDraw, lx - hw * 0.45f, ly + 0.3f, highlighted: ctx.Wave == 1);
        DrawLedgerRow(debugDraw, lx + hw * 0.45f, ly + 0.3f, highlighted: ctx.Wave >= 2);
        if (ctx.Wave >= 2)
        {
            debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = new Vector2(lx, ly - 0.15f),
                Radius = 0.34f,
                Thickness = 0.1f,
                Color = LedgerStamp
            });
        }
        else
        {
            debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = new Vector2(lx, ly - 0.15f),
                Radius = 0.28f,
                Thickness = 0.08f,
                Color = LedgerSeal
            });
        }

        DrawMaterializedBody(ctx, debugDraw);
        if (string.Equals(ctx.Vignette.Op, "InvokeBuiltin", StringComparison.Ordinal))
        {
            DrawClearedRack(ctx, debugDraw);
        }
    }

    private static void DrawLedgerRow(DebugDrawCommandBuffer debugDraw, float x, float y, bool highlighted)
    {
        DebugDrawColor color = highlighted
            ? GraphShowcaseStagePresenter.CasterColor
            : GraphShowcaseStagePresenter.GhostColor;
        debugDraw.Boxes.Add(new DebugDrawBox2D
        {
            Center = new Vector2(x, y),
            HalfWidth = 0.4f,
            HalfHeight = 0.1f,
            Thickness = highlighted ? 0.06f : 0.03f,
            Color = color
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(x - 0.3f, y + 0.06f),
            B = new Vector2(x + 0.3f, y + 0.06f),
            Thickness = 0.04f,
            Color = color
        });
    }

    /// <summary>The new body appears ghost→solid next to the target; the ledger seals mark the transaction.</summary>
    private void DrawMaterializedBody(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        Entity body = _boundBody;
        if (body == Entity.Null ||
            !ctx.SimWorld.IsAlive(body) ||
            !ctx.SimWorld.Has<WorldPositionCm>(body))
        {
            return;
        }

        WorldCmInt2 pos = ctx.SimWorld.Get<WorldPositionCm>(body).ToWorldCmInt2();
        float x = pos.X / 100f;
        float y = pos.Y / 100f;
        if (ctx.Wave >= 2)
        {
            GraphShowcaseStagePresenter.DrawThickOutlineCircle(
                debugDraw, x, y, 0.55f, GraphShowcaseStagePresenter.OutlineDark, GraphShowcaseStagePresenter.GuardColor);
        }
        else
        {
            GraphShowcaseStagePresenter.DrawGhostCircle(debugDraw, x, y, 0.55f, GraphShowcaseStagePresenter.GuardColor);
        }
    }

    /// <summary>After ClearActiveEffects the body's effect rack reads as a row of empty slots swept with a slash.</summary>
    private void DrawClearedRack(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        Entity body = _boundBody;
        if (body == Entity.Null ||
            !ctx.SimWorld.IsAlive(body) ||
            !ctx.SimWorld.Has<WorldPositionCm>(body))
        {
            return;
        }

        WorldCmInt2 pos = ctx.SimWorld.Get<WorldPositionCm>(body).ToWorldCmInt2();
        float x = pos.X / 100f;
        float y = pos.Y / 100f + 1.5f;
        for (int i = 0; i < 3; i++)
        {
            float slotX = x + (i - 1) * 0.34f;
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(slotX, y),
                HalfWidth = 0.12f,
                HalfHeight = 0.16f,
                Thickness = 0.05f,
                Color = GraphShowcaseStagePresenter.GateColor
            });
        }

        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(x - 0.55f, y + 0.24f),
            B = new Vector2(x + 0.55f, y - 0.24f),
            Thickness = 0.08f,
            Color = GraphShowcaseStagePresenter.EnemyColor
        });
    }

    private void DrawLiveMark(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int targetIndex)
    {
        if (!TargetHasLiveMark(ctx))
        {
            return;
        }

        GraphOpsNodeActor target = ctx.Vignette.Actors[targetIndex];
        GraphShowcaseStagePresenter.DrawThickOutlineCircle(
            debugDraw,
            target.X,
            target.Y,
            MarkHaloRadius,
            GraphShowcaseStagePresenter.OutlineDark,
            MarkViolet);
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw,
            target.X,
            target.Y + MarkBadgeLift,
            GraphShowcaseStagePresenter.BadgeKind.Diamond,
            MarkViolet);
    }

    private bool TargetHasLiveMark(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Target == Entity.Null ||
            !ctx.SimWorld.IsAlive(ctx.Target) ||
            !ctx.SimWorld.Has<ActiveEffectContainer>(ctx.Target))
        {
            return false;
        }

        ActiveEffectContainer container = ctx.SimWorld.Get<ActiveEffectContainer>(ctx.Target);
        for (int i = 0; i < container.Count; i++)
        {
            Entity effect = container.GetEntity(i);
            if (ctx.SimWorld.IsAlive(effect) &&
                ctx.SimWorld.Has<GameplayEffect>(effect) &&
                !ctx.SimWorld.Get<GameplayEffect>(effect).CancelRequested &&
                ctx.SimWorld.Has<EffectTemplateRef>(effect) &&
                ctx.SimWorld.Get<EffectTemplateRef>(effect).TemplateId == _markTemplateId)
            {
                return true;
            }
        }

        return false;
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

        if (IsLifecycleOp(op))
        {
            if (ctx.LastMaterializedTarget == Entity.Null ||
                !ctx.SimWorld.IsAlive(ctx.LastMaterializedTarget))
            {
                throw new InvalidOperationException($"{op} gallery must leave the new body alive in the world.");
            }

            if (string.Equals(op, "InvokeBuiltin", StringComparison.Ordinal) &&
                GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "mark") >= 0)
            {
                throw new InvalidOperationException("InvokeBuiltin gallery must not keep the mark stand-in actor.");
            }
        }
    }
}
