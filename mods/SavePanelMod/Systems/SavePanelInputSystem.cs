using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using SavePanelMod.Runtime;

namespace SavePanelMod.Systems;

internal sealed class SavePanelInputSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly SavePanelRuntime _runtime;

    public SavePanelInputSystem(GameEngine engine, SavePanelRuntime runtime) : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public override void Update(in float dt)
    {
        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input &&
            input.PressedThisFrame(SavePanelIds.ToggleAction))
        {
            _runtime.ToggleVisible();
        }
    }
}
