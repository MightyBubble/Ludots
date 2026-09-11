using System;
using System.Threading.Tasks;
using CapabilityStandardPresenterActivationConditionShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardPresenterActivationConditionShowcaseMod;

public sealed class CapabilityStandardPresenterActivationConditionShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardPresenterActivationConditionShowcaseMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.GetEngine()
                ?? throw new InvalidOperationException("CapabilityStandardPresenterActivationConditionShowcaseMod requires GameEngine.");

            engine.RegisterSystem(
                new CapabilityStandardPresenterActivationConditionShowcaseSystem(engine),
                SystemGroup.InputCollection);

            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
