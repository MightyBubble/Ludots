using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiShowcaseCoreMod.Showcase;

namespace UiReactiveShowcaseMod;

public sealed class UiReactiveShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[UiReactiveShowcaseMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, scriptContext =>
        {
            UiShowcaseMounting.MountReactivePage(scriptContext, UiShowcaseFactory.CreateReactivePage());
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}
