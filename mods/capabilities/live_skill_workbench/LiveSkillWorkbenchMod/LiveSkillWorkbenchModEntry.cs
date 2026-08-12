using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using LiveSkillWorkbenchMod.Contracts;
using LiveSkillWorkbenchMod.DataPlane;
using LiveSkillWorkbenchMod.Runtime;

namespace LiveSkillWorkbenchMod;

public sealed class LiveSkillWorkbenchModEntry : IMod
{
	private IModContext? _modContext;
	private LiveSkillWorkbenchRuntime? _runtime;
	private LiveSkillWorkbenchDataPlaneInstallation? _installation;

	public void OnLoad(IModContext context)
	{
		_modContext = context ?? throw new ArgumentNullException(nameof(context));
		context.Log("[LiveSkillWorkbenchMod] Loaded.");
		// Capability starts with no authored catalog/document. A real/injected source must provide one.
		_runtime = new LiveSkillWorkbenchRuntime();
		context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
	}

	public void OnUnload()
	{
		_installation?.Dispose();
		_installation = null;
		_runtime = null;
		_modContext = null;
	}

	private async Task OnGameStartAsync(ScriptContext context)
	{
		IModContext modContext = _modContext
			?? throw new InvalidOperationException("LiveSkillWorkbenchMod context was not captured during OnLoad.");
		LiveSkillWorkbenchRuntime runtime = _runtime
			?? throw new InvalidOperationException("LiveSkillWorkbenchRuntime was not created during OnLoad.");
		GameEngine engine = context.GetEngine();

		// Publish before DataPlane so other Mods can resolve the host (#618+ extension point).
		engine.SetService(LiveSkillWorkbenchServiceKeys.Runtime, runtime);

		if (engine.TryGetService(
				LiveSkillWorkbenchServiceKeys.DocumentSource,
				out ILiveSkillWorkbenchDocumentSource documentSource) &&
			documentSource != null)
		{
			runtime.LoadFromSource(documentSource);
			modContext.Log("[LiveSkillWorkbenchMod] Loaded document from injected DocumentSource service.");
		}

		_installation = await LiveSkillWorkbenchDataPlaneInstaller
			.InstallAsync(engine, modContext, runtime)
			.ConfigureAwait(false);
	}
}
