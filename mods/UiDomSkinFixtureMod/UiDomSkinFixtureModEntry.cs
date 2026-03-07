using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiShowcaseCoreMod.Showcase;

namespace UiDomSkinFixtureMod;

public sealed class UiDomSkinFixtureModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[UiDomSkinFixtureMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, scriptContext =>
        {
            UiShowcaseMounting.MountScene(scriptContext, UiShowcaseFactory.CreateSkinFixtureScene(UiSkinThemes.Classic));
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}
