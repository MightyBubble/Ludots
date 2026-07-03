using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace CapabilityStandardPhysics2DShowcaseMod.Runtime;

internal sealed class CapabilityStandardPhysics2DShowcasePresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly CapabilityStandardPhysics2DShowcaseRuntime _runtime;
    private readonly CapabilityStandardPhysics2DShowcasePanelController _panel;

    public CapabilityStandardPhysics2DShowcasePresentationSystem(
        GameEngine engine,
        CapabilityStandardPhysics2DShowcaseRuntime runtime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _panel = new CapabilityStandardPhysics2DShowcasePanelController(runtime);
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float t)
    {
        if (_engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        if (!_runtime.IsActive)
        {
            _panel.ClearIfOwned(root);
            return;
        }

        CapabilityStandardPhysics2DShowcasePanelState state = _runtime.CapturePanelState(_engine);
        _panel.MountOrSync(root, _engine, in state);
    }
}
