using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiShowcaseCoreMod.Showcase;

namespace PanelSkinMarkupMod;

public sealed class PanelSkinMarkupModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[PanelSkinMarkupMod] Loaded");
        context.OnEvent(GameEvents.MapLoaded, ctx =>
        {
            try
            {
                FireballPanelShowcaseMounting.InstallSkinSurface(
                    ctx,
                    ownerId: "markup-panel",
                    skinLabel: "Markup",
                    accentR: 68,
                    accentG: 136,
                    accentB: 204);
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
