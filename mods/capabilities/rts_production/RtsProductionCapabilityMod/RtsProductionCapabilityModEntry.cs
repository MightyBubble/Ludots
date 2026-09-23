using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RtsProductionCapabilityMod.Runtime;

namespace RtsProductionCapabilityMod;

public sealed class RtsProductionCapabilityModEntry : IMod
{
	public void OnLoad(IModContext context)
	{
		context.Log("[RtsProductionCapabilityMod] Loaded");
		var runtime = new RtsProductionRuntime();
		context.OnEvent(GameEvents.GameStart, ctx =>
		{
			GameEngine? engine = ctx.GetEngine();
			if (engine == null)
			{
				return Task.CompletedTask;
			}

			if (engine.GlobalContext.TryGetValue(RtsProductionIds.InstalledKey, out object? installedObj) &&
				installedObj is bool installed &&
				installed)
			{
				return Task.CompletedTask;
			}

			engine.GlobalContext[RtsProductionIds.InstalledKey] = true;
			engine.GlobalContext[RtsProductionIds.RuntimeKey] = runtime;
			engine.RegisterSystem(new RtsProductionSimulationSystem(engine, runtime), SystemGroup.InputCollection);
			return Task.CompletedTask;
		});
		context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
		context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
		context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
	}

	public void OnUnload()
	{
	}
}
