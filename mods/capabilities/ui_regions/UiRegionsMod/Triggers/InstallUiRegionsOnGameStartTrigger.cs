using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiRegionsMod.Runtime;

namespace UiRegionsMod.Triggers;

public sealed class InstallUiRegionsOnGameStartTrigger
{
	private readonly IModContext _context;

	public InstallUiRegionsOnGameStartTrigger(IModContext context)
	{
		_context = context ?? throw new ArgumentNullException(nameof(context));
	}

	public Task ExecuteAsync(ScriptContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		GameEngine engine = context.Get(CoreServiceKeys.Engine)
			?? throw new InvalidOperationException("GameEngine missing.");

		var runtime = new UiRegionsRuntime();
		runtime.Install(_ => true);
		engine.SetService(UiRegionsServiceKeys.Runtime, runtime);
		context.Set(UiRegionsServiceKeys.Runtime, runtime);
		_context.Log("[UiRegionsMod] Nine-grid region catalog installed.");
		return Task.CompletedTask;
	}
}
