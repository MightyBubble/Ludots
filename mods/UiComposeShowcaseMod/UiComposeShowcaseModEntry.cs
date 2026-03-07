using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiShowcaseCoreMod.Showcase;

namespace UiComposeShowcaseMod;

public sealed class UiComposeShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[UiComposeShowcaseMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, scriptContext =>
        {
            UiShowcaseMounting.MountScene(scriptContext, UiShowcaseFactory.CreateComposeScene());
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}
