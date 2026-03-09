using System;
using System.Collections.Generic;
using Arch.Core;
using CoreInputMod.Systems;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Camera.FollowTargets;
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

            ApplyCamera(next);
            ApplyInteractionMode(next);
            ApplySkillBar(next);
            _globals[ActiveModeIdKey] = next.Id;
        }

        private void ApplyCamera(ViewModeConfig mode)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.CameraPresetRegistry.Name, out var presetObj) || presetObj is not CameraPresetRegistry presetRegistry)
            {
                return;
            }

            if (!presetRegistry.TryGet(mode.CameraPresetId, out var preset) || preset == null)
            {
                return;
            }

            _camera.ClearVirtualCamera();
            _camera.ApplyPreset(preset, BuildFollowTarget(mode.FollowTargetKind));

            TrySeedFollowTargetPosition(mode.FollowTargetKind);
        }

        private ICameraFollowTarget? BuildFollowTarget(string followTargetKind)
        {
            if (!Enum.TryParse<CameraFollowTargetKind>(followTargetKind, ignoreCase: true, out var kind))
            {
                kind = CameraFollowTargetKind.None;
            }

            return kind switch
            {
                CameraFollowTargetKind.None => null,
                CameraFollowTargetKind.LocalPlayer => new GlobalEntityFollowTarget(_world, _globals, CoreServiceKeys.LocalPlayerEntity.Name),
                CameraFollowTargetKind.SelectedEntity => new GlobalEntityFollowTarget(_world, _globals, CoreServiceKeys.SelectedEntity.Name),
                CameraFollowTargetKind.SelectedOrLocalPlayer => new FallbackChainFollowTarget(
                    new GlobalEntityFollowTarget(_world, _globals, CoreServiceKeys.SelectedEntity.Name),
                    new GlobalEntityFollowTarget(_world, _globals, CoreServiceKeys.LocalPlayerEntity.Name)),
                _ => null
            };
        }

        private void TrySeedFollowTargetPosition(string followTargetKind)
        {
            string? globalKey = followTargetKind switch
            {
                "LocalPlayer" => CoreServiceKeys.LocalPlayerEntity.Name,
                "SelectedEntity" => CoreServiceKeys.SelectedEntity.Name,
                "SelectedOrLocalPlayer" => ResolveSelectedOrLocalPlayer(),
                _ => null
            };

            if (globalKey == null)
            {
                return;
            }

            if (_globals.TryGetValue(globalKey, out var entityObj) && entityObj is Entity entity && _world.IsAlive(entity) && _world.Has<WorldPositionCm>(entity))
            {
                var position = _world.Get<WorldPositionCm>(entity).Value;
                _camera.FollowTargetPositionCm = new System.Numerics.Vector2(position.X.ToFloat(), position.Y.ToFloat());
            }
        }

        private string ResolveSelectedOrLocalPlayer()
        {
            if (_globals.TryGetValue(CoreServiceKeys.SelectedEntity.Name, out var selectedObj) && selectedObj is Entity selected && _world.IsAlive(selected))
            {
                return CoreServiceKeys.SelectedEntity.Name;
            }

            return CoreServiceKeys.LocalPlayerEntity.Name;
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
