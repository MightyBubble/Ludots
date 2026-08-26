using System;
using System.Threading.Tasks;
using CapabilityStandardPresenterTrailMeshShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardPresenterTrailMeshShowcaseMod;

public sealed class CapabilityStandardPresenterTrailMeshShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardPresenterTrailMeshShowcaseMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.GetEngine()
                ?? throw new InvalidOperationException("CapabilityStandardPresenterTrailMeshShowcaseMod requires GameEngine.");

            engine.RegisterSystem(
                new CapabilityStandardPresenterTrailMeshShowcaseSystem(engine),
                SystemGroup.InputCollection);

            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
