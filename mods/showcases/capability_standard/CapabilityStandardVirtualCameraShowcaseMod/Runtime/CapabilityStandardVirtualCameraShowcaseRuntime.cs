using System;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Scripting;

namespace CapabilityStandardVirtualCameraShowcaseMod.Runtime;

internal sealed class CapabilityStandardVirtualCameraShowcaseRuntime
{
    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine engine = context.GetEngine()
            ?? throw new InvalidOperationException("CapabilityStandardVirtualCameraShowcaseMod requires GameEngine.");

        string mapId = context.TryGet(CoreServiceKeys.MapId, out var resolvedMapId)
            ? resolvedMapId.Value
            : engine.CurrentMapSession?.MapId.Value ?? string.Empty;

        if (!CapabilityStandardVirtualCameraShowcaseIds.IsShowcaseMap(mapId))
        {
            engine.GlobalContext.Remove(CapabilityStandardVirtualCameraShowcaseIds.RuntimeStateKey);
            return Task.CompletedTask;
        }

        VirtualCameraRegistry registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry)
            ?? throw new InvalidOperationException("CapabilityStandardVirtualCameraShowcaseMod requires VirtualCameraRegistry.");
        RequireVirtualCamera(registry, CapabilityStandardVirtualCameraShowcaseIds.TacticalCameraId);
        RequireVirtualCamera(registry, CapabilityStandardVirtualCameraShowcaseIds.BehaviorOrbitCameraId);
        RequireVirtualCamera(registry, CapabilityStandardVirtualCameraShowcaseIds.HeightmapOrbitCameraId);
        RequireVirtualCamera(registry, CapabilityStandardVirtualCameraShowcaseIds.TpsCameraId);
        RequireVirtualCamera(registry, CapabilityStandardVirtualCameraShowcaseIds.FpsCameraId);
        RequireVirtualCamera(registry, CapabilityStandardVirtualCameraShowcaseIds.RevealShotCameraId);

        if (engine.GameSession.Camera.VirtualCameraBrain == null)
        {
            throw new InvalidOperationException("CapabilityStandardVirtualCameraShowcaseMod requires VirtualCameraBrain.");
        }

        if (!engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) ||
            managerObj is not ViewModeManager manager)
        {
            throw new InvalidOperationException("CapabilityStandardVirtualCameraShowcaseMod requires ViewModeManager.");
        }

        RequireViewMode(manager, CapabilityStandardVirtualCameraShowcaseIds.BehaviorOrbitModeId);
        RequireViewMode(manager, CapabilityStandardVirtualCameraShowcaseIds.HeightmapOrbitModeId);
        RequireViewMode(manager, CapabilityStandardVirtualCameraShowcaseIds.TpsModeId);
        RequireViewMode(manager, CapabilityStandardVirtualCameraShowcaseIds.FpsModeId);
        EnsureDefaultCommandSource(engine);

        engine.GlobalContext[CapabilityStandardVirtualCameraShowcaseIds.RuntimeStateKey] =
            new CapabilityStandardVirtualCameraShowcaseState(
                mapId,
                CapabilityStandardVirtualCameraShowcaseIds.TacticalCameraId,
                CapabilityStandardVirtualCameraShowcaseIds.BehaviorOrbitCameraId,
                CapabilityStandardVirtualCameraShowcaseIds.HeightmapOrbitCameraId,
                CapabilityStandardVirtualCameraShowcaseIds.TpsCameraId,
                CapabilityStandardVirtualCameraShowcaseIds.FpsCameraId,
                CapabilityStandardVirtualCameraShowcaseIds.RevealShotCameraId,
                CapabilityStandardVirtualCameraShowcaseIds.BehaviorOrbitModeId,
                CapabilityStandardVirtualCameraShowcaseIds.HeightmapOrbitModeId,
                CapabilityStandardVirtualCameraShowcaseIds.TpsModeId,
                CapabilityStandardVirtualCameraShowcaseIds.FpsModeId);

        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        string mapId = context.TryGet(CoreServiceKeys.MapId, out var resolvedMapId)
            ? resolvedMapId.Value
            : string.Empty;

        if (CapabilityStandardVirtualCameraShowcaseIds.IsShowcaseMap(mapId))
        {
            engine.GlobalContext.Remove(CapabilityStandardVirtualCameraShowcaseIds.RuntimeStateKey);
        }

        return Task.CompletedTask;
    }

    private static void RequireVirtualCamera(VirtualCameraRegistry registry, string cameraId)
    {
        if (!registry.TryGet(cameraId, out var definition) || definition == null)
        {
            throw new InvalidOperationException(
                $"Capability standard virtual camera showcase requires virtual camera '{cameraId}'.");
        }
    }

    private static void RequireViewMode(ViewModeManager manager, string modeId)
    {
        for (int i = 0; i < manager.Modes.Count; i++)
        {
            if (string.Equals(manager.Modes[i].Id, modeId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Capability standard virtual camera showcase requires view mode '{modeId}'.");
    }

    private static void EnsureDefaultCommandSource(GameEngine engine)
    {
        Entity localPlayer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        if (localPlayer == Entity.Null || !engine.World.IsAlive(localPlayer))
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera showcase requires a live LocalPlayerEntity before publishing the command source.");
        }

        EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("Capability standard virtual camera showcase requires EntityCollectionStore.");

        Span<Entity> members = stackalloc Entity[1] { localPlayer };
        var descriptor = EntityCollectionDescriptor.Create(
            EntityCollectionKeys.CommandSource,
            EntityCollectionSourceKind.Explicit,
            EntityCollectionRoleKind.CommandSource,
            localPlayer,
            localPlayer,
            "Virtual camera avatar",
            "Default local avatar command source");
        collections.Replace(localPlayer, descriptor, members, localPlayer);
    }
}

public sealed record CapabilityStandardVirtualCameraShowcaseState(
    string MapId,
    string TacticalCameraId,
    string BehaviorOrbitCameraId,
    string HeightmapOrbitCameraId,
    string TpsCameraId,
    string FpsCameraId,
    string RevealShotCameraId,
    string BehaviorOrbitModeId,
    string HeightmapOrbitModeId,
    string TpsModeId,
    string FpsModeId);
