using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace EntityInfoPanelsMod.Systems;

internal sealed class EntityInfoPanelPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly EntityInfoPanelService _service;

    public EntityInfoPanelPresentationSystem(GameEngine engine, EntityInfoPanelService service)
    {
        _engine = engine;
        _service = service;
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float t)
    {
    }

    public void Update(in float t)
    {
        _service.Refresh(_engine.World, _engine.GlobalContext);

        ScreenOverlayBuffer? overlay = _engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
        IViewController? view = _engine.GetService(CoreServiceKeys.ViewController);
        if (overlay != null && view != null)
        {
            _service.RenderOverlay(overlay, view.Resolution);
        }
    }

    public void AfterUpdate(in float t)
    {
    }

    public void Dispose()
    {
    }
}
