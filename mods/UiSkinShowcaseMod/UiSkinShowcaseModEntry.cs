using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI.Runtime;
using UiShowcaseCoreMod.Showcase;

namespace UiSkinShowcaseMod;

public sealed class UiSkinShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[UiSkinShowcaseMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, scriptContext =>
        {
            var textMeasurer = (IUiTextMeasurer)scriptContext.Get(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)scriptContext.Get(CoreServiceKeys.UiImageSizeProvider);
            UiShowcaseMounting.MountScene(scriptContext, UiShowcaseFactory.CreateSkinShowcaseScene(textMeasurer, imageSizeProvider));
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}

