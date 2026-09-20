using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using SavePanelMod.Runtime;
using SavePanelMod.UI;

namespace SavePanelMod.Systems;

internal sealed class SavePanelPresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly SavePanelRuntime _runtime;
    private readonly SavePanelController _panel;

    public SavePanelPresentationSystem(GameEngine engine, SavePanelRuntime runtime) : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
        _panel = new SavePanelController(runtime);
    }

    public override void Update(in float dt)
    {
        if (_runtime.IsVisible &&
            _engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.UIRoot) is Ludots.UI.UIRoot root)
        {
            _panel.MountOrRefresh(root, _engine);
        }
        else
        {
            _panel.ClearIfOwned();
        }
    }
}
