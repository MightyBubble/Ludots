using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using TimeFlowShowcaseMod.UI;

namespace TimeFlowShowcaseMod.Systems;

internal sealed class TimeFlowShowcasePresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly TimeFlowShowcaseRuntime _runtime;
    private readonly TimeFlowShowcaseHudController _hudController;

    public TimeFlowShowcasePresentationSystem(GameEngine engine, TimeFlowShowcaseRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
        _hudController = new TimeFlowShowcaseHudController();
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float t)
    {
    }

    public void Update(in float t)
    {
        _runtime.AdvancePresentationFrame(_engine, t);

        if (_engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        TimeFlowShowcaseSnapshot? snapshot = _runtime.GetSnapshot();
        if (snapshot == null)
        {
            _hudController.ClearIfOwned(root);
            return;
        }

        _hudController.MountOrRefresh(root, _engine, snapshot);
    }

    public void AfterUpdate(in float t)
    {
    }

    public void Dispose()
    {
    }
}
