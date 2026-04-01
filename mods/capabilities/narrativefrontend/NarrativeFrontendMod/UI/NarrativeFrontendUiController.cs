using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using NarrativeFrontendMod.Runtime;

namespace NarrativeFrontendMod.UI;

internal sealed class NarrativeFrontendUiController
{
    private ReactivePage<NarrativeFrontendRenderState>? _page;

    public void MountOrRefresh(UIRoot root, GameEngine engine, NarrativeFrontendRenderState state)
    {
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<NarrativeFrontendRenderState>(textMeasurer, imageSizeProvider, state, NarrativeFrontendUiComposer.BuildRoot);
        }
        else
        {
            _page.SetState(_ => state);
        }

        if (!ReferenceEquals(root.Scene, _page.Scene))
        {
            root.MountScene(_page.Scene);
        }

        root.IsDirty = true;
    }

    public void ClearIfOwned(UIRoot root)
    {
        if (_page != null && ReferenceEquals(root.Scene, _page.Scene))
        {
            root.ClearScene();
        }
    }
}
