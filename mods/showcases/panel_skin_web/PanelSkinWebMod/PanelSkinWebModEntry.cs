using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiShowcaseCoreMod.Showcase;

namespace PanelSkinWebMod;

public sealed class PanelSkinWebModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[PanelSkinWebMod] Loaded — Web UI skin");
        context.OnEvent(GameEvents.MapLoaded, ctx =>
        {
            try
            {
                FireballPanelShowcaseMounting.InstallSkinSurface(
                    ctx,
                    ownerId: "web-panel",
                    skinLabel: "Web UI",
                    accentR: 255,
                    accentG: 152,
                    accentB: 0);
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
