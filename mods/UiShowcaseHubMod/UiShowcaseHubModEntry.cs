using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiShowcaseCoreMod.Showcase;

namespace UiShowcaseHubMod;

public sealed class UiShowcaseHubModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[UiShowcaseHubMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, scriptContext =>
        {
            UiShowcaseMounting.MountScene(scriptContext, UiShowcaseFactory.CreateHubScene());
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}
