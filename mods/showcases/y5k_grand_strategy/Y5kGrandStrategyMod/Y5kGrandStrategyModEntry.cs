using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Y5kGrandStrategyMod.Runtime;
using Y5kGrandStrategyMod.Triggers;

namespace Y5kGrandStrategyMod;

public sealed class Y5kGrandStrategyModEntry : IMod
{
	private IModContext? _context;
	private InstallY5kHudOnGameStartTrigger? _hudTrigger;
	private InstallY5kWorldOnGameStartTrigger? _worldTrigger;
	private Y5kLoopDemoDirectorSystem? _demoDirector;

	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		_context = context;
		context.Log("[Y5kGrandStrategyMod] Loaded — content + HUD assembly only.");
		_worldTrigger = new InstallY5kWorldOnGameStartTrigger(context);
		_hudTrigger = new InstallY5kHudOnGameStartTrigger(context);

		// Seed world/objectives first on MapLoaded, then bind/refresh HUD so panels show live data.
		context.OnEvent(GameEvents.GameStart, _worldTrigger.ExecuteAsync);
		context.OnEvent(GameEvents.MapLoaded, async ctx =>
		{
			await _worldTrigger.HandleMapLoadedAsync(ctx).ConfigureAwait(false);
			await _hudTrigger.ExecuteAsync(ctx).ConfigureAwait(false);
			InstallDemoDirector(ctx);
		});
	}

	public void OnUnload()
	{
		if (_demoDirector != null)
		{
			_demoDirector.Dispose();
			_demoDirector = null;
		}

		_hudTrigger?.DisposeInstallation();
		_hudTrigger = null;
		_worldTrigger = null;
		_context = null;
	}

	private void InstallDemoDirector(ScriptContext context)
	{
		GameEngine engine = context.Get(CoreServiceKeys.Engine)
			?? throw new InvalidOperationException("GameEngine missing.");
		if (_demoDirector != null || _hudTrigger?.Installation == null)
		{
			return;
		}

		var state = new Y5kDemoState
		{
			PhaseId = "boot",
			PhaseTitle = "开局",
			PhaseDetail = "河口有粮、隘口在握、山城未下。",
			BulletinLines = new[]
			{
				"开局",
				"河口有粮、隘口在握、山城未下。",
			},
		};
		engine.SetService(Y5kDemoServiceKeys.State, state);
		Y5kLoopDemoDirectorSystem.WireBulletin(engine, state);

		StrategicDomainMod.Runtime.StrategicDomainRuntime domain =
			engine.GetService(StrategicDomainMod.StrategicDomainServiceKeys.Runtime)
			?? throw new InvalidOperationException("StrategicDomainRuntime missing.");
		Ludots.Core.Gameplay.Providers.ProviderServices providers =
			engine.GetService(CoreServiceKeys.ProviderServices)
			?? throw new InvalidOperationException("ProviderServices missing.");
		Ludots.Core.Gameplay.Activities.ActivityRuntimeService activities =
			engine.GetService(CoreServiceKeys.ActivityRuntimeService)
			?? throw new InvalidOperationException("ActivityRuntimeService missing.");
		Ludots.Core.Gameplay.Tasks.TaskRuntimeService tasks =
			engine.GetService(CoreServiceKeys.TaskRuntimeService)
			?? throw new InvalidOperationException("TaskRuntimeService missing.");

		_demoDirector = new Y5kLoopDemoDirectorSystem(
			engine.World,
			domain,
			providers,
			activities,
			tasks,
			state,
			() => _hudTrigger!.Installation!.RefreshLivePanels());
		engine.SetService(Y5kDemoServiceKeys.Director, _demoDirector);
		engine.RegisterPresentationSystem(_demoDirector);
		_hudTrigger.Installation.RefreshLivePanels();
		_context?.Log("[Y5kGrandStrategyMod] Five-loop demo director armed.");
	}
}
