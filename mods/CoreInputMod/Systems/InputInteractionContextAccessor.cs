using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace CoreInputMod.Systems
{
    internal sealed class InputInteractionContextAccessor
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;

        public InputInteractionContextAccessor(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
        }

        public bool TryGetEntity(string key, out Entity entity)
        {
            entity = default;
            if (!_globals.TryGetValue(key, out var value) || value is not Entity candidate || !_world.IsAlive(candidate))
            {
                return false;
            }

            entity = candidate;
            return true;
        }

        public bool TryGetSelectionOwner(out Entity owner)
        {
            owner = default;
            return _globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
                   localObj is Entity local &&
                   _world.IsAlive(local) &&
                   (owner = local) != Entity.Null;
        }

        public bool TryGetGroundWorldCm(out WorldCmInt2 worldCm)
        {
            worldCm = default;
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenRayProvider.Name, out var rayProviderObj) ||
                rayProviderObj is not IScreenRayProvider rayProvider)
            {
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) ||
                inputObj is not IInputActionReader input)
            {
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.WorldSizeSpec.Name, out var worldSizeObj) ||
                worldSizeObj is not WorldSizeSpec worldSize)
            {
                return false;
            }

            var ray = rayProvider.GetRay(input.ReadAction<Vector2>("PointerPos"));
            return GroundRaycastUtil.TryGetGroundWorldCmBounded(in ray, worldSize, out worldCm);
        }

        public Entity GetControlledActor(int playerId = 1)
        {
            if (_globals.TryGetValue(CoreServiceKeys.SelectedEntity.Name, out var selectedObj) &&
                selectedObj is Entity selected &&
                _world.IsAlive(selected) &&
                _world.TryGet(selected, out PlayerOwner owner) &&
                owner.PlayerId == playerId)
            {
                return selected;
            }

            if (_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
                localObj is Entity local &&
                _world.IsAlive(local))
            {
                return local;
            }

            return default;
        }

        public bool TryCreateAbilityIndicatorBridge(out AbilityIndicatorOverlayBridge bridge)
        {
            bridge = default!;
            if (!_globals.TryGetValue(CoreServiceKeys.AbilityDefinitionRegistry.Name, out var abilitiesObj) ||
                abilitiesObj is not AbilityDefinitionRegistry abilities ||
                !_globals.TryGetValue(CoreServiceKeys.GroundOverlayBuffer.Name, out var overlaysObj) ||
                overlaysObj is not GroundOverlayBuffer overlays)
            {
                return false;
            }

            bridge = new AbilityIndicatorOverlayBridge(_world, abilities, overlays);
            return true;
        }
    }
}
