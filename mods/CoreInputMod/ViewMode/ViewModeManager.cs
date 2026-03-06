using System;
using System.Collections.Generic;
using Arch.Core;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Camera.FollowTargets;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Scripting;

namespace CoreInputMod.ViewMode
{
    /// <summary>
    /// Orchestrates runtime ViewMode switching by composing existing Core atomic capabilities:
    /// CameraPresetRegistry, CameraControllerFactory, PlayerInputHandler context stack,
    /// InputOrderMappingSystem interaction mode, and SkillBarOverlaySystem globals.
    /// </summary>
    public sealed class ViewModeManager
    {
        public const string GlobalKey = "CoreInputMod.ViewModeManager";
        public const string ActiveModeIdKey = "CoreInputMod.ActiveViewModeId";

        private readonly List<ViewModeConfig> _modes = new();
        private readonly Dictionary<string, ViewModeConfig> _modeMap = new();
        private readonly Dictionary<string, object> _globals;
        private readonly World _world;
        private readonly Ludots.Core.Gameplay.Camera.CameraManager _camera;

        private int _activeIndex = -1;

        public ViewModeConfig? ActiveMode => _activeIndex >= 0 && _activeIndex < _modes.Count
            ? _modes[_activeIndex] : null;
        public IReadOnlyList<ViewModeConfig> Modes => _modes;

        public ViewModeManager(World world, Dictionary<string, object> globals,
            Ludots.Core.Gameplay.Camera.CameraManager camera)
        {
            _world = world;
            _globals = globals;
            _camera = camera;
        }

        public void Register(ViewModeConfig mode)
        {
            if (_modeMap.ContainsKey(mode.Id)) return;
            _modes.Add(mode);
            _modeMap[mode.Id] = mode;
        }

        public bool SwitchTo(string modeId)
        {
            if (!_modeMap.TryGetValue(modeId, out var target)) return false;
            int idx = _modes.IndexOf(target);
            if (idx == _activeIndex) return true;

            var prev = ActiveMode;
            _activeIndex = idx;

            ApplyViewMode(prev, target);
            return true;
        }

        public bool SwitchNext()
        {
            if (_modes.Count == 0) return false;
            int next = (_activeIndex + 1) % _modes.Count;
            return SwitchTo(_modes[next].Id);
        }

        public bool SwitchPrev()
        {
            if (_modes.Count == 0) return false;
            int prev = _activeIndex <= 0 ? _modes.Count - 1 : _activeIndex - 1;
            return SwitchTo(_modes[prev].Id);
        }

        private void ApplyViewMode(ViewModeConfig? prev, ViewModeConfig next)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.InputHandler.Name, out var io) ||
                io is not PlayerInputHandler input) return;

            if (prev != null && !string.IsNullOrEmpty(prev.InputContextId))
                input.PopContext(prev.InputContextId);
            if (!string.IsNullOrEmpty(next.InputContextId))
                input.PushContext(next.InputContextId);

            ApplyCamera(next);
            ApplyInteractionMode(next);
            ApplySkillBar(next);

            _globals[ActiveModeIdKey] = next.Id;
        }

        private void ApplyCamera(ViewModeConfig mode)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.CameraPresetRegistry.Name, out var pr) ||
                pr is not CameraPresetRegistry presetReg) return;
            if (!presetReg.TryGet(mode.CameraPresetId, out var preset)) return;

            _camera.State.DistanceCm = preset.DistanceCm;
            _camera.State.Pitch = preset.Pitch;
            _camera.State.FovYDeg = preset.FovYDeg;
            _camera.State.Yaw = preset.Yaw;
            _camera.FollowMode = preset.FollowMode;

            if (_globals.TryGetValue(CoreServiceKeys.InputHandler.Name, out var io) &&
                io is PlayerInputHandler input &&
                _globals.TryGetValue(CoreServiceKeys.ViewController.Name, out var vc) &&
                vc is IViewController viewport)
            {
                var ctx = new CameraBehaviorContext(input, viewport);
                var controller = CameraControllerFactory.FromPreset(preset, ctx);
                _camera.SetController(controller);
            }

            var followKind = FollowTargetKindParser.Parse(mode.FollowTargetKind);
            _camera.FollowTarget = followKind switch
            {
                FollowTargetKind.LocalPlayer => CreateEntityFollowTarget(CoreServiceKeys.LocalPlayerEntity.Name),
                FollowTargetKind.SelectedEntity => CreateEntityFollowTarget(CoreServiceKeys.SelectedEntity.Name),
                FollowTargetKind.SelectedOrLocalPlayer => new SelectedEntityFollowTarget(_world, _globals),
                _ => null
            };
        }

        private ICameraFollowTarget? CreateEntityFollowTarget(string globalKey)
        {
            if (_globals.TryGetValue(globalKey, out var obj) && obj is Arch.Core.Entity e && _world.IsAlive(e))
                return new EntityFollowTarget(_world, e);
            return null;
        }

        private void ApplyInteractionMode(ViewModeConfig mode)
        {
            if (!Enum.TryParse<InteractionModeType>(mode.InteractionMode, true, out var modeType))
                return;

            if (_globals.TryGetValue(LocalOrderSourceHelper.ActiveMappingKey, out var m) &&
                m is InputOrderMappingSystem mapping)
            {
                mapping.SetInteractionMode(modeType);
            }
        }

        private void ApplySkillBar(ViewModeConfig mode)
        {
            if (mode.SkillBarKeyLabels != null)
                _globals[SkillBarOverlaySystem.SkillBarKeyLabelsKey] = mode.SkillBarKeyLabels;
            _globals[SkillBarOverlaySystem.SkillBarEnabledKey] = mode.SkillBarEnabled;
        }
    }
}
