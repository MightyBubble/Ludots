using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiShowcaseCoreMod.Showcase;

namespace PanelSkinReactiveMod;

public sealed class PanelSkinReactiveModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[PanelSkinReactiveMod] Loaded — Reactive skin");
        context.OnEvent(GameEvents.MapLoaded, ctx =>
        {
            try
            {
                FireballPanelShowcaseMounting.InstallSkinSurface(
                    ctx,
                    ownerId: "reactive-panel",
                    skinLabel: "Reactive",
                    accentR: 156,
                    accentG: 39,
                    accentB: 176);
                return Task.CompletedTask;
            }
            catch (System.Exception ex)
            {
                return Task.FromException(ex);
            }
        });
    }

    public void OnUnload() { }
}
