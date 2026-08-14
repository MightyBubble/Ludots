using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using RtsMultiplayerFrontlineThreeProcessAcceptanceMod.Runtime;

namespace RtsMultiplayerFrontlineThreeProcessAcceptanceMod;

public sealed class RtsMultiplayerFrontlineThreeProcessAcceptanceModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        AcceptancePlan plan = AcceptancePlan.Load(context);
        var frontline = AcceptancePlan.LoadFrontlineConfig(context);
        context.OnEvent(GameEvents.NetworkRuntimeReady, scriptContext =>
        {
            GameEngine engine = scriptContext.GetEngine()
                ?? throw new InvalidOperationException("Three-process acceptance requires a running game engine.");
            var progress = new AcceptanceProgress();
            var driver = new AcceptanceDriver(engine, plan, frontline, progress);
            NetworkProcessRole role = engine.GetService(CoreServiceKeys.NetworkProcessRole);
            if (role == NetworkProcessRole.ReplicatedClient)
            {
                if (engine.TryGetService(
                        CoreServiceKeys.PresentationCaptureMilestoneSource,
                        out IPresentationCaptureMilestoneSource existingSource) &&
                    existingSource != null)
                {
                    throw new InvalidOperationException(
                        $"Three-process acceptance cannot replace presentation capture milestone source '{existingSource.GetType().FullName}'.");
                }

                engine.SetService(
                    CoreServiceKeys.PresentationCaptureMilestoneSource,
                    (IPresentationCaptureMilestoneSource)progress);
                engine.RegisterSystem(driver, SystemGroup.LocalInput);
                engine.RegisterPresentationSystem(new AcceptancePresentationSystem(engine, plan, progress));
            }
            else if (role == NetworkProcessRole.AuthoritativeServer)
            {
                engine.RegisterSystem(driver, SystemGroup.Cleanup);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Three-process acceptance does not support network role {role}.");
            }
            return Task.CompletedTask;
        });
        context.Log("[RtsMultiplayerFrontlineThreeProcessAcceptanceMod] Player-path acceptance driver loaded");
    }

    public void OnUnload()
    {
    }
}
