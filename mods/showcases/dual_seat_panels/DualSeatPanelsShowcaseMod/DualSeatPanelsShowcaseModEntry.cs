using System;
using System.Threading.Tasks;
using Arch.Core;
using DualSeatPanelsShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace DualSeatPanelsShowcaseMod;

/// <summary>
/// Dual-seat panel showcase entry (#1315): panels are template panels created by the
/// map's MapLoaded trigger graphs (zero mod code); this mod contributes the seat-attributed
/// operation layer (per-seat hotkeys → FireFromSeat admission → custom events) and the
/// guidance/admission-feedback strip. The admitted boost settles here on the event bus —
/// TriggerGraphs cannot author attribute writes, so the showcase's effect layer is this
/// handler going through <see cref="AttributeMutationOps"/> (attribute write authority),
/// the same shape as the night-raid kill tool.
/// </summary>
public sealed class DualSeatPanelsShowcaseModEntry : IMod
{
    private const string BoostTargetKey = "DSP.BoostTarget";
    private const string BoostAmountKey = "DSP.Amount";

    public void OnLoad(IModContext context)
    {
        context.Log("[DualSeatPanelsShowcaseMod] Loaded - dual-seat per-seat panel showcase");
        var feedback = new DualSeatPanelsFeedback();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine)
            {
                throw new InvalidOperationException("DualSeatPanelsShowcaseMod requires GameEngine on GameStart.");
            }

            engine.RegisterSystem(
                new DualSeatPanelEventSystem(engine, feedback),
                SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new DualSeatPanelsHudSystem(engine, feedback));
            return Task.CompletedTask;
        });

        context.OnEvent(new EventKey(DualSeatPanelsShowcaseIds.BoostUsedEvent), ctx =>
        {
            SettleBoost(ctx);
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }

    private static void SettleBoost(ScriptContext ctx)
    {
        if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine ||
            !DualSeatPanelsShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        if (!ctx.Contains(BoostTargetKey) || !ctx.Contains(BoostAmountKey))
        {
            return;
        }

        Entity target = ctx.Get<Entity>(BoostTargetKey);
        float amount = ctx.Get<float>(BoostAmountKey);
        if (target == Entity.Null || !engine.World.IsAlive(target))
        {
            return;
        }

        var tagOps = ctx.Get(CoreServiceKeys.TagOps);
        if (tagOps == null)
        {
            throw new InvalidOperationException("DualSeatPanelsShowcaseMod requires TagOps for attribute settlement.");
        }

        int healthId = AttributeRegistry.RequireId("Health");
        AttributeMutationOps.AddCurrent(engine.World, target, healthId, amount, tagOps);
    }
}
