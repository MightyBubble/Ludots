using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using MassNavigationPresentationAdapter;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;

namespace CapabilityStandardMassNavigationLargeWorld10kMod;

public sealed class CapabilityStandardMassNavigationLargeWorld10kModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardMassNavigationLargeWorld10kMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ConfigureLargeWorldShowcaseAsync);
        context.OnEvent(GameEvents.MapLoaded, ConfigureLargeWorldShowcaseAsync);
        context.OnEvent(GameEvents.MapResumed, ConfigureLargeWorldShowcaseAsync);
    }

    public void OnUnload()
    {
    }

    private Task ConfigureLargeWorldShowcaseAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        EnsureMassNavigationPresentationAdapter(engine);
        bool mapFocused = CapabilityStandardMassNavigationLargeWorld10kMapFocus.IsStartupMapFocused(engine);
        engine.SetService(CoreServiceKeys.PresentationAudienceRevealHidden, mapFocused);
        if (!mapFocused)
        {
            return Task.CompletedTask;
        }

        if (engine.GetService(CoreServiceKeys.MinimapRuntime) is not MinimapRuntime runtime)
        {
            return Task.CompletedTask;
        }

        runtime.Visible = true;
        runtime.SetRotateWithCamera(false);
        runtime.UseRtsFullMapPreset();
        return Task.CompletedTask;
    }

    private static void EnsureMassNavigationPresentationAdapter(GameEngine engine)
    {
        MassNavigationPresentationAdapterInstaller.EnsureLocalObserverDisclosure(engine);
        MassNavigationPresentationAdapterInstaller.EnsureLocomotionAnimatorParams(engine);
    }
}
