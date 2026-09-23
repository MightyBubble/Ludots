using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI.Surfaces;
using ParticipantViewCapabilityMod.Runtime;
using RtsHudWebMod.Systems;
using RtsProductionCapabilityMod.Runtime;

namespace RtsHudWebMod;

public sealed class RtsHudWebModEntry : IMod
{
	public void OnLoad(IModContext context)
	{
		context.Log("[RtsHudWebMod] Loaded");
		var controller = new UI.RtsHudPanelController();
		context.OnEvent(GameEvents.GameStart, ctx =>
		{
			GameEngine? engine = ctx.GetEngine();
			if (engine == null)
			{
				return Task.CompletedTask;
			}

			if (engine.GetService(RtsHudWebServiceKeys.UiSurfaceLeaseService) == null)
			{
				engine.SetService(RtsHudWebServiceKeys.UiSurfaceLeaseService, new UiSurfaceLeaseService());
			}
			engine.GlobalContext[ParticipantViewCapabilityIds.PanelDisabledServiceKey] = true;

			if (engine.GlobalContext.TryGetValue(RtsHudWebIds.InstalledKey, out object? installedObj) &&
				installedObj is bool installed &&
				installed)
			{
				return Task.CompletedTask;
			}

			engine.GlobalContext[RtsHudWebIds.InstalledKey] = true;
			engine.RegisterPresentationSystem(new RtsHudPresentationSystem(engine, controller));
			return Task.CompletedTask;
		});
	}

	public void OnUnload()
	{
	}
}
