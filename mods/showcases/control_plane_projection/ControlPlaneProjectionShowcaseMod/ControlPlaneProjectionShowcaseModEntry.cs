using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using ControlPlaneProjectionShowcaseMod.DataPlane;
using ControlPlaneProjectionShowcaseMod.Triggers;

namespace ControlPlaneProjectionShowcaseMod
{
    public sealed class ControlPlaneProjectionShowcaseModEntry : IMod
    {
        private ControlPlaneProjectionDataPlaneInstallation? _dataPlaneInstallation;

        public void OnLoad(IModContext context)
        {
            context.OnEvent(
                GameEvents.GameStart,
                new InstallControlPlaneProjectionShowcaseOnGameStartTrigger(
                    context,
                    installation => _dataPlaneInstallation = installation).ExecuteAsync);
        }

        public void OnUnload()
        {
            _dataPlaneInstallation?.Dispose();
            _dataPlaneInstallation = null;
        }
    }
}
