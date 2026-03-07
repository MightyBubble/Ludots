using Ludots.Core.Modding;
using Ludots.UI.Runtime;
using UiShowcaseCoreMod.Showcase;

namespace UiSkinSciFiHudMod;

public sealed class UiSkinSciFiHudModEntry : IMod
{
    public static UiThemePack Theme => UiSkinThemes.SciFiHud;

    public void OnLoad(IModContext context)
    {
        context.Log("[UiSkinSciFiHudMod] Skin loaded.");
    }

    public void OnUnload() { }
}
