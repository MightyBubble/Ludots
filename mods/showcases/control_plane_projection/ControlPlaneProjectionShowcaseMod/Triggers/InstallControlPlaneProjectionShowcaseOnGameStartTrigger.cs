using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using ControlPlaneProjectionShowcaseMod.DataPlane;
using ControlPlaneProjectionShowcaseMod.Runtime;
using ControlPlaneProjectionShowcaseMod.Systems;

namespace ControlPlaneProjectionShowcaseMod.Triggers
{
    internal sealed class InstallControlPlaneProjectionShowcaseOnGameStartTrigger : Trigger
    {
        private readonly IModContext _context;

        public InstallControlPlaneProjectionShowcaseOnGameStartTrigger(IModContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            EventKey = GameEvents.GameStart;
        }

        public override async Task ExecuteAsync(ScriptContext context)
        {
            GameEngine? engine = context.GetEngine();
            if (engine == null)
            {
                return;
            }

            if (engine.GlobalContext.TryGetValue(ControlPlaneProjectionShowcaseIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return;
            }

            var state = new ControlPlaneProjectionScenarioState();
            engine.GlobalContext[ControlPlaneProjectionShowcaseIds.InstalledKey] = true;
            engine.GlobalContext[ControlPlaneProjectionShowcaseIds.StateKey] = state;

            engine.InsertSystemBeforeRequired<CurrentSelectionApplySystem>(
                new ControlPlaneProjectionScenarioSystem(engine, state),
                SystemGroup.InputCollection);
            engine.RegisterSystem(new ControlPlaneRoutedSelectionSystem(engine, state), SystemGroup.PostMovement);
            engine.RegisterSystem(new ControlPlaneMarkerProjectionSystem(engine, state), SystemGroup.PostMovement);

            await ControlPlaneProjectionDataPlaneInstaller.TryInstallAsync(engine, state, _context).ConfigureAwait(false);

            _context.Log("[ControlPlaneProjectionShowcaseMod] Systems installed.");
        }
    }
}
