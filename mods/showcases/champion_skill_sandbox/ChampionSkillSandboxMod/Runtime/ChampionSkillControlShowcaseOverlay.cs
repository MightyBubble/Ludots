using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace ChampionSkillSandboxMod.Runtime
{
    internal sealed partial class ChampionSkillSandboxRuntime
    {
        private void SyncControlShowcaseOverlay(GameEngine engine)
        {
            if (!ChampionSkillSandboxIds.IsControlMap(engine.CurrentMapSession?.MapId.Value))
            {
                return;
            }

            ScreenOverlayBuffer? overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            if (overlay == null)
            {
                return;
            }

            Entity selected = SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity current)
                ? current
                : ResolveChampionEntity(engine, ChampionSkillSandboxIds.ControlHeroName);
            if (selected == Entity.Null || !engine.World.IsAlive(selected))
            {
                return;
            }

            TagOps? tagOps = engine.GetService(CoreServiceKeys.TagOps);
            int x = 20;
            int y = 320;
            overlay.AddRect(x, y, 560, 228, SelectionPanelFill, SelectionPanelBorder, stableId: 43100, dirtySerial: 1);
            overlay.AddText(x + 16, y + 26, "Control Showcase", 20, SelectionPanelTitle, stableId: 43101, dirtySerial: 1);
            overlay.AddText(x + 16, y + 54, "Q slow | W silence | E root | R stun | select runner/caster to inspect status propagation", 13, SelectionPanelHint, stableId: 43102, dirtySerial: 1);
            overlay.AddText(x + 16, y + 78, $"Selected {ResolveEntityLabel(engine.World, selected) ?? $"Entity#{selected.Id}"}", 15, SelectionPanelText, stableId: 43103, dirtySerial: 1);

            string tags = BuildControlTagSummary(engine.World, selected, tagOps);
            overlay.AddText(x + 16, y + 102, $"Tags {tags}", 13, SelectionPanelText, stableId: 43104, dirtySerial: 1);
            overlay.AddText(x + 16, y + 126, BuildMoveSpeedSummary(engine.World, selected), 13, SelectionPanelText, stableId: 43105, dirtySerial: 1);
            overlay.AddText(x + 16, y + 150, BuildControlStateSummary(engine.World, selected, tagOps), 13, SelectionPanelText, stableId: 43106, dirtySerial: 1);
            overlay.AddText(x + 16, y + 174, BuildOrderAndExecSummary(engine.World, selected), 13, SelectionPanelText, stableId: 43107, dirtySerial: 1);
            overlay.AddText(x + 16, y + 198, "Runner auto-loops between lanes. Caster auto-casts a 20-tick spell that silence blocks and stun interrupts.", 12, SelectionPanelHint, stableId: 43108, dirtySerial: 1);
        }

        private static string BuildControlTagSummary(World world, Entity entity, TagOps? tagOps)
        {
            if (!world.TryGet(entity, out GameplayTagContainer tags))
            {
                return "(none)";
            }

            var active = new List<string>(6);
            AppendIfActive(active, tagOps, ref tags, "Status.Slowed", "Slowed");
            AppendIfActive(active, tagOps, ref tags, "Status.Silenced", "Silenced");
            AppendIfActive(active, tagOps, ref tags, "Status.Rooted", "Rooted");
            AppendIfActive(active, tagOps, ref tags, "Status.Stunned", "Stunned");
            AppendIfActive(active, tagOps, ref tags, "Status.CannotMove", "CannotMove");
            AppendIfActive(active, tagOps, ref tags, "Status.CannotCast", "CannotCast");
            return active.Count == 0 ? "(none)" : string.Join(", ", active);
        }

        private static void AppendIfActive(List<string> destination, TagOps? tagOps, ref GameplayTagContainer tags, string tagName, string label)
        {
            int tagId = TagRegistry.GetId(tagName);
            if (tagId <= 0)
            {
                return;
            }

            bool active = tagOps != null
                ? tagOps.HasTag(ref tags, tagId, TagSense.Effective)
                : tags.HasTag(tagId);
            if (active)
            {
                destination.Add(label);
            }
        }

        private static string BuildMoveSpeedSummary(World world, Entity entity)
        {
            int moveSpeedId = AttributeRegistry.Register("MoveSpeed");
            if (!world.TryGet(entity, out AttributeBuffer attributes))
            {
                return "MoveSpeed n/a";
            }

            float current = attributes.GetCurrent(moveSpeedId);
            float baseValue = attributes.GetBase(moveSpeedId);
            return $"MoveSpeed current={current:0.#} base={baseValue:0.#}";
        }

        private static string BuildControlStateSummary(World world, Entity entity, TagOps? tagOps)
        {
            GameplayControlState state = GameplayControlStateResolver.GetOrDefault(world, entity);
            bool cannotCast = false;
            if (world.TryGet(entity, out GameplayTagContainer tags))
            {
                int cannotCastTagId = TagRegistry.GetId("Status.CannotCast");
                if (cannotCastTagId > 0)
                {
                    cannotCast = tagOps != null
                        ? tagOps.HasTag(ref tags, cannotCastTagId, TagSense.Effective)
                        : tags.HasTag(cannotCastTagId);
                }
            }

            return $"Control moveBlocked={state.IsMoveBlocked()} actionBlocked={state.ActionBlocked != 0} cannotCast={cannotCast}";
        }

        private static string BuildOrderAndExecSummary(World world, Entity entity)
        {
            string orderText = "Order idle";
            if (world.TryGet(entity, out OrderBuffer orders))
            {
                if (orders.HasActive)
                {
                    orderText = $"Order active={orders.ActiveOrder.Order.OrderTypeId}";
                }
                else if (orders.HasPending)
                {
                    orderText = $"Order pending={orders.PendingOrder.Order.OrderTypeId}";
                }
                else if (orders.HasQueued)
                {
                    orderText = $"Order queued={orders.QueuedCount}";
                }
            }

            string execText = "Exec idle";
            if (world.TryGet(entity, out AbilityExecInstance exec))
            {
                execText = $"Exec slot={exec.AbilitySlot} state={exec.State}";
            }

            return $"{orderText} | {execText}";
        }
    }
}
