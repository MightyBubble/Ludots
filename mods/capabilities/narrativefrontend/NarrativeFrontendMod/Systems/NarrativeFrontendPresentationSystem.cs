using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using NarrativeFrontendMod.Runtime;
using NarrativeFrontendMod.UI;

namespace NarrativeFrontendMod.Systems;

internal sealed class NarrativeFrontendPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly NarrativeFrontendService _service;
    private readonly NarrativeFrontendUiController _controller = new();
    private int _lastRevision = -1;

    public NarrativeFrontendPresentationSystem(GameEngine engine, NarrativeFrontendService service)
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
        if (_engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        NarrativeFrontendRenderState snapshot = _service.Snapshot;
        if (!snapshot.HasVisibleContent)
        {
            _controller.ClearIfOwned(root);
            _lastRevision = snapshot.Revision;
            return;
        }

        if (_lastRevision == snapshot.Revision)
        {
            return;
        }

        _controller.MountOrRefresh(root, _engine, snapshot);
        _lastRevision = snapshot.Revision;
    }

    public void AfterUpdate(in float t)
    {
    }

    public void Dispose()
    {
    }
}
