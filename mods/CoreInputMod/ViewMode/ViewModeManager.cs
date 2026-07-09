using System;
using System.Collections.Generic;
using Arch.Core;
using CoreInputMod.Systems;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.CommandSources;
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
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private int _activeIndex = -1;

        public ViewModeConfig? ActiveMode => _activeIndex >= 0 && _activeIndex < _modes.Count ? _modes[_activeIndex] : null;
        public IReadOnlyList<ViewModeConfig> Modes => _modes;

        public ViewModeManager(World world, Dictionary<string, object> globals)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
        }

        public void Register(ViewModeConfig mode)
        {
            ArgumentNullException.ThrowIfNull(mode);
            if (string.IsNullOrWhiteSpace(mode.Id))
            {
                throw new InvalidOperationException("ViewMode config requires a non-empty Id.");
            }

            if (_modeMap.ContainsKey(mode.Id))
            {
                throw new InvalidOperationException($"ViewMode '{mode.Id}' is registered more than once.");
            }

            RequireInteractionMode(mode);
            if (!string.IsNullOrWhiteSpace(mode.FollowTargetKind) &&
                !Enum.TryParse<CameraFollowTargetKind>(mode.FollowTargetKind, ignoreCase: true, out _))
            {
                throw new InvalidOperationException(
                    $"ViewMode '{mode.Id}' declared unsupported FollowTargetKind '{mode.FollowTargetKind}'.");
            }

            _modes.Add(mode);
            _modeMap[mode.Id] = mode;
        }

        public bool SwitchTo(string modeId)
        {
            return SwitchTo(modeId, applyCamera: true);
        }

        public bool SwitchTo(string modeId, bool applyCamera)
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
            ApplyViewMode(previous, target, applyCamera);
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

            _activeIndex = -1;
            _globals.Remove(ActiveModeIdKey);
            _globals.Remove(SkillBarOverlaySystem.SkillBarKeyLabelsKey);
            _globals[SkillBarOverlaySystem.SkillBarEnabledKey] = true;
        }

        private void ApplyViewMode(ViewModeConfig? previous, ViewModeConfig next, bool applyCamera)
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

            if (applyCamera)
            {
                ApplyCamera(next);
            }

            ApplyInteractionMode(next);
            ApplySkillBar(next);
            _globals[ActiveModeIdKey] = next.Id;
        }

        private void ApplyCamera(ViewModeConfig next)
        {
            if (string.IsNullOrWhiteSpace(next.VirtualCameraId))
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.VirtualCameraRegistry.Name, out var registryObj) ||
                registryObj is not VirtualCameraRegistry registry)
            {
                throw new InvalidOperationException(
                    $"ViewMode '{next.Id}' declared virtual camera '{next.VirtualCameraId}', but VirtualCameraRegistry is not available.");
            }

            if (!registry.TryGet(next.VirtualCameraId, out var definition) || definition == null)
            {
                throw new InvalidOperationException(
                    $"ViewMode '{next.Id}' declared unknown virtual camera '{next.VirtualCameraId}'.");
            }

            if (!Enum.TryParse<CameraFollowTargetKind>(next.FollowTargetKind, ignoreCase: true, out var followTargetKind))
            {
                throw new InvalidOperationException(
                    $"ViewMode '{next.Id}' declared unsupported FollowTargetKind '{next.FollowTargetKind}'.");
            }

            var request = new VirtualCameraRequest
            {
                Id = next.VirtualCameraId,
                FollowTargetKindOverride = followTargetKind,
                FollowCollectionOwnerOverride = ResolveFollowCollectionOwner(next.Id, followTargetKind),
                FollowCollectionKeyOverride = string.IsNullOrWhiteSpace(next.FollowCollectionKey)
                    ? definition.FollowCollectionKey
                    : next.FollowCollectionKey,
                SnapToFollowTargetWhenAvailable = definition.SnapToFollowTargetWhenAvailable,
                ResetRuntimeState = true,
                ReplaceActiveStack = true
            };

            _globals[CoreServiceKeys.VirtualCameraRequest.Name] = request;
        }

        private Entity ResolveFollowCollectionOwner(string modeId, CameraFollowTargetKind followTargetKind)
        {
            if (!CameraFollowTargetFactory.RequiresEntityCollection(followTargetKind))
            {
                return Entity.Null;
            }

            if (TryResolveLocalCommandSourceOwner(out Entity owner))
            {
                return owner;
            }

            throw new InvalidOperationException(
                $"ViewMode '{modeId}' requires an explicit entity collection owner before activating collection camera follow.");
        }

        private bool TryResolveLocalCommandSourceOwner(out Entity owner)
        {
            owner = Entity.Null;
            return _globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) &&
                   localObj is Entity local &&
                   local != Entity.Null &&
                   _world.IsAlive(local) &&
                   (owner = local) != Entity.Null;
        }

        private void ApplyInteractionMode(ViewModeConfig mode)
        {
            InteractionModeType interactionMode = RequireInteractionMode(mode);

            if (_globals.TryGetValue(CoreServiceKeys.ActiveInputOrderMapping.Name, out var mappingObj) && mappingObj is InputOrderMappingSystem mapping)
            {
                mapping.SetInteractionMode(interactionMode);
            }
        }

        private static InteractionModeType RequireInteractionMode(ViewModeConfig mode)
        {
            if (string.IsNullOrWhiteSpace(mode.InteractionMode))
            {
                throw new InvalidOperationException(
                    $"ViewMode '{mode.Id}' must declare InteractionMode explicitly.");
            }

            if (!Enum.TryParse<InteractionModeType>(mode.InteractionMode, true, out var interactionMode))
            {
                throw new InvalidOperationException(
                    $"ViewMode '{mode.Id}' declared unsupported InteractionMode '{mode.InteractionMode}'.");
            }

            return interactionMode;
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
