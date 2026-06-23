using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace UtilityAutocastShowcaseMod.Triggers;

public sealed class PrintUtilityAutocastTraceOnMapLoadedTrigger : Trigger
{
    private const string MapId = "utility_autocast_showcase";
    private static readonly EventKey PrintAiConfig = new("AIInspector.PrintAiConfig");
    private readonly IModContext _context;

    public PrintUtilityAutocastTraceOnMapLoadedTrigger(IModContext context)
    {
        _context = context;
        EventKey = GameEvents.MapLoaded;
    }

    public override async Task ExecuteAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null ||
            !string.Equals(engine.CurrentMapSession?.MapConfig?.Id, MapId, StringComparison.Ordinal))
        {
            return;
        }

        await engine.TriggerManager.FireEventAsync(PrintAiConfig, engine.CreateContext());
        _context.Log("[UtilityAutocastShowcaseMod] AI inspector trace requested");
    }
}
