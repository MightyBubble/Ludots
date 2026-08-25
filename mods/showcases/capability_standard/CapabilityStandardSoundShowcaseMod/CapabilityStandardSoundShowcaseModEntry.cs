using System;
using System.Threading.Tasks;
using CapabilityStandardSoundShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardSoundShowcaseMod;

public sealed class CapabilityStandardSoundShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardSoundShowcaseMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.GetEngine()
                ?? throw new InvalidOperationException("CapabilityStandardSoundShowcaseMod requires GameEngine.");

            engine.RegisterSystem(
                new CapabilityStandardSoundShowcaseSystem(engine),
                SystemGroup.InputCollection);

            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
