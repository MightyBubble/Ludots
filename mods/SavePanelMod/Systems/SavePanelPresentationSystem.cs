using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;
using SavePanelMod.Runtime;
using SavePanelMod.UI;

namespace SavePanelMod.Systems;

internal sealed class SavePanelPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly SavePanelRuntime _runtime;
    private readonly SavePanelController _controller;

    public SavePanelPresentationSystem(GameEngine engine, SavePanelRuntime runtime, SavePanelController controller)
    {
        _engine = engine;
        _runtime = runtime;
        _controller = controller;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float t)
    {
        _runtime.DrainPendingAfterFixedStep(_engine);
        HandleInput();

        if (_engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        if (_runtime.IsVisible(_engine))
        {
            _controller.MountOrRefresh(root, _engine);
        }
        else
        {
            _controller.ClearIfOwned();
        }
    }

    private void HandleInput()
    {
        if (_engine.GetService(CoreServiceKeys.InputHandler) is not PlayerInputHandler input)
        {
            return;
        }

        if (input.PressedThisFrame(SavePanelIds.TogglePanel))
        {
            _runtime.ToggleVisible(_engine);
        }

        if (!_runtime.IsVisible(_engine))
        {
            return;
        }

        if (input.PressedThisFrame(SavePanelIds.SaveManual))
        {
            _runtime.RequestManualSave(_engine);
        }

        if (input.PressedThisFrame(SavePanelIds.WriteAutosave))
        {
            _runtime.RequestAutosave(_engine);
        }

        if (input.PressedThisFrame(SavePanelIds.RestoreSelected))
        {
            _runtime.RestoreSelected(_engine);
        }

        if (input.PressedThisFrame(SavePanelIds.DeleteSelected))
        {
            _runtime.DeleteSelected(_engine);
        }
    }
}
