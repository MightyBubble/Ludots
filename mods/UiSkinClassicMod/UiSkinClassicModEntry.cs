using Ludots.Core.Modding;
using Ludots.UI.Runtime;
using UiShowcaseCoreMod.Showcase;

namespace UiSkinClassicMod;

public sealed class UiSkinClassicModEntry : IMod
{
    public static UiThemePack Theme => UiSkinThemes.Classic;

    public void OnLoad(IModContext context)
    {
        context.Log("[UiSkinClassicMod] Skin loaded.");
    }

    public void OnUnload() { }
}
