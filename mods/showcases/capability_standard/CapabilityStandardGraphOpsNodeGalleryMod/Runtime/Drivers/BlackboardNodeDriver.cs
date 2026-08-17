using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;


namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class BlackboardNodeDriver : IGraphOpsNodeDriver
{
    public const string PowerKey = "showcase.bb.power";
    public const string StacksKey = "showcase.bb.stacks";
    public const string NamedKey = "showcase.bb.named";
    public const string StrikeEffectId = "Effect.GraphOps.Strike";
    public const float SeedPower = 35f;
    public const int SeedStacks = 4;

    private const string ConfigPowerKey = "showcase.config.power";
    private const string ConfigTierKey = "showcase.config.tier";
    private const string ConfigChainEffectKey = "showcase.config.chainEffect";

    private static readonly DebugDrawColor NamedColor = DebugDrawColor.Cyan;
    private static readonly DebugDrawColor TicketColor = DebugDrawColor.Red;
    private static readonly DebugDrawColor BoardFrameColor = DebugDrawColor.White;
    private static readonly DebugDrawColor EnvelopeColor = DebugDrawColor.White;
    private static readonly DebugDrawColor SealColor = DebugDrawColor.Red;

    private int _powerKey;
    private int _stacksKey;
    private int _namedKey;
    private int _configPowerKey;
    private int _configTierKey;
    private int _configChainEffectKey;
    private bool _seeded;
    private float _writtenPower;
    private bool _powerWriteVerified;
    private float _configPower;
    private int _configTier;
    private int _configChainEffectId;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        if (!_seeded)
        {
            GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
            for (int i = 0; i < actors.Length; i++)
            {
                BindRole(ctx, actors[i].Role, ctx.SimActors[i]);
            }

            _powerKey = ConfigKeyRegistry.Register(PowerKey);
            _stacksKey = ConfigKeyRegistry.Register(StacksKey);
            _namedKey = ConfigKeyRegistry.Register(NamedKey);
            _configPowerKey = ConfigKeyRegistry.Register(ConfigPowerKey);
            _configTierKey = ConfigKeyRegistry.Register(ConfigTierKey);
            _configChainEffectKey = ConfigKeyRegistry.Register(ConfigChainEffectKey);
            ReadConfigParams(ctx);
            SeedBlackboard(ctx);
            _seeded = true;
        }

        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        int targetIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        float healthBefore = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        GraphOpsNodeExecuteResult result = ctx.ExecuteFeaturedGraph();
        GraphOpsNodeActorBinding.SyncActorHealthFromWorld(ctx);

        float healthAfter = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        ApplyVisibleResult(ctx, result, targetIndex, healthBefore, ref healthAfter);

        if (ctx.Vignette.DetailTemplate.Contains("{result}", StringComparison.Ordinal))
        {
            ctx.CaptionValues["result"] = FormatFeaturedResult(ctx, result);
        }

        ctx.CaptionValues["healthBefore"] = healthBefore.ToString("0");
        ctx.CaptionValues["healthAfter"] = healthAfter.ToString("0");
        ctx.CaptionValues["named"] = ResolveNamed(ctx, result);
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
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

        switch (ctx.Vignette.Op)
        {
            case "WriteBlackboardFloat":
                DrawPowerBoard(ctx, debugDraw, caster);
                return;
            case "ReadBlackboardFloat":
                DrawMemoBoard(ctx, debugDraw, caster, MemoSlot.Power, ctx.Wave, showReadbackCheck: false);
                DrawFlyToTarget(ctx, debugDraw, caster, target, MemoSlot.Power, GraphShowcaseStagePresenter.EnemyColor);
                DrawBoardDamageFloat(ctx, debugDraw, target);
                return;
            case "ReadBlackboardInt":
                DrawMemoBoard(ctx, debugDraw, caster, MemoSlot.Stacks, ctx.Wave, showReadbackCheck: false);
                DrawStackStamps(ctx, debugDraw, caster, target);
                return;
            case "ReadBlackboardEntity":
                DrawMemoBoard(ctx, debugDraw, caster, MemoSlot.Named, ctx.Wave, showReadbackCheck: false);
                DrawLassoToTarget(ctx, debugDraw, caster, target);
                return;
            case "WriteBlackboardInt":
                DrawMemoBoard(ctx, debugDraw, caster, MemoSlot.Stacks, ctx.Wave, showReadbackCheck: true);
                DrawStacksIntoSlot(ctx, debugDraw, caster);
                return;
            case "WriteBlackboardEntity":
                DrawMemoBoard(ctx, debugDraw, caster, MemoSlot.Named, ctx.Wave, showReadbackCheck: true);
                DrawChipIntoSlot(ctx, debugDraw, caster, target);
                return;
            case "LoadConfigFloat":
                DrawConfigBook(ctx, debugDraw, caster, ConfigRow.Power);
                DrawFlyToTarget(ctx, debugDraw, caster, target, ConfigRow.Power, GraphShowcaseStagePresenter.EnemyColor);
                DrawBookDamageFloat(ctx, debugDraw, target);
                return;
            case "LoadConfigInt":
                DrawConfigBook(ctx, debugDraw, caster, ConfigRow.Tier);
                DrawTierBadge(ctx, debugDraw, caster);
                return;
            case "LoadConfigEffectId":
                DrawConfigBook(ctx, debugDraw, caster, ConfigRow.Ticket);
                DrawTicketFly(ctx, debugDraw, caster);
                DrawTicketDamageFloat(ctx, debugDraw, target);
                return;
            case "LoadContextSource":
                DrawEnvelope(ctx, debugDraw, caster, EnvelopeCard.Gold);
                return;
            case "LoadContextTargetContext":
                DrawEnvelope(ctx, debugDraw, caster, EnvelopeCard.Cyan);
                DrawContextNamed(ctx, debugDraw, caster);
                return;
        }
    }

    /// <summary>
    /// Three-slot memo board at the caster's right hand (power / stacks / named); this op fills only
    /// the power slot, with the written value dashed in from the hand and a check once read-back passes.
    /// </summary>
    private void DrawPowerBoard(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        const float boardWidth = 1.6f;
        const float boardHeight = 2.4f;
        GraphOpsNodeActor actor = ctx.Vignette.Actors[caster];
        float boardX = actor.X + 2.1f;
        float boardY = actor.Y + 1.6f;

        GraphShowcaseStagePresenter.DrawPanelBox(
            debugDraw, boardX, boardY, boardWidth, boardHeight, slots: 3, GraphShowcaseStagePresenter.GateColor);

        float powerSlotCenterY = boardY + boardHeight / 3f;
        GraphShowcaseStagePresenter.DrawDashedDirectedLine(
            debugDraw,
            actor.X + 0.4f,
            actor.Y + 0.6f,
            boardX - boardWidth * 0.5f,
            powerSlotCenterY,
            0.06f,
            GraphShowcaseStagePresenter.CasterColor,
            arrowStart: false);

        if (!_powerWriteVerified)
        {
            return;
        }

        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, boardX + 0.35f, boardY + boardHeight * 0.5f + 0.35f, (int)_writtenPower, 0.5f, GraphShowcaseStagePresenter.CasterColor);
        if (ctx.Wave % 2 == 0)
        {
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new System.Numerics.Vector2(boardX, powerSlotCenterY),
                HalfWidth = boardWidth * 0.5f - 0.1f,
                HalfHeight = boardHeight / 6f - 0.08f,
                Thickness = 0.08f,
                Color = GraphShowcaseStagePresenter.CasterColor
            });
        }

        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw,
            boardX + boardWidth * 0.5f + 0.35f,
            boardY + boardHeight * 0.5f + 0.1f,
            GraphShowcaseStagePresenter.BadgeKind.Check,
            DebugDrawColor.Green);
    }

    // ── 记事板（黑板）──

    private void DrawMemoBoard(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, MemoSlot flashSlot, int wave, bool showReadbackCheck)
    {
        const float boardWidth = 2.0f;
        const float boardHeight = 1.4f;
        GraphOpsNodeActor actor = ctx.Vignette.Actors[caster];
        float bx = actor.X + 1.0f;
        float by = actor.Y + 1.4f;
        float hw = boardWidth * 0.5f;
        float hh = boardHeight * 0.5f;

        GraphShowcaseStagePresenter.DrawPanelBox(
            debugDraw, bx, by, boardWidth, boardHeight, slots: 1, BoardFrameColor);

        float slotWidth = boardWidth / 3f;
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(bx - slotWidth * 0.5f, by - hh),
            B = new Vector2(bx - slotWidth * 0.5f, by + hh),
            Thickness = 0.05f,
            Color = BoardFrameColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(bx + slotWidth * 0.5f, by - hh),
            B = new Vector2(bx + slotWidth * 0.5f, by + hh),
            Thickness = 0.05f,
            Color = BoardFrameColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(bx - 0.45f, by + hh + 0.07f),
            B = new Vector2(bx + 0.45f, by + hh + 0.07f),
            Thickness = 0.12f,
            Color = BoardFrameColor
        });

        if (wave % 2 == 0)
        {
            float flashX = bx + ((int)flashSlot - 1) * slotWidth;
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(flashX, by),
                HalfWidth = slotWidth * 0.5f - 0.08f,
                HalfHeight = hh - 0.08f,
                Thickness = 0.07f,
                Color = GraphShowcaseStagePresenter.GateColor
            });
        }

        DrawSlotContents(ctx, debugDraw, bx, by, slotWidth, hh);
        if (showReadbackCheck && SlotHasValue(ctx, flashSlot))
        {
            float checkX = bx + ((int)flashSlot - 1) * slotWidth + slotWidth * 0.5f - 0.18f;
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw, checkX, by + hh - 0.22f, GraphShowcaseStagePresenter.BadgeKind.Check, DebugDrawColor.Green, scale: 0.8f);
        }
    }

    private bool SlotHasValue(GraphOpsNodeDriverContext ctx, MemoSlot slot)
    {
        return slot switch
        {
            MemoSlot.Power => ctx.Api.TryReadBlackboardFloat(ctx.Caster, _powerKey, out _),
            MemoSlot.Stacks => ctx.Api.TryReadBlackboardInt(ctx.Caster, _stacksKey, out _),
            MemoSlot.Named => ctx.Api.TryReadBlackboardEntity(ctx.Caster, _namedKey, out _),
            _ => false
        };
    }

    private void DrawSlotContents(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, float bx, float by, float slotWidth, float hh)
    {
        float powerX = bx - slotWidth;
        float stacksX = bx;
        float namedX = bx + slotWidth;
        if (ctx.Api.TryReadBlackboardFloat(ctx.Caster, _powerKey, out float power))
        {
            GraphShowcaseStagePresenter.DrawNumber(
                debugDraw, powerX + slotWidth * 0.25f, by, (int)power, 0.5f, GraphShowcaseStagePresenter.EnemyColor);
        }

        if (ctx.Api.TryReadBlackboardInt(ctx.Caster, _stacksKey, out int stacks))
        {
            GraphShowcaseStagePresenter.DrawNumber(
                debugDraw, stacksX + slotWidth * 0.25f, by, stacks, 0.5f, GraphShowcaseStagePresenter.CasterColor);
        }

        if (ctx.Api.TryReadBlackboardEntity(ctx.Caster, _namedKey, out Entity named) && ctx.SimWorld.IsAlive(named))
        {
            DrawStakeChip(debugDraw, namedX + slotWidth * 0.2f, by, NamedColor, scale: 1f);
        }
    }

    private static void DrawStakeChip(DebugDrawCommandBuffer buffer, float x, float y, DebugDrawColor color, float scale)
    {
        float hw = 0.1f * scale;
        float hh = 0.26f * scale;
        buffer.Boxes.Add(new DebugDrawBox2D
        {
            Center = new Vector2(x, y - 0.05f * scale),
            HalfWidth = hw,
            HalfHeight = hh,
            Thickness = 0.05f * scale,
            Color = color
        });
        buffer.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(x - 0.1f * scale, y + hh - 0.04f * scale),
            B = new Vector2(x + 0.1f * scale, y + hh - 0.04f * scale),
            Thickness = 0.06f * scale,
            Color = color
        });
    }

    private static void DrawFlyToTarget(
        GraphOpsNodeDriverContext ctx,
        DebugDrawCommandBuffer debugDraw,
        int caster,
        int target,
        MemoSlot slot,
        DebugDrawColor color)
    {
        if (target < 0)
        {
            return;
        }

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        float bx = casterActor.X + 1.0f + (slot == MemoSlot.Power ? -2.0f / 3f : 0f);
        float by = casterActor.Y + 1.4f;
        GraphShowcaseStagePresenter.DrawDashedDirectedLine(
            debugDraw,
            bx,
            by,
            targetActor.X,
            targetActor.Y + 0.4f,
            0.07f,
            color);
    }

    private static void DrawFlyToTarget(
        GraphOpsNodeDriverContext ctx,
        DebugDrawCommandBuffer debugDraw,
        int caster,
        int target,
        ConfigRow row,
        DebugDrawColor color)
    {
        if (target < 0)
        {
            return;
        }

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        float bx = casterActor.X - 1.2f + 0.9f;
        float by = casterActor.Y + 1.0f + 0.25f;
        GraphShowcaseStagePresenter.DrawDashedDirectedLine(
            debugDraw,
            bx,
            by,
            targetActor.X,
            targetActor.Y + 0.4f,
            0.07f,
            color);
    }

    private void DrawBoardDamageFloat(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int target)
    {
        if (target < 0 || !ctx.Api.TryReadBlackboardFloat(ctx.Caster, _powerKey, out float power))
        {
            return;
        }

        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, targetActor.X + 0.5f, targetActor.Y + 2.5f, -(int)power, 0.45f, GraphShowcaseStagePresenter.EnemyColor);
    }

    private void DrawBookDamageFloat(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int target)
    {
        if (target < 0)
        {
            return;
        }

        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, targetActor.X + 0.5f, targetActor.Y + 2.5f, -(int)_configPower, 0.45f, GraphShowcaseStagePresenter.EnemyColor);
    }

    private void DrawTicketDamageFloat(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int target)
    {
        if (target < 0 || _configChainEffectId <= 0)
        {
            return;
        }

        float delta = ReadTemplateHealthDelta(ctx, _configChainEffectId);
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, targetActor.X + 0.5f, targetActor.Y + 2.5f, (int)delta, 0.45f, GraphShowcaseStagePresenter.EnemyColor);
    }

    /// <summary>The four stack stamps read off the board land as a stack on the target's head.</summary>
    private static void DrawStackStamps(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        if (target < 0)
        {
            return;
        }

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        float sx = casterActor.X + 1.0f;
        float sy = casterActor.Y + 1.4f;
        float tx = targetActor.X;
        float ty = targetActor.Y + 2.4f;
        GraphShowcaseStagePresenter.DrawDashedDirectedLine(
            debugDraw, sx, sy, tx, ty, 0.06f, GraphShowcaseStagePresenter.CasterColor);
        for (int i = 0; i < 4; i++)
        {
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(tx, ty - i * 0.28f),
                HalfWidth = 0.14f,
                HalfHeight = 0.1f,
                Thickness = 0.05f,
                Color = GraphShowcaseStagePresenter.CasterColor
            });
        }
    }

    /// <summary>The named chip launches as a lasso arrow toward the real stake; the stake gets a green lock ring.</summary>
    private static void DrawLassoToTarget(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        if (target < 0)
        {
            return;
        }

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        float sx = casterActor.X + 1.0f + 2.0f / 3f;
        float sy = casterActor.Y + 1.4f;
        GraphShowcaseStagePresenter.DrawDashedDirectedLine(
            debugDraw, sx, sy, targetActor.X, targetActor.Y + 0.5f, 0.08f, NamedColor);
        GraphShowcaseStagePresenter.DrawThickOutlineCircle(
            debugDraw, targetActor.X, targetActor.Y - 0.1f, 0.8f, GraphShowcaseStagePresenter.OutlineDark, GraphShowcaseStagePresenter.GuardColor);
        DrawStakeChip(debugDraw, (sx + targetActor.X) * 0.5f, (sy + targetActor.Y + 0.5f) * 0.5f, NamedColor, scale: 1.4f);
    }

    /// <summary>Write mirror of the stack stamps: four small stamps fly back into the stacks slot.</summary>
    private static void DrawStacksIntoSlot(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        float sx = casterActor.X;
        float sy = casterActor.Y + 0.6f;
        float tx = casterActor.X + 1.0f;
        float ty = casterActor.Y + 1.4f;
        GraphShowcaseStagePresenter.DrawDashedDirectedLine(
            debugDraw, sx, sy, tx, ty, 0.06f, GraphShowcaseStagePresenter.CasterColor);
        for (int i = 0; i < 4; i++)
        {
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(tx + 0.2f, ty - i * 0.2f),
                HalfWidth = 0.12f,
                HalfHeight = 0.08f,
                Thickness = 0.04f,
                Color = GraphShowcaseStagePresenter.CasterColor
            });
        }
    }

    /// <summary>Write mirror of the lasso: a pull-arrow peels the chip off the stake into the named slot.</summary>
    private static void DrawChipIntoSlot(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, int target)
    {
        if (target < 0)
        {
            return;
        }

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor targetActor = ctx.Vignette.Actors[target];
        float sx = targetActor.X;
        float sy = targetActor.Y + 0.5f;
        float tx = casterActor.X + 1.0f + 2.0f / 3f;
        float ty = casterActor.Y + 1.4f;
        GraphShowcaseStagePresenter.DrawDashedDirectedLine(
            debugDraw, sx, sy, tx, ty, 0.08f, NamedColor);
        DrawStakeChip(debugDraw, (sx + tx) * 0.5f, (sy + ty) * 0.5f, NamedColor, scale: 1.4f);
    }

    // ── 配置册 ──

    private enum ConfigRow
    {
        Tier = 0,
        Power = 1,
        Ticket = 2
    }

    private void DrawConfigBook(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, ConfigRow flashRow)
    {
        const float bookWidth = 2.6f;
        const float bookHeight = 1.3f;
        GraphOpsNodeActor actor = ctx.Vignette.Actors[caster];
        float bx = actor.X - 1.2f;
        float by = actor.Y + 1.0f;
        float hw = bookWidth * 0.5f;
        float hh = bookHeight * 0.5f;

        float left = bx - hw;
        float right = bx + hw;
        float top = by + hh;
        float bottom = by - hh;
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(left, top),
            B = new Vector2(right, top),
            Thickness = 0.07f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(left, bottom),
            B = new Vector2(right, bottom),
            Thickness = 0.07f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(left, top),
            B = new Vector2(left, bottom),
            Thickness = 0.07f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(right, top),
            B = new Vector2(right, bottom),
            Thickness = 0.07f,
            Color = GraphShowcaseStagePresenter.GateColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(bx, top),
            B = new Vector2(bx, bottom),
            Thickness = 0.08f,
            Color = GraphShowcaseStagePresenter.GateColor
        });

        // Left page: 品阶 scale row (two tick marks, roman II).
        float tierY = by + 0.2f;
        DrawTierTicks(debugDraw, bx - hw * 0.5f, tierY, GraphShowcaseStagePresenter.CasterColor);
        // Right page: 威力 row + 衔接效果 ticket row.
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw, bx + hw * 0.75f, by + 0.25f, (int)_configPower, 0.45f, GraphShowcaseStagePresenter.EnemyColor);
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw, bx + hw * 0.55f, by - 0.35f, GraphShowcaseStagePresenter.BadgeKind.Flame, TicketColor, scale: 1.1f);

        if (ctx.Wave % 2 == 0)
        {
            float flashCenterY = flashRow == ConfigRow.Tier ? tierY : flashRow == ConfigRow.Power ? by + 0.25f : by - 0.35f;
            float flashLeft = flashRow == ConfigRow.Tier ? left + 0.1f : bx + 0.1f;
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(flashLeft + 0.5f, flashCenterY),
                HalfWidth = 0.65f,
                HalfHeight = 0.22f,
                Thickness = 0.06f,
                Color = GraphShowcaseStagePresenter.GateColor
            });
        }
    }

    private static void DrawTierTicks(DebugDrawCommandBuffer debugDraw, float x, float y, DebugDrawColor color)
    {
        for (int i = 0; i < 2; i++)
        {
            float tx = x + (i - 0.5f) * 0.4f;
            debugDraw.Lines.Add(new DebugDrawLine2D
            {
                A = new Vector2(tx, y + 0.2f),
                B = new Vector2(tx, y - 0.2f),
                Thickness = 0.07f,
                Color = color
            });
        }
    }

    /// <summary>Shield badge over the caster's head whose two rank stars light gray→gold after the tier read.</summary>
    private static void DrawTierBadge(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor actor = ctx.Vignette.Actors[caster];
        float bx = actor.X;
        float by = actor.Y + 2.6f;
        debugDraw.Boxes.Add(new DebugDrawBox2D
        {
            Center = new Vector2(bx, by + 0.05f),
            HalfWidth = 0.42f,
            HalfHeight = 0.5f,
            Thickness = 0.07f,
            Color = GraphShowcaseStagePresenter.CasterColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(bx - 0.42f, by - 0.45f),
            B = new Vector2(bx, by - 0.95f),
            Thickness = 0.07f,
            Color = GraphShowcaseStagePresenter.CasterColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(bx + 0.42f, by - 0.45f),
            B = new Vector2(bx, by - 0.95f),
            Thickness = 0.07f,
            Color = GraphShowcaseStagePresenter.CasterColor
        });
        int litStars = ctx.Wave >= 2 ? 2 : 1;
        for (int i = 0; i < 2; i++)
        {
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw,
                bx + (i - 0.5f) * 0.42f,
                by + 0.05f,
                GraphShowcaseStagePresenter.BadgeKind.Ring,
                i < litStars ? GraphShowcaseStagePresenter.CasterColor : GraphShowcaseStagePresenter.GhostColor,
                scale: 0.55f);
        }
    }

    /// <summary>The ticket tears off the book and flies into the caster's hand before the strike lands.</summary>
    private static void DrawTicketFly(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor actor = ctx.Vignette.Actors[caster];
        float sx = actor.X - 1.2f + 0.8f;
        float sy = actor.Y + 1.0f - 0.35f;
        float tx = actor.X + 0.35f;
        float ty = actor.Y + 0.5f;
        GraphShowcaseStagePresenter.DrawDashedDirectedLine(
            debugDraw, sx, sy, tx, ty, 0.06f, TicketColor);
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw, (sx + tx) * 0.5f, (sy + ty) * 0.5f + 0.3f, GraphShowcaseStagePresenter.BadgeKind.Flame, TicketColor, scale: 0.9f);
    }

    // ── 情境信封 ──

    private enum EnvelopeCard
    {
        Gold = 0,
        Red = 1,
        Cyan = 2
    }

    private void DrawEnvelope(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, EnvelopeCard pulledCard)
    {
        GraphOpsNodeActor actor = ctx.Vignette.Actors[caster];
        float ex = actor.X;
        float ey = actor.Y + 2.4f;
        float hw = 0.9f;
        float hh = 0.55f;

        // Three cards sticking out of the envelope mouth (gold / red / cyan).
        for (int i = 0; i < 3; i++)
        {
            DebugDrawColor cardColor = i switch
            {
                0 => GraphShowcaseStagePresenter.CasterColor,
                1 => GraphShowcaseStagePresenter.EnemyColor,
                _ => NamedColor
            };
            float cx = ex + (i - 1) * 0.42f;
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(cx, ey + hh + 0.22f),
                HalfWidth = 0.14f,
                HalfHeight = 0.2f,
                Thickness = 0.05f,
                Color = cardColor
            });
        }

        debugDraw.Boxes.Add(new DebugDrawBox2D
        {
            Center = new Vector2(ex, ey),
            HalfWidth = hw,
            HalfHeight = hh,
            Thickness = 0.07f,
            Color = EnvelopeColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(ex - hw, ey + hh),
            B = new Vector2(ex, ey + hh * 0.2f),
            Thickness = 0.07f,
            Color = EnvelopeColor
        });
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(ex + hw, ey + hh),
            B = new Vector2(ex, ey + hh * 0.2f),
            Thickness = 0.07f,
            Color = EnvelopeColor
        });
        debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = new Vector2(ex, ey + hh * 0.2f),
            Radius = 0.16f,
            Thickness = 0.08f,
            Color = SealColor
        });

        if (pulledCard == EnvelopeCard.Gold)
        {
            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw, ex - 0.42f, ey + hh + 0.22f, actor.X, actor.Y + 0.6f, 0.06f, GraphShowcaseStagePresenter.CasterColor);
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(actor.X, actor.Y + 0.6f),
                HalfWidth = 0.22f,
                HalfHeight = 0.22f,
                Thickness = 0.06f,
                Color = GraphShowcaseStagePresenter.CasterColor
            });
        }
    }

    /// <summary>The extra person is named: caster pulls a cyan arc to the context actor and locks their feet.</summary>
    private void DrawContextNamed(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        int contextIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "context");
        if (contextIndex < 0)
        {
            return;
        }

        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphOpsNodeActor contextActor = ctx.Vignette.Actors[contextIndex];
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            casterActor.X,
            casterActor.Y + 0.5f,
            contextActor.X,
            contextActor.Y + 0.4f,
            0.09f,
            NamedColor);
        GraphShowcaseStagePresenter.DrawThickOutlineCircle(
            debugDraw, contextActor.X, contextActor.Y - 0.1f, 0.8f, GraphShowcaseStagePresenter.OutlineDark, NamedColor);
    }

    // ── 状态 ──

    private void SeedBlackboard(GraphOpsNodeDriverContext ctx)
    {
        ref BlackboardFloatBuffer floats = ref ctx.SimWorld.Get<BlackboardFloatBuffer>(ctx.Caster);
        ref BlackboardIntBuffer ints = ref ctx.SimWorld.Get<BlackboardIntBuffer>(ctx.Caster);
        ref BlackboardEntityBuffer entities = ref ctx.SimWorld.Get<BlackboardEntityBuffer>(ctx.Caster);

        if (ctx.Vignette.Op is "ReadBlackboardFloat")
        {
            floats.Set(_powerKey, SeedPower);
        }
        else if (ctx.Vignette.Op is "ReadBlackboardInt")
        {
            ints.Set(_stacksKey, SeedStacks);
        }
        else if (ctx.Vignette.Op is "ReadBlackboardEntity")
        {
            ctx.Target = GraphOpsNodeActorBinding.RequireRole(ctx, "target");
            entities.Set(_namedKey, ctx.Target);
        }
    }

    private void ReadConfigParams(GraphOpsNodeDriverContext ctx)
    {
        if (!ctx.EffectTemplates!.TryGet(ctx.ConfigEffectTemplateId, out EffectTemplateData template))
        {
            throw new InvalidOperationException(
                $"Gallery '{ctx.Vignette.Op}' config effect {ctx.ConfigEffectTemplateId} is not registered.");
        }

        _configPower = template.ConfigParams.TryGetFloat(_configPowerKey, out float power) ? power : 0f;
        _configTier = template.ConfigParams.TryGetInt(_configTierKey, out int tier) ? tier : 0;
        _configChainEffectId = template.ConfigParams.TryGetInt(_configChainEffectKey, out int chain) ? chain : 0;
        int strikeId = EffectTemplateIdRegistry.GetId(StrikeEffectId);
        if (strikeId > 0 && _configChainEffectId != strikeId)
        {
            throw new InvalidOperationException(
                $"Gallery '{ctx.Vignette.Op}' config chainEffect must point at {StrikeEffectId}, got {_configChainEffectId}.");
        }
    }

    private void ApplyVisibleResult(
        GraphOpsNodeDriverContext ctx,
        GraphOpsNodeExecuteResult result,
        int targetIndex,
        float healthBefore,
        ref float healthAfter)
    {
        switch (ctx.Vignette.Op)
        {
            case "ReadBlackboardFloat":
                RequireNonZeroFloat(ctx, result.FloatValue, "威力");
                break;
            case "ReadBlackboardInt":
                if (result.IntValue != SeedStacks)
                {
                    throw new InvalidOperationException($"ReadBlackboardInt expected {SeedStacks} stacks, got {result.IntValue}.");
                }

                break;
            case "ReadBlackboardEntity":
                if (result.EntityValue != ctx.Target)
                {
                    throw new InvalidOperationException("ReadBlackboardEntity did not lock the seeded target.");
                }

                break;
            case "WriteBlackboardFloat":
                float writtenPower = ReadFloat(ctx, ctx.Caster, _powerKey);
                RequireNonZeroFloat(ctx, writtenPower, "记下威力");
                _writtenPower = writtenPower;
                _powerWriteVerified = true;
                break;
            case "WriteBlackboardInt":
                int writtenStacks = ReadInt(ctx, ctx.Caster, _stacksKey);
                if (writtenStacks != SeedStacks)
                {
                    throw new InvalidOperationException($"WriteBlackboardInt expected {SeedStacks}, got {writtenStacks}.");
                }

                break;
            case "WriteBlackboardEntity":
                if (!ctx.Api.TryReadBlackboardEntity(ctx.Caster, _namedKey, out Entity named) || named != ctx.Target)
                {
                    throw new InvalidOperationException("WriteBlackboardEntity did not store the target on the board.");
                }

                break;
            case "LoadConfigFloat":
                RequireNonZeroFloat(ctx, result.FloatValue, "配置威力");
                break;
            case "LoadConfigInt":
                if (result.IntValue != _configTier)
                {
                    throw new InvalidOperationException(
                        $"LoadConfigInt expected 品阶 {_configTier}, got {result.IntValue}.");
                }

                break;
            case "LoadConfigEffectId":
                ApplyConfigEffectTicket(ctx, result, targetIndex, healthBefore, ref healthAfter);
                break;
            case "LoadContextSource":
                if (result.EntityValue != ctx.Caster)
                {
                    throw new InvalidOperationException("LoadContextSource did not return the caster.");
                }

                break;
            case "LoadContextTargetContext":
                if (ctx.TargetContext == Entity.Null || result.EntityValue != ctx.TargetContext)
                {
                    throw new InvalidOperationException("LoadContextTargetContext did not return the seeded context actor.");
                }

                break;
        }
    }

    /// <summary>
    /// The config effect ticket must be a real effect id wired through ApplyEffectDynamic: exactly one
    /// request for the ticket's template lands on the target, and no ConstFloat stand-in is allowed.
    /// </summary>
    private void ApplyConfigEffectTicket(
        GraphOpsNodeDriverContext ctx,
        GraphOpsNodeExecuteResult result,
        int targetIndex,
        float healthBefore,
        ref float healthAfter)
    {
        int ticketId = result.IntValue;
        if (ticketId <= 0)
        {
            throw new InvalidOperationException("LoadConfigEffectId returned a dangling zero effect id.");
        }

        if (ticketId != _configChainEffectId)
        {
            throw new InvalidOperationException(
                $"LoadConfigEffectId returned effect {ticketId}, config chainEffect is {_configChainEffectId}.");
        }

        if (ctx.EffectRequests == null || ctx.EffectRequests.Count != 1)
        {
            throw new InvalidOperationException(
                $"LoadConfigEffectId must settle exactly one effect request, got {ctx.EffectRequests?.Count ?? 0}.");
        }

        EffectRequest request = ctx.EffectRequests[0];
        if (request.TemplateId != ticketId || request.Target != ctx.Target)
        {
            throw new InvalidOperationException("LoadConfigEffectId ticket did not apply to the target.");
        }

        if (ProgramHasOp(ctx.Compiled.Program, GraphNodeOp.ConstFloat))
        {
            throw new InvalidOperationException("LoadConfigEffectId graph must not carry a ConstFloat stand-in.");
        }

        float delta = ReadTemplateHealthDelta(ctx, ticketId);
        if (targetIndex < 0)
        {
            throw new InvalidOperationException($"LoadConfigEffectId requires a target actor for health visibility.");
        }

        healthAfter = Math.Clamp(healthBefore + delta, 0f, ctx.Vignette.Actors[targetIndex].HealthMax);
    }

    /// <summary>Sum of the ticket template's additive Health modifiers — the number the queue settles.</summary>
    private static float ReadTemplateHealthDelta(GraphOpsNodeDriverContext ctx, int templateId)
    {
        if (!ctx.EffectTemplates!.TryGet(templateId, out EffectTemplateData template))
        {
            throw new InvalidOperationException($"LoadConfigEffectId ticket template {templateId} is not registered.");
        }

        float delta = 0f;
        int healthId = GraphOpsNodeActorBinding.HealthAttributeId();
        EffectModifiers modifiers = template.Modifiers;
        for (int i = 0; i < modifiers.Count; i++)
        {
            ModifierData modifier = modifiers.Get(i);
            if (modifier.AttributeId == healthId && modifier.Operation == ModifierOp.Add)
            {
                delta += modifier.Value;
            }
        }

        return delta;
    }


    private static bool ProgramHasOp(GraphInstruction[] program, GraphNodeOp op)
    {
        for (int i = 0; i < program.Length; i++)
        {
            if (program[i].Op == (ushort)op)
            {
                return true;
            }
        }

        return false;
    }

    private string FormatFeaturedResult(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        return ctx.Vignette.Op switch
        {
            "ReadBlackboardFloat" or "LoadConfigFloat" => result.FloatValue.ToString("0.#"),
            "WriteBlackboardFloat" => ReadFloat(ctx, ctx.Caster, _powerKey).ToString("0.#"),
            "ReadBlackboardInt" or "LoadConfigInt" => result.IntValue.ToString(),
            "WriteBlackboardInt" => ReadInt(ctx, ctx.Caster, _stacksKey).ToString(),
            _ => throw new InvalidOperationException(
                $"Blackboard gallery '{ctx.Vignette.Op}' has no featured result formatter.")
        };
    }

    private static string ResolveNamed(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        Entity named = result.EntityValue;
        if (named == Entity.Null)
        {
            named = ctx.TargetContext != Entity.Null ? ctx.TargetContext : ctx.Target;
        }

        int index = GraphOpsNodeActorBinding.IndexOf(ctx, named);
        return index >= 0 ? ctx.Vignette.Actors[index].Name : "木桩";
    }

    private static void BindRole(GraphOpsNodeDriverContext ctx, string role, Entity entity)
    {
        if (string.Equals(role, "caster", StringComparison.Ordinal))
        {
            ctx.Caster = entity;
        }
        else if (string.Equals(role, "target", StringComparison.Ordinal))
        {
            ctx.Target = entity;
        }
        else if (string.Equals(role, "context", StringComparison.Ordinal))
        {
            ctx.TargetContext = entity;
        }
    }

    private static float ReadFloat(GraphOpsNodeDriverContext ctx, Entity entity, int key)
    {
        if (!ctx.Api.TryReadBlackboardFloat(entity, key, out float value))
        {
            throw new InvalidOperationException(
                $"Gallery '{ctx.Vignette.Op}' blackboard float key {key} is missing on entity {entity.Id}.");
        }

        return value;
    }

    private static int ReadInt(GraphOpsNodeDriverContext ctx, Entity entity, int key)
    {
        if (!ctx.Api.TryReadBlackboardInt(entity, key, out int value))
        {
            throw new InvalidOperationException(
                $"Gallery '{ctx.Vignette.Op}' blackboard int key {key} is missing on entity {entity.Id}.");
        }

        return value;
    }

    private static void RequireNonZeroFloat(GraphOpsNodeDriverContext ctx, float value, string label)
    {
        if (value == 0f)
        {
            throw new InvalidOperationException($"{ctx.Vignette.Op} returned 0 for {label}; missing config or unpatched blackboard key.");
        }
    }

    private enum MemoSlot
    {
        Power = 0,
        Stacks = 1,
        Named = 2
    }
}
