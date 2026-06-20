using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.MassCrowd;
using Ludots.Core.MassCrowd.Runtime;
using MassNavigationMod.UI;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationPanelPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationPanelPresenter _presenter;
    private float _refreshIntervalSeconds;
    private float _refreshAccumulatorSeconds;

    public MassNavigationPanelPresentationSystem(GameEngine engine, MassNavigationPanelPresenter presenter)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (_engine.GetService(MassNavigationKeys.SimulationRuntime) is not MassNavigationSimulationRuntime simulation)
        {
            _refreshAccumulatorSeconds = _refreshIntervalSeconds;
            return;
        }

        float refreshIntervalSeconds = simulation.Config.ScenarioRuntime.PanelControls.PanelRefreshIntervalSeconds;
        if (!(refreshIntervalSeconds > 0f))
        {
            throw new InvalidOperationException(
                "MassNavigation panel presentation requires scenarioRuntime.panelControls.panelRefreshIntervalSeconds > 0.");
        }

        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            _presenter.ClearPanelIfOwned(_engine);
            _refreshIntervalSeconds = refreshIntervalSeconds;
            _refreshAccumulatorSeconds = refreshIntervalSeconds;
            return;
        }

        if (Math.Abs(_refreshIntervalSeconds - refreshIntervalSeconds) > float.Epsilon)
        {
            _refreshIntervalSeconds = refreshIntervalSeconds;
            _refreshAccumulatorSeconds = refreshIntervalSeconds;
        }

        _refreshAccumulatorSeconds += dt;
        if (_refreshAccumulatorSeconds < _refreshIntervalSeconds)
        {
            return;
        }

        _refreshAccumulatorSeconds = 0f;
        simulation.ObservePanelTick();
        _presenter.RefreshPanel(_engine);
    }
}
