using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiShowcaseCoreMod.Showcase;

namespace PanelSkinComposeMod;

public sealed class PanelSkinComposeModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[PanelSkinComposeMod] Loaded — Compose skin");
        context.OnEvent(GameEvents.MapLoaded, ctx =>
        {
            try
            {
                FireballPanelShowcaseMounting.InstallSkinSurface(
                    ctx,
                    ownerId: "compose-panel",
                    skinLabel: "Compose",
                    accentR: 76,
                    accentG: 175,
                    accentB: 80);
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
