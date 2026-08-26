using System;
using System.Threading.Tasks;
using CapabilityStandardPresenterMaterialBehaviorShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardPresenterMaterialBehaviorShowcaseMod;

public sealed class CapabilityStandardPresenterMaterialBehaviorShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardPresenterMaterialBehaviorShowcaseMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.GetEngine()
                ?? throw new InvalidOperationException("CapabilityStandardPresenterMaterialBehaviorShowcaseMod requires GameEngine.");

            engine.RegisterSystem(
                new CapabilityStandardPresenterMaterialBehaviorShowcaseSystem(engine),
                SystemGroup.InputCollection);

            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
