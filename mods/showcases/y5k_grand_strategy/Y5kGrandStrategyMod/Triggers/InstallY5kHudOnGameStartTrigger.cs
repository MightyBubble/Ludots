using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiRegionsMod.Runtime;

namespace Y5kGrandStrategyMod.Triggers;

public sealed class InstallY5kHudOnGameStartTrigger
{
	public const string ManifestVfsPath = "Y5kGrandStrategyMod:assets/PanelKit/y5k_hud_manifest.json";

	private readonly IModContext _context;
	private UiRegionsHudInstallation? _installation;

	public InstallY5kHudOnGameStartTrigger(IModContext context)
	{
		_context = context ?? throw new ArgumentNullException(nameof(context));
	}

	public Task ExecuteAsync(ScriptContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		GameEngine engine = context.Get(CoreServiceKeys.Engine)
			?? throw new InvalidOperationException("GameEngine missing.");

		if (_installation != null)
		{
			return Task.CompletedTask;
		}

		if (engine.VFS == null ||
		    !engine.VFS.TryResolveFullPath(ManifestVfsPath, out string manifestPath) ||
		    string.IsNullOrWhiteSpace(manifestPath))
		{
			throw new FileNotFoundException(
				$"Unable to resolve y5k HUD manifest via VFS path '{ManifestVfsPath}'.");
		}

		_installation = UiRegionsHudInstaller.Install(engine, manifestPath);
		_context.Log(
			$"[Y5kGrandStrategyMod] HUD bound: panels={_installation.BoundPanelIds.Count}, topics={_installation.Topics.Count}");
		return Task.CompletedTask;
	}

	public void DisposeInstallation()
	{
		_installation?.Dispose();
		_installation = null;
	}
}
