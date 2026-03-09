using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiShowcaseCoreMod.Showcase;

namespace UiSkinShowcaseMod;

public sealed class UiSkinShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[UiSkinShowcaseMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, scriptContext =>
        {
            UiShowcaseMounting.MountScene(scriptContext, UiShowcaseFactory.CreateSkinShowcaseScene());
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}

