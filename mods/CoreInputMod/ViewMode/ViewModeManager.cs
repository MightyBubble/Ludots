using System;
using System.Collections.Generic;
using Arch.Core;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace CoreInputMod.ViewMode
{
    public sealed class ViewModeManager
    {
        public const string GlobalKey = "CoreInputMod.ViewModeManager";
        public const string ActiveModeIdKey = "CoreInputMod.ActiveViewModeId";

        private readonly List<ViewModeConfig> _modes = new();
        private readonly Dictionary<string, ViewModeConfig> _modeMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> _globals;
        private readonly World _world;
        private readonly CameraManager _camera;
        private int _activeIndex = -1;
        private string? _ownedVirtualCameraId;

        public ViewModeConfig? ActiveMode => _activeIndex >= 0 && _activeIndex < _modes.Count ? _modes[_activeIndex] : null;
        public IReadOnlyList<ViewModeConfig> Modes => _modes;

        public ViewModeManager(World world, Dictionary<string, object> globals, CameraManager camera)
        {
            _world = world;
            _globals = globals;
            _camera = camera;
        }

        public void Register(ViewModeConfig mode)
        {
            if (string.IsNullOrWhiteSpace(mode.Id) || _modeMap.ContainsKey(mode.Id))
            {
                return;
            }

            _modes.Add(mode);
            _modeMap[mode.Id] = mode;
        }

        public bool SwitchTo(string modeId)
        {
            if (!_modeMap.TryGetValue(modeId, out var target))
            {
                return false;
            }

            int nextIndex = _modes.IndexOf(target);
            if (nextIndex == _activeIndex)
            {
                return true;
            }

            var previous = ActiveMode;
            _activeIndex = nextIndex;
            ApplyViewMode(previous, target);
            return true;
        }

        public bool SwitchNext()
        {
            if (_modes.Count == 0)
            {
                return false;
            }

            int nextIndex = (_activeIndex + 1) % _modes.Count;
            return SwitchTo(_modes[nextIndex].Id);
        }

        public bool SwitchPrev()
        {
            if (_modes.Count == 0)
            {
                return false;
            }

            int prevIndex = _activeIndex <= 0 ? _modes.Count - 1 : _activeIndex - 1;
            return SwitchTo(_modes[prevIndex].Id);
        }

        public void ClearActiveMode()
        {
            var previous = ActiveMode;
            if (_globals.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) && inputObj is PlayerInputHandler input)
            {
                if (previous != null && !string.IsNullOrWhiteSpace(previous.InputContextId))
                {
                    input.PopContext(previous.InputContextId);
                }
            }

            if (!string.IsNullOrWhiteSpace(_ownedVirtualCameraId))
            {
                _camera.DeactivateVirtualCamera(_ownedVirtualCameraId);
                _ownedVirtualCameraId = null;
            }

            _activeIndex = -1;
            _globals.Remove(ActiveModeIdKey);
            _globals.Remove(SkillBarOverlaySystem.SkillBarKeyLabelsKey);
            _globals[SkillBarOverlaySystem.SkillBarEnabledKey] = true;
        }

        private void ApplyViewMode(ViewModeConfig? previous, ViewModeConfig next)
        {
            if (_globals.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) && inputObj is PlayerInputHandler input)
            {
                if (previous != null && !string.IsNullOrWhiteSpace(previous.InputContextId))
                {
                    input.PopContext(previous.InputContextId);
                }

                if (!string.IsNullOrWhiteSpace(next.InputContextId))
                {
                    input.PushContext(next.InputContextId);
                }
            }

            ApplyCamera(previous, next);
            ApplyInteractionMode(next);
            ApplySkillBar(next);
            _globals[ActiveModeIdKey] = next.Id;
        }

        private void ApplyCamera(ViewModeConfig? previous, ViewModeConfig next)
        {
            if (!string.IsNullOrWhiteSpace(_ownedVirtualCameraId))
            {
                _camera.DeactivateVirtualCamera(_ownedVirtualCameraId);
                _ownedVirtualCameraId = null;
            }
            else if (previous != null &&
                     !string.IsNullOrWhiteSpace(previous.VirtualCameraId) &&
                     !string.Equals(previous.VirtualCameraId, next.VirtualCameraId, StringComparison.OrdinalIgnoreCase) &&
                     _camera.IsVirtualCameraActive(previous.VirtualCameraId))
            {
                // The previous mode may be the authoritative camera inherited from map default.
                // In that case we leave it active and only swap the top mode-owned camera.
            }

            if (string.IsNullOrWhiteSpace(next.VirtualCameraId))
            {
                return;
            }

            if (_camera.IsVirtualCameraActive(next.VirtualCameraId))
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.VirtualCameraRegistry.Name, out var registryObj) ||
                registryObj is not VirtualCameraRegistry registry ||
                !registry.TryGet(next.VirtualCameraId, out var definition) ||
                definition == null)
            {
                return;
            }

            if (!Enum.TryParse<CameraFollowTargetKind>(next.FollowTargetKind, ignoreCase: true, out var followTargetKind))
            {
                throw new InvalidOperationException(
                    $"ViewMode '{next.Id}' declared unsupported FollowTargetKind '{next.FollowTargetKind}'.");
            }

            _camera.ActivateVirtualCamera(
                next.VirtualCameraId,
                blendDurationSeconds: null,
                followTarget: CameraFollowTargetFactory.Build(_world, _globals, followTargetKind),
                snapToFollowTargetWhenAvailable: definition.SnapToFollowTargetWhenAvailable);
            _ownedVirtualCameraId = next.VirtualCameraId;
        }

        private void ApplyInteractionMode(ViewModeConfig mode)
        {
            if (!Enum.TryParse<InteractionModeType>(mode.InteractionMode, true, out var interactionMode))
            {
                return;
            }

            if (_globals.TryGetValue(LocalOrderSourceHelper.ActiveMappingKey, out var mappingObj) && mappingObj is InputOrderMappingSystem mapping)
            {
                mapping.SetInteractionMode(interactionMode);
            }
        }

        private void ApplySkillBar(ViewModeConfig mode)
        {
            if (mode.SkillBarKeyLabels != null)
            {
                _globals[SkillBarOverlaySystem.SkillBarKeyLabelsKey] = mode.SkillBarKeyLabels;
            }

            _globals[SkillBarOverlaySystem.SkillBarEnabledKey] = mode.SkillBarEnabled;
        }
    }
}
