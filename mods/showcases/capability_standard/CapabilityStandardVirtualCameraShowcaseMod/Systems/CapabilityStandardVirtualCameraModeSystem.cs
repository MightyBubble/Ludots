using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace CapabilityStandardVirtualCameraShowcaseMod.Systems;

/// <summary>
/// Mode switching for the virtual-camera showcase: F5-F8 replace the local camera stack with the
/// picked definition and write the sparse <see cref="InteractionMode"/> component on the sole
/// possessed rep. InputContextProjectionSystem turns the mode into the Controls / AvatarControls
/// IMC context set (orbit keys vs avatar WASD); the Control mode is also the initial state while
/// the showcase map is focused.
/// </summary>
public sealed class CapabilityStandardVirtualCameraModeSystem : ISystem<float>
{
    internal const string ControlModeId = "CapabilityStandard.VirtualCamera.Control";
    internal const string AvatarModeId = "CapabilityStandard.VirtualCamera.Avatar";

    private readonly World _world;
    private readonly Dictionary<string, object> _globals;

    public CapabilityStandardVirtualCameraModeSystem(World world, Dictionary<string, object> globals)
    {
        _world = world;
        _globals = globals;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float t)
    {
        if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) ||
            inputObj is not IInputActionReader input)
        {
            return;
        }

        bool showcaseMapFocused = CapabilityStandardVirtualCameraShowcaseIds.IsShowcaseMap(CurrentMapId());

        string? cameraId = null;
        string? modeId = null;
        if (input.PressedThisFrame(CapabilityStandardVirtualCameraShowcaseIds.BehaviorOrbitModeActionId))
        {
            cameraId = CapabilityStandardVirtualCameraShowcaseIds.BehaviorOrbitCameraId;
            modeId = ControlModeId;
        }
        else if (input.PressedThisFrame(CapabilityStandardVirtualCameraShowcaseIds.HeightmapOrbitModeActionId))
        {
            cameraId = CapabilityStandardVirtualCameraShowcaseIds.HeightmapOrbitCameraId;
            modeId = ControlModeId;
        }
        else if (input.PressedThisFrame(CapabilityStandardVirtualCameraShowcaseIds.TpsModeActionId))
        {
            cameraId = CapabilityStandardVirtualCameraShowcaseIds.TpsCameraId;
            modeId = AvatarModeId;
        }
        else if (input.PressedThisFrame(CapabilityStandardVirtualCameraShowcaseIds.FpsModeActionId))
        {
            cameraId = CapabilityStandardVirtualCameraShowcaseIds.FpsCameraId;
            modeId = AvatarModeId;
        }

        if (cameraId != null && modeId != null && showcaseMapFocused)
        {
            _globals[CoreServiceKeys.VirtualCameraRequest.Name] = new VirtualCameraRequest
            {
                Id = cameraId,
                ResetRuntimeState = true,
                ReplaceActiveStack = true
            };

            SetMode(modeId);
        }
        else if (showcaseMapFocused &&
                 TryGetModeMap(out var modeMap) &&
                 ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out Entity rep) &&
                 _world.IsAlive(rep) &&
                 !_world.Has<InteractionMode>(rep))
        {
            // Initial state while the showcase map is focused: the Control mode's context set
            // carries the orbit keys and the F5-F8 mode switches themselves.
            SetMode(ControlModeId);
        }
    }

    private string? CurrentMapId()
    {
        return _globals.TryGetValue(CoreServiceKeys.Engine.Name, out var engineObj) &&
            engineObj is GameEngine engine
                ? engine.CurrentMapSession?.MapId.Value
                : null;
    }

    private bool TryGetModeMap(out InteractionModeMap modeMap)
    {
        if (_globals.TryGetValue(CoreServiceKeys.InteractionModeMap.Name, out var mapObj) &&
            mapObj is InteractionModeMap resolved)
        {
            modeMap = resolved;
            return true;
        }

        modeMap = null!;
        return false;
    }

    private void SetMode(string modeId)
    {
        if (!ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out Entity rep) || !_world.IsAlive(rep))
        {
            return;
        }

        if (!TryGetModeMap(out var modeMap) ||
            !modeMap.ModeIdRegistry.TryGetId(modeId, out int registeredModeId))
        {
            throw new InvalidOperationException(
                $"Capability standard virtual camera showcase requires interaction mode '{modeId}'.");
        }

        var component = new InteractionMode { ModeId = registeredModeId };
        if (_world.TryGet<InteractionMode>(rep, out InteractionMode existing))
        {
            _world.Set(rep, component);
        }
        else
        {
            _world.Add(rep, component);
        }
    }
}
