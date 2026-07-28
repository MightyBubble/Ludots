using GraphWorkbenchShowcaseMod.DataPlane;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace GraphWorkbenchShowcaseMod;

public sealed class GraphWorkbenchShowcaseModEntry : IMod
{
    private GraphWorkbenchDataPlaneInstallation? _dataPlaneInstallation;

    public void OnLoad(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.OnEvent(GameEvents.GameStart, gameContext =>
        {
            GameEngine engine = gameContext.GetEngine()
                ?? throw new InvalidOperationException("GraphWorkbenchShowcaseMod requires a GameEngine.");
            return InstallAsync(engine, context);
        });
    }

    public void OnUnload()
    {
        _dataPlaneInstallation?.Dispose();
        _dataPlaneInstallation = null;
    }

    private async Task InstallAsync(GameEngine engine, IModContext context)
    {
        _dataPlaneInstallation?.Dispose();
        _dataPlaneInstallation = await GraphWorkbenchDataPlaneInstaller
            .InstallAsync(engine, context)
            .ConfigureAwait(false);
    }
}
