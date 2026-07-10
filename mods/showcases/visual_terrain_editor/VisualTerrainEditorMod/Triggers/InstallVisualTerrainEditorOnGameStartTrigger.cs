using System.Threading.Tasks;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using VisualTerrainEditorMod.Runtime;
using VisualTerrainEditorMod.Systems;

namespace VisualTerrainEditorMod.Triggers;

internal sealed class InstallVisualTerrainEditorOnGameStartTrigger : Trigger
{
    private const string InstalledKey = "VisualTerrainEditorMod.Installed";
    private readonly VisualTerrainEditorRuntime _runtime;

    public InstallVisualTerrainEditorOnGameStartTrigger(VisualTerrainEditorRuntime runtime)
    {
        _runtime = runtime;
        EventKey = GameEvents.GameStart;
    }

    public override Task ExecuteAsync(ScriptContext context)
    {
        var engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (engine.GlobalContext.TryGetValue(InstalledKey, out var installedObj) &&
            installedObj is bool installed &&
            installed)
        {
            return Task.CompletedTask;
        }

        engine.GlobalContext[InstalledKey] = true;
        engine.InsertPresentationSystemBefore<PerformerRuleSystem>(new VisualTerrainEditorPresentationSystem(engine, _runtime));
        return Task.CompletedTask;
    }
}
