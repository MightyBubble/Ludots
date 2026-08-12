using System;
using System.IO;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
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

		if (!engine.TryGetService(CoreServiceKeys.LiveGasEditPipeline, out LiveGasEditPipeline pipeline) ||
			pipeline == null)
		{
			throw new InvalidOperationException(
				"LiveGasEditPipeline service is required for LiveSkillWorkbenchMod Precheck/Apply.");
		}

		runtime.BindPipeline(pipeline);
		modContext.Log("[LiveSkillWorkbenchMod] Bound Core LiveGasEditPipeline for Precheck/Apply.");

		if (!engine.TryGetService(CoreServiceKeys.LiveAttributeCommandExecutor, out LiveAttributeCommandExecutor? attrExec) ||
			attrExec == null)
		{
			throw new InvalidOperationException("LiveAttributeCommandExecutor service is required (#620).");
		}

		if (!engine.TryGetService(CoreServiceKeys.LiveEffectChainTracer, out LiveEffectChainTracer? tracer) ||
			tracer == null)
		{
			throw new InvalidOperationException("LiveEffectChainTracer service is required (#621).");
		}

		if (!engine.TryGetService(CoreServiceKeys.AiSkillDraftGenerator, out IAiSkillDraftGenerator? ai) ||
			ai == null)
		{
			throw new InvalidOperationException("AiSkillDraftGenerator service is required (#623).");
		}

		if (!engine.TryGetService(CoreServiceKeys.LiveAiDraftBinder, out LiveAiDraftBinder? binder) ||
			binder == null)
		{
			throw new InvalidOperationException("LiveAiDraftBinder service is required (#623).");
		}

		if (!engine.TryGetService(CoreServiceKeys.LiveEditModSaveService, out LiveEditModSaveService? save) ||
			save == null)
		{
			throw new InvalidOperationException("LiveEditModSaveService service is required (#624).");
		}

		string saveModId = LiveSkillWorkbenchIds.DefaultSaveTargetModId;
		string? saveModRoot = null;
		if (engine.VFS.TryResolveFullPath($"{saveModId}:mod.json", out string modJsonPath))
		{
			saveModRoot = Path.GetDirectoryName(modJsonPath);
			if (string.IsNullOrWhiteSpace(saveModRoot) || !Directory.Exists(saveModRoot))
			{
				throw new DirectoryNotFoundException(
					$"Save target Mod root for '{saveModId}' does not exist (resolved from {modJsonPath}).");
			}
		}

		runtime.BindEpicServices(attrExec, tracer, ai, binder, save, saveModId, saveModRoot);
		modContext.Log(saveModRoot == null
			? $"[LiveSkillWorkbenchMod] Bound epic services; save root unset until Mod '{saveModId}' is mounted (save commands fail closed)."
			: $"[LiveSkillWorkbenchMod] Bound #620/#621/#623/#624 epic services; save root={saveModRoot}");

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
