using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiShowcaseCoreMod.Showcase;

namespace UiMarkupShowcaseMod;

public sealed class UiMarkupShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[UiMarkupShowcaseMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, scriptContext =>
        {
            UiShowcaseMounting.MountScene(scriptContext, UiShowcaseFactory.CreateMarkupScene());
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}
