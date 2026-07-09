using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
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
        private readonly Action<ControlPlaneProjectionDataPlaneInstallation?> _onDataPlaneInstalled;

        public InstallControlPlaneProjectionShowcaseOnGameStartTrigger(
            IModContext context,
            Action<ControlPlaneProjectionDataPlaneInstallation?> onDataPlaneInstalled)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _onDataPlaneInstalled = onDataPlaneInstalled ?? throw new ArgumentNullException(nameof(onDataPlaneInstalled));
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

            engine.RegisterSystem(new ControlPlaneProjectionScenarioSystem(engine, state), SystemGroup.InputCollection);
            engine.RegisterSystem(new ControlPlaneRoutedSelectionSystem(engine, state), SystemGroup.PostMovement);
            engine.RegisterSystem(new ControlPlaneMarkerProjectionSystem(engine, state), SystemGroup.PostMovement);

            ControlPlaneProjectionDataPlaneInstallation? installation =
                await ControlPlaneProjectionDataPlaneInstaller.TryInstallAsync(engine, state, _context).ConfigureAwait(false);
            _onDataPlaneInstalled(installation);

            _context.Log("[ControlPlaneProjectionShowcaseMod] Systems installed.");
        }
    }
}
