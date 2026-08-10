using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using StrategicDomainMod;
using StrategicDomainMod.Runtime;
using UiRegionsMod;
using UiRegionsMod.Runtime;

namespace Y5kGrandStrategyMod.Triggers;

public sealed class InstallY5kWorldOnGameStartTrigger
{
	private readonly IModContext _context;
	private bool _worldSeeded;

	public InstallY5kWorldOnGameStartTrigger(IModContext context)
	{
		_context = context ?? throw new ArgumentNullException(nameof(context));
	}

	public Task ExecuteAsync(ScriptContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		_context.Log("[Y5kGrandStrategyMod] GameStart — waiting for map focus to seed world.");
		return Task.CompletedTask;
	}

	public Task HandleMapLoadedAsync(ScriptContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		if (_worldSeeded)
		{
			return Task.CompletedTask;
		}

		GameEngine engine = context.Get(CoreServiceKeys.Engine)
			?? throw new InvalidOperationException("GameEngine missing.");

		StrategicDomainRuntime runtime = context.Get(StrategicDomainServiceKeys.Runtime)
			?? engine.GetService(StrategicDomainServiceKeys.Runtime)
			?? throw new InvalidOperationException(
				"StrategicDomainRuntime missing. Ensure StrategicDomainMod is loaded before Y5kGrandStrategyMod.");

		UiRegionsRuntime uiRegions = context.Get(UiRegionsServiceKeys.Runtime)
			?? engine.GetService(UiRegionsServiceKeys.Runtime)
			?? throw new InvalidOperationException(
				"UiRegionsRuntime missing. Ensure UiRegionsMod is loaded before Y5kGrandStrategyMod.");

		_ = uiRegions.Catalog;
		SeedMinimalWorld(runtime);
		_worldSeeded = true;
		_context.Log("[Y5kGrandStrategyMod] Seeded estuary/hub/mountain supply topology.");
		return Task.CompletedTask;
	}

	private static void SeedMinimalWorld(StrategicDomainRuntime runtime)
	{
		// Neutral structural keys — display names live in content/locale, not runtime ids.
		const int Estuary = 1;
		const int Pass = 2;
		const int Mountain = 3;
		runtime.RegisterSettlement(Estuary, factionOwner: 1, wallMax: 20, garrisonMax: 20);
		runtime.RegisterSettlement(Pass, factionOwner: 1, wallMax: 15, garrisonMax: 15);
		runtime.RegisterSettlement(Mountain, factionOwner: 2, wallMax: 25, garrisonMax: 25, residentHeroKey: 200);

		runtime.RegisterSupplyNode(101, Estuary, providesSupply: true, isHub: false, capacity: 100, demandWeight: 0);
		runtime.RegisterSupplyNode(102, settlementKey: 0, providesSupply: false, isHub: false, capacity: 0, demandWeight: 0);
		runtime.RegisterSupplyNode(103, Pass, providesSupply: false, isHub: true, capacity: 0, demandWeight: 0);
		runtime.RegisterSupplyNode(104, Mountain, providesSupply: false, isHub: false, capacity: 0, demandWeight: 0);
		runtime.Connect(101, 102);
		runtime.Connect(102, 103);
		runtime.Connect(103, 104);
		runtime.RegisterForce(forceKey: 1, factionOwner: 1, nodeKey: 104, strength: 40, hasSiegeCapability: true, isLogistics: false);
	}
}
