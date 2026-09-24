using System;
using System.Threading.Tasks;
using CapabilityStandardVirtualCameraShowcaseMod.Runtime;
using CapabilityStandardVirtualCameraShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Scripting;

namespace CapabilityStandardVirtualCameraShowcaseMod;

public sealed class CapabilityStandardVirtualCameraShowcaseModEntry : IMod
{
    private readonly CapabilityStandardVirtualCameraShowcaseRuntime _runtime = new();

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardVirtualCameraShowcaseMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.GetEngine()
                ?? throw new InvalidOperationException("CapabilityStandardVirtualCameraShowcaseMod requires GameEngine.");

            var config = new CapabilityStandardVirtualCameraShowcaseConfigLoader(engine.ConfigPipeline)
                .Load(engine.ConfigCatalog, engine.ConfigConflictReport);
            if (!CapabilityStandardVirtualCameraShowcaseIds.IsShowcaseMap(config.MapId))
            {
                throw new InvalidOperationException(
                    $"CapabilityStandardVirtualCameraShowcaseMod config mapId '{config.MapId}' does not match showcase map '{CapabilityStandardVirtualCameraShowcaseIds.MapId}'.");
            }

            if (!engine.TryGetService(CoreServiceKeys.DebugDrawCommandBuffer, out DebugDrawCommandBuffer _))
            {
                engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, new DebugDrawCommandBuffer());
            }

            engine.RegisterSystem(
                new CapabilityStandardVirtualCameraAvatarMoveSystem(engine),
                SystemGroup.InputCollection);
            engine.RegisterSystem(
                new CapabilityStandardVirtualCameraImpulseSystem(engine, config),
                SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(
                new CapabilityStandardVirtualCameraObstacleDebugSystem(engine));
            engine.RegisterPresentationSystem(
                new CapabilityStandardVirtualCameraDebugPanelSystem(engine, config));

            return Task.CompletedTask;
        });

        context.OnEvent(GameEvents.MapLoaded, _runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, _runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, _runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }
}
