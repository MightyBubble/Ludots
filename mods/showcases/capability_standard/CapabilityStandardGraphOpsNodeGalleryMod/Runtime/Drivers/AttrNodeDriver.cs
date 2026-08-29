using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Platform.Abstractions;
using Ludots.Core.Mathematics;


namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class AttrNodeDriver : IGraphOpsNodeDriver
{
    public const string MarkEffectId = "Effect.GraphOpsAttr.Mark";
    public const string StrikeEffectId = "Effect.GraphOps.Strike";
    public const string NewBodyTemplate = "GraphOps.Ally";
    public const string NewBodyName = "新身体";
    public const string HealthIntKey = "showcase.attr.healthInt";

    private const float MarkHaloRadius = 0.9f;
    private const float MarkBadgeLift = 2.4f;
    private static readonly DebugDrawColor MarkViolet = new(198, 0, 255);
    private static readonly DebugDrawColor LedgerSeal = DebugDrawColor.Cyan;
    private static readonly DebugDrawColor LedgerStamp = DebugDrawColor.Red;
    private static readonly DebugDrawColor LedgerCyan = DebugDrawColor.Cyan;
    private static readonly DebugDrawColor Gold = DebugDrawColor.Yellow;
    private static readonly DebugDrawColor CoinCopper = new(222, 140, 32);
    private static readonly DebugDrawColor WriteGreen = DebugDrawColor.Green;

    private int _markTemplateId;
    private int _strikeTemplateId;
    private int _healthIntKey;
    private float _strikeDelta;
    private Entity _seededMark = Entity.Null;
    private Entity _boundBody = Entity.Null;
    private bool _markWasSeeded;

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

        ResolveStrikeTemplate(ctx);
        if (string.Equals(ctx.Vignette.Op, "CompareLtInt", StringComparison.Ordinal))
        {
            _healthIntKey = ConfigKeyRegistry.Register(HealthIntKey);
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
        ProjectHealthInt(ctx, targetIndex);
        GraphOpsNodeExecuteResult result = ctx.ExecuteFeaturedGraph();
        if (IsLifecycleOp(ctx.Vignette.Op))
        {
            BindMaterializedBody(ctx);
        }

        GraphOpsNodeActorBinding.SyncActorHealthFromWorld(ctx);

        float targetAfter = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        float casterAfter = casterIndex >= 0 ? ctx.ActorHealth[casterIndex] : 0f;
        if (IsStrikeSettledOp(ctx.Vignette.Op))
        {
            RequireStrikeRequestOnTarget(ctx);
            targetAfter = Math.Clamp(targetBefore + _strikeDelta, 0f, MaxHealth(ctx, targetIndex));
        }

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

        switch (ctx.Vignette.Op)
        {
            case "LoadContextTarget":
                DrawLedgerRing(ctx, debugDraw, caster, target);
                break;
            case "LoadAttribute":
                DrawReconLine(ctx, debugDraw, caster, target);
                break;
            case "ConstInt":
                DrawCastPlate(ctx, debugDraw, caster, target);
                break;
            case "CompareEqInt":
                DrawScaleStrike(ctx, debugDraw, caster, target);
                break;
            case "CompareEqEntity":
                DrawIdentitySeal(ctx, debugDraw, caster, target);
                break;
            case "RemoveEffectTemplate":
                DrawMarkShatter(ctx, debugDraw, target);
                break;
            case "SelectEntity":
                DrawForkGate(ctx, debugDraw, caster, target);
                break;
            case "ModifyAttributeAdd":
                DrawDamageFloat(ctx, debugDraw, target);
                break;
            case "ModifyAttributeSet":
                DrawDamageFloat(ctx, debugDraw, target);
                break;
            case "LoadSelfAttribute":
                DrawSelfLoop(ctx, debugDraw, caster);
                break;
            case "WriteSelfAttribute":
                DrawWriteLine(ctx, debugDraw, caster);
                break;
            case "CompareLtInt":
                DrawThresholdRuler(ctx, debugDraw, caster, target);
                break;
            case "LoadCaster":
                DrawIdentityBeam(ctx, debugDraw, caster, target);
                break;
            case "AddInt":
                DrawComboBench(ctx, debugDraw, caster, target);
                break;
            case "LoadExplicitTarget":
                DrawCrosshairLock(ctx, debugDraw, caster, target);
                break;
            case "ApplyEffectTemplate":
                GraphShowcaseStagePresenter.DrawAggroLine(
                    debugDraw,
                    ctx.Vignette.Actors[caster].X,
                    ctx.Vignette.Actors[caster].Y,
                    ctx.Vignette.Actors[target].X,
                    ctx.Vignette.Actors[target].Y);
                DrawLiveMark(ctx, debugDraw, target);
                break;
        }
    }

    private static bool IsLifecycleOp(string op)
    {
        return string.Equals(op, "BeginLifecycleTransaction", StringComparison.Ordinal)
            || string.Equals(op, "InvokeBuiltin", StringComparison.Ordinal);
    }

    private static bool IsStrikeSettledOp(string op)
    {
        return string.Equals(op, "CompareEqInt", StringComparison.Ordinal)
            || string.Equals(op, "CompareEqEntity", StringComparison.Ordinal)
            || string.Equals(op, "SelectEntity", StringComparison.Ordinal);
    }

    private void ProjectHealthInt(GraphOpsNodeDriverContext ctx, int targetIndex)
    {
        if (!string.Equals(ctx.Vignette.Op, "CompareLtInt", StringComparison.Ordinal))
        {
            return;
        }

        Entity target = GraphOpsNodeActorBinding.RequireRole(ctx, "target");
        if (!ctx.SimWorld.Has<BlackboardIntBuffer>(target))
        {
            throw new InvalidOperationException("CompareLtInt gallery target is missing BlackboardIntBuffer.");
        }

        float health = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        ref BlackboardIntBuffer ints = ref ctx.SimWorld.Get<BlackboardIntBuffer>(target);
        ints.Set(_healthIntKey, (int)health);
    }

    private void ResolveStrikeTemplate(GraphOpsNodeDriverContext ctx)
    {
        if (!IsStrikeSettledOp(ctx.Vignette.Op))
        {
            return;
        }

        _strikeTemplateId = EffectTemplateIdRegistry.GetId(StrikeEffectId);
        if (_strikeTemplateId <= 0)
        {
            throw new InvalidOperationException(
                $"Attr gallery requires '{StrikeEffectId}' loaded through EffectTemplateLoader.");
        }

        if (ctx.EffectTemplates == null || !ctx.EffectTemplates.TryGet(_strikeTemplateId, out EffectTemplateData strike))
        {
            throw new InvalidOperationException(
                $"Attr gallery requires '{StrikeEffectId}' in the production EffectTemplateRegistry.");
        }

        _strikeDelta = 0f;
        int healthId = GraphOpsNodeActorBinding.HealthAttributeId();
        EffectModifiers modifiers = strike.Modifiers;
        for (int i = 0; i < modifiers.Count; i++)
        {
            ModifierData modifier = modifiers.Get(i);
            if (modifier.AttributeId == healthId && modifier.Operation == ModifierOp.Add)
            {
                _strikeDelta += modifier.Value;
            }
        }
    }

    private void RequireStrikeRequestOnTarget(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.EffectRequests == null || ctx.EffectRequests.Count != 1)
        {
            throw new InvalidOperationException(
                $"{ctx.Vignette.Op} must settle exactly one Strike request, got {ctx.EffectRequests?.Count ?? 0}.");
        }

        EffectRequest request = ctx.EffectRequests[0];
        if (request.TemplateId != _strikeTemplateId || request.Target != ctx.Target)
        {
            throw new InvalidOperationException($"{ctx.Vignette.Op} Strike request must land on 木桩.");
        }
    }

    private static float MaxHealth(GraphOpsNodeDriverContext ctx, int targetIndex)
    {
        return targetIndex >= 0 && ctx.Vignette.Actors[targetIndex].HealthMax > 0f
            ? ctx.Vignette.Actors[targetIndex].HealthMax
            : 100f;
    }

    /// <summary>The materialized body is bound once as a formal stage actor so the recorder sees a named
    /// body (not a phantom). Headless galleries skip stage binding and only assert the body lives.</summary>
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

    // ── 青色单据环（LoadContextTarget）──

    /// <summary>This strike's own ledger: a cyan ring at the stake's feet that the red line pulls along.</summary>
    private static void DrawLedgerRing(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        GraphShowcaseStagePresenter.DrawThickOutlineCircle(
            debugDraw, targetActor.X, targetActor.Y - 0.25f, 0.8f, GraphShowcaseStagePresenter.OutlineDark, LedgerCyan);
        GraphShowcaseStagePresenter.DrawAggroLine(
            debugDraw, casterActor.X, casterActor.Y, targetActor.X, targetActor.Y);
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, targetActor.X - 1.1f, targetActor.Y + 1.6f, -12, 0.5f, GraphShowcaseStagePresenter.EnemyColor);
    }

    // ── 黄色虚线侦查线（LoadAttribute）──

    /// <summary>Recon line: yellow dashed from the caster's hand to the stake, a large readout floats over its head.</summary>
    private static void DrawReconLine(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        GraphShowcaseStagePresenter.DrawDashedDirectedLine(
            debugDraw,
            casterActor.X,
            casterActor.Y + 0.5f,
            targetActor.X,
            targetActor.Y + 0.4f,
            0.08f,
            GraphShowcaseStagePresenter.CasterColor);
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, targetActor.X, targetActor.Y + 2.8f, (int)ctx.ActorHealth[target], 0.7f, GraphShowcaseStagePresenter.CasterColor);
    }

    // ── 铸数铭牌（ConstInt）──

    /// <summary>Cast plate at stage center: one bright slot with the locked 3; three hollow rings wait over the stake.</summary>
    private static void DrawCastPlate(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        float plateX = (casterActor.X + targetActor.X) * 0.5f;
        float plateY = casterActor.Y + 1.7f;

        GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, plateX, plateY, 1.9f, 0.9f, slots: 1, GraphShowcaseStagePresenter.GateColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, plateX + 0.35f, plateY, 3, 0.6f, GraphShowcaseStagePresenter.CasterColor);
        debugDraw.Boxes.Add(new DebugDrawBox2D
        {
            Center = new Vector2(plateX + 0.55f, plateY + 0.2f),
            HalfWidth = 0.12f,
            HalfHeight = 0.1f,
            Thickness = 0.05f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = new Vector2(plateX + 0.55f, plateY + 0.32f),
            Radius = 0.1f,
            Thickness = 0.04f,
            Color = GraphShowcaseStagePresenter.GateColor
        });

        for (int i = 0; i < 3; i++)
        {
            GraphShowcaseStagePresenter.DrawGhostCircle(
                debugDraw, targetActor.X, targetActor.Y + 2.6f + i * 0.4f, 0.24f, GraphShowcaseStagePresenter.CasterColor);
        }
    }

    // ── 对撞天平（CompareEqInt）──

    /// <summary>Three flame layers meet the full line on a balance; aligned, the thick white strike lands.</summary>
    private static void DrawScaleStrike(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        float scaleX = (casterActor.X + targetActor.X) * 0.5f;
        float scaleY = casterActor.Y + 1.5f;

        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(scaleX, scaleY - 0.7f),
            B = new Vector2(scaleX, scaleY + 0.7f),
            Thickness = 0.12f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(scaleX - 1.1f, scaleY),
            B = new Vector2(scaleX + 1.1f, scaleY),
            Thickness = 0.1f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        for (int side = -1; side <= 1; side += 2)
        {
            float panX = scaleX + side * 1.05f;
            debugDraw.Lines.Add(new DebugDrawLine2D
            {
                A = new Vector2(panX, scaleY - 0.25f),
                B = new Vector2(panX, scaleY - 0.6f),
                Thickness = 0.06f,
                Color = GraphShowcaseStagePresenter.GateColor
            });
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw, panX, scaleY - 0.8f, GraphShowcaseStagePresenter.BadgeKind.Ring, GraphShowcaseStagePresenter.GateColor, scale: 1.1f);
            GraphShowcaseStagePresenter.DrawNumber(
                debugDraw, panX + 0.4f, scaleY - 0.8f, 3, 0.42f, side < 0 ? GraphShowcaseStagePresenter.CasterColor : GraphShowcaseStagePresenter.GuardColor);
        }

        for (int i = 0; i < 3; i++)
        {
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw, targetActor.X, targetActor.Y + 2.4f - i * 0.34f,
                GraphShowcaseStagePresenter.BadgeKind.Flame, GraphShowcaseStagePresenter.CasterColor);
        }

        if (ctx.Wave >= 1)
        {
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw, casterActor.X, casterActor.Y, targetActor.X, targetActor.Y, 0.24f, GraphShowcaseStagePresenter.GateColor);
        }
    }

    // ── 证同章（CompareEqEntity）──

    /// <summary>Self ghost wears the identity seal; the real line leaves it for the stake.</summary>
    private static void DrawIdentitySeal(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        GraphShowcaseStagePresenter.DrawGhostCircle(debugDraw, casterActor.X, casterActor.Y, 0.5f, GraphShowcaseStagePresenter.GhostColor);
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw, casterActor.X, casterActor.Y + 2.2f, GraphShowcaseStagePresenter.BadgeKind.Diamond, GraphShowcaseStagePresenter.GateColor, scale: 1.2f);
        GraphShowcaseStagePresenter.DrawAggroLine(
            debugDraw, casterActor.X, casterActor.Y, targetActor.X, targetActor.Y);
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, targetActor.X - 1.1f, targetActor.Y + 1.6f, -18, 0.5f, GraphShowcaseStagePresenter.EnemyColor);
    }

    // ── 紫色标记卸除（RemoveEffectTemplate）──

    private void DrawMarkShatter(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int target)
    {
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        bool live = TargetHasLiveMark(ctx);
        if (live)
        {
            GraphShowcaseStagePresenter.DrawThickOutlineCircle(
                debugDraw, targetActor.X, targetActor.Y, MarkHaloRadius, GraphShowcaseStagePresenter.OutlineDark, MarkViolet);
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw, targetActor.X, targetActor.Y + MarkBadgeLift, GraphShowcaseStagePresenter.BadgeKind.Diamond, MarkViolet);
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw, targetActor.X, targetActor.Y + MarkBadgeLift, GraphShowcaseStagePresenter.BadgeKind.Ring, MarkViolet, scale: 1.5f);
            return;
        }

        if (!_markWasSeeded)
        {
            return;
        }

        GraphShowcaseStagePresenter.DrawArcArrow(
            debugDraw, targetActor.X, targetActor.Y + MarkBadgeLift, 1.4f, -30f, 30f, GraphShowcaseStagePresenter.GateColor);
        for (int i = 0; i < 3; i++)
        {
            float sx = targetActor.X + (i - 1) * 0.4f;
            float sy = targetActor.Y + MarkBadgeLift - i * 0.16f;
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(sx, sy),
                HalfWidth = 0.14f,
                HalfHeight = 0.14f,
                RotationRadians = MathF.PI / 4f,
                Thickness = 0.05f,
                Color = i == ctx.Wave % 3 ? MarkViolet : GraphShowcaseStagePresenter.GhostColor
            });
        }
    }

    // ── 岔路牌（SelectEntity）──

    /// <summary>Fork sign with two gate flags: the ghost branch fades on the left, the real branch locks the stake.</summary>
    private static void DrawForkGate(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        float forkX = (casterActor.X + targetActor.X) * 0.5f;
        float forkY = casterActor.Y + 0.6f;
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(forkX, forkY - 0.9f),
            B = new Vector2(forkX, forkY + 0.9f),
            Thickness = 0.1f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw, forkX - 0.5f, forkY + 0.7f, GraphShowcaseStagePresenter.BadgeKind.Flag, GraphShowcaseStagePresenter.GhostColor);
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw, forkX + 0.5f, forkY + 0.7f, GraphShowcaseStagePresenter.BadgeKind.Flag, GraphShowcaseStagePresenter.GuardColor);

        GraphShowcaseStagePresenter.DrawGhostCircle(debugDraw, casterActor.X, casterActor.Y + 0.4f, 0.5f, GraphShowcaseStagePresenter.GhostColor);
        GraphShowcaseStagePresenter.DrawAggroLine(
            debugDraw, casterActor.X, casterActor.Y, targetActor.X, targetActor.Y);
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, targetActor.X - 1.1f, targetActor.Y + 1.6f, -18, 0.5f, GraphShowcaseStagePresenter.EnemyColor);
    }

    // ── 伤害浮标（ModifyAttributeAdd）──

    /// <summary>Red -25 rises over the stake; a white residual bar holds where the health was cut.</summary>
    private static void DrawDamageFloat(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int target)
    {
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        float before = ctx.Vignette.Actors[target].Health;
        float after = ctx.ActorHealth[target];
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, targetActor.X, targetActor.Y + 2.6f, (int)(after - before), 0.6f, GraphShowcaseStagePresenter.EnemyColor);
        GraphShowcaseStagePresenter.DrawGhostSegment(
            debugDraw, targetActor.X - 0.5f, targetActor.Y - 0.7f, targetActor.X + 0.5f, targetActor.Y - 0.7f, GraphShowcaseStagePresenter.GateColor);
    }

    // ── 青色自查回环（LoadSelfAttribute）──

    /// <summary>Cyan loop starts and ends on the caster; the read number floats over their head.</summary>
    private static void DrawSelfLoop(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphShowcaseStagePresenter.DrawArcArrow(debugDraw, casterActor.X, casterActor.Y, 0.9f, 160f, 380f, LedgerCyan);
        GraphShowcaseStagePresenter.DrawDashedDirectedLine(
            debugDraw,
            casterActor.X + 0.4f,
            casterActor.Y + 0.8f,
            casterActor.X + 0.55f,
            casterActor.Y - 0.2f,
            0.06f,
            LedgerCyan,
            arrowEnd: false);
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, casterActor.X, casterActor.Y + 2.7f, (int)ctx.ActorHealth[caster], 0.7f, LedgerCyan);
    }

    // ── 金色写入线（WriteSelfAttribute）──

    /// <summary>Gold write line drops onto the caster; green =90 floats; the gained band is picked out in gold.</summary>
    private static void DrawWriteLine(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw, casterActor.X, casterActor.Y + 2.6f, casterActor.X, casterActor.Y + 0.4f, 0.12f, Gold, arrowEnd: false);
        float before = ctx.Vignette.Actors[caster].Health;
        float after = ctx.ActorHealth[caster];
        debugDraw.Boxes.Add(new DebugDrawBox2D
        {
            Center = new Vector2(casterActor.X - 0.55f, casterActor.Y + 2.1f),
            HalfWidth = 0.28f,
            HalfHeight = 0.16f,
            Thickness = 0.06f,
            Color = WriteGreen
        });
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, casterActor.X + 0.25f, casterActor.Y + 2.5f, (int)after, 0.5f, WriteGreen);
        if (after > before)
        {
            GraphShowcaseStagePresenter.DrawGhostSegment(
                debugDraw, casterActor.X - 0.55f, casterActor.Y - 0.8f, casterActor.X + 0.55f, casterActor.Y - 0.8f, Gold);
        }
    }

    // ── 阈值标尺（CompareLtInt）──

    /// <summary>Ruler across the stake's bar with the red 80 line; below it the heavy white strike lands.</summary>
    private static void DrawThresholdRuler(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        float rulerY = targetActor.Y + 1.1f;
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(targetActor.X - 0.8f, rulerY),
            B = new Vector2(targetActor.X + 0.8f, rulerY),
            Thickness = 0.06f,
            Color = GraphShowcaseStagePresenter.GhostColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(targetActor.X - 0.8f, rulerY + 0.15f),
            B = new Vector2(targetActor.X - 0.8f, rulerY - 0.15f),
            Thickness = 0.08f,
            Color = GraphShowcaseStagePresenter.GhostColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(targetActor.X + 0.8f, rulerY + 0.15f),
            B = new Vector2(targetActor.X + 0.8f, rulerY - 0.15f),
            Thickness = 0.08f,
            Color = GraphShowcaseStagePresenter.GhostColor
        });
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, targetActor.X + 1.0f, rulerY, 80, 0.34f, GraphShowcaseStagePresenter.EnemyColor);

        if (ctx.Wave >= 1)
        {
            GraphShowcaseStagePresenter.DrawThickOutlineCircle(
                debugDraw, targetActor.X, targetActor.Y, 1.0f, GraphShowcaseStagePresenter.OutlineDark, GraphShowcaseStagePresenter.GateColor);
            GraphShowcaseStagePresenter.DrawDirectedLine(
                debugDraw, casterActor.X, casterActor.Y, targetActor.X, targetActor.Y, 0.24f, GraphShowcaseStagePresenter.GateColor);
        }
    }

    // ── 白色身份光柱（LoadCaster）──

    /// <summary>White beam on the caster with the gold striker seal; the red line lights from that end.</summary>
    private static void DrawIdentityBeam(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(casterActor.X, casterActor.Y + 2.9f),
            B = new Vector2(casterActor.X, casterActor.Y + 0.4f),
            Thickness = 0.18f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw, casterActor.X, casterActor.Y + 3.4f, GraphShowcaseStagePresenter.BadgeKind.Ring, Gold, scale: 1.3f);
        GraphShowcaseStagePresenter.DrawAggroLine(
            debugDraw, casterActor.X, casterActor.Y, targetActor.X, targetActor.Y);
    }

    // ── 算式台（AddInt）──

    /// <summary>Bench casting 2 + 1, flip card showing 3, and three combo sparks over the stake.</summary>
    private static void DrawComboBench(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        float benchX = (casterActor.X + targetActor.X) * 0.5f;
        float benchY = casterActor.Y + 1.7f;
        GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, benchX, benchY, 2.6f, 0.8f, slots: 3, GraphShowcaseStagePresenter.GateColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, benchX - 0.8f, benchY, 2, 0.5f, CoinCopper);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, benchX + 0.05f, benchY, 1, 0.5f, CoinCopper);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, benchX + 0.9f, benchY, 3, 0.5f, GraphShowcaseStagePresenter.CasterColor);

        for (int i = 0; i < 3; i++)
        {
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw, targetActor.X, targetActor.Y + 2.5f - i * 0.32f,
                GraphShowcaseStagePresenter.BadgeKind.Flame, GraphShowcaseStagePresenter.CasterColor, scale: 0.8f);
        }
    }

    // ── 红色准星（LoadExplicitTarget）──

    /// <summary>Red crosshair brackets close on the stake, then the red line hits along them.</summary>
    private static void DrawCrosshairLock(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        const float half = 0.65f;
        float tx = targetActor.X;
        float ty = targetActor.Y;
        debugDraw.Lines.Add(new DebugDrawLine2D { A = new Vector2(tx - half - 0.2f, ty - half), B = new Vector2(tx - 0.15f, ty - half), Thickness = 0.1f, Color = GraphShowcaseStagePresenter.EnemyColor });
        debugDraw.Lines.Add(new DebugDrawLine2D { A = new Vector2(tx + 0.15f, ty - half), B = new Vector2(tx + half + 0.2f, ty - half), Thickness = 0.1f, Color = GraphShowcaseStagePresenter.EnemyColor });
        debugDraw.Lines.Add(new DebugDrawLine2D { A = new Vector2(tx - half - 0.2f, ty + half), B = new Vector2(tx - 0.15f, ty + half), Thickness = 0.1f, Color = GraphShowcaseStagePresenter.EnemyColor });
        debugDraw.Lines.Add(new DebugDrawLine2D { A = new Vector2(tx + 0.15f, ty + half), B = new Vector2(tx + half + 0.2f, ty + half), Thickness = 0.1f, Color = GraphShowcaseStagePresenter.EnemyColor });
        GraphShowcaseStagePresenter.DrawAggroLine(
            debugDraw, casterActor.X, casterActor.Y, targetActor.X, targetActor.Y);
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, targetActor.X - 1.1f, targetActor.Y + 1.6f, -15, 0.5f, GraphShowcaseStagePresenter.EnemyColor);
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

        _markWasSeeded = true;
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
        ctx.CaptionValues["delta"] = (targetAfter - targetBefore).ToString("0");
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

        if (string.Equals(op, "ModifyAttributeSet", StringComparison.Ordinal) &&
            MathF.Abs(targetAfter - 42f) > 0.01f)
        {
            throw new InvalidOperationException($"ModifyAttributeSet gallery expected target health 42, got {targetAfter}.");
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
