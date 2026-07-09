using System;
using System.Threading.Tasks;
using CapabilityStandardVirtualCameraShowcaseMod.Runtime;
using CapabilityStandardVirtualCameraShowcaseMod.Systems;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardVirtualCameraShowcaseMod;

public sealed class CapabilityStandardVirtualCameraShowcaseModEntry : IMod
{
    private readonly CapabilityStandardVirtualCameraShowcaseRuntime _runtime = new();

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardVirtualCameraShowcaseMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.GetEngine()
                ?? throw new InvalidOperationException("CapabilityStandardVirtualCameraShowcaseMod requires GameEngine.");

            ViewModeRegistrar.RegisterFromVfs(
                context,
                engine.GlobalContext,
                sourceModId: context.ModId,
                activateWhenUnset: false);

            engine.RegisterSystem(
                new CapabilityStandardVirtualCameraAvatarMoveSystem(engine),
                SystemGroup.InputCollection);

            return Task.CompletedTask;
        });

        context.OnEvent(GameEvents.MapLoaded, _runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, _runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, _runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
