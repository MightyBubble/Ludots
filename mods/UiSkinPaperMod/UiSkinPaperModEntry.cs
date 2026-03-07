using Ludots.Core.Modding;
using Ludots.UI.Runtime;
using UiShowcaseCoreMod.Showcase;

namespace UiSkinPaperMod;

public sealed class UiSkinPaperModEntry : IMod
{
    public static UiThemePack Theme => UiSkinThemes.Paper;

    public void OnLoad(IModContext context)
    {
        context.Log("[UiSkinPaperMod] Skin loaded.");
    }

    public void OnUnload() { }
}
