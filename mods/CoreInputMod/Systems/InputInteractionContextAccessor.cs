using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
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
        private readonly SelectionRuntime? _selection;

        public InputInteractionContextAccessor(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
            _selection = globals.TryGetValue(CoreServiceKeys.SelectionRuntime.Name, out var selectionObj) &&
                         selectionObj is SelectionRuntime selection
                ? selection
                : null;
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
            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) ||
                inputObj is not IInputActionReader input)
            {
                return false;
            }

            if (AuthoritativeGroundPointerHelper.TryRead(input, out worldCm))
            {
                return true;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.ScreenRayProvider.Name, out var rayProviderObj) ||
                rayProviderObj is not IScreenRayProvider rayProvider ||
                !_globals.TryGetValue(CoreServiceKeys.WorldSizeSpec.Name, out var worldSizeObj) ||
                worldSizeObj is not WorldSizeSpec worldSize)
            {
                return false;
            }

            var ray = rayProvider.GetRay(input.ReadAction<Vector2>("PointerPos"));
            return GroundRaycastUtil.TryGetGroundWorldCmBounded(in ray, worldSize, out worldCm);
        }

        public bool TryGetPointerScreenPosition(out Vector2 screenPosition)
        {
            screenPosition = default;
            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) ||
                inputObj is not IInputActionReader input)
            {
                return false;
            }

            screenPosition = input.ReadAction<Vector2>("PointerPos");
            return true;
        }

        public Entity GetControlledActor(int playerId = 1)
        {
            if (TryGetSelectedEntity(SelectionSetKeys.Ambient, out var selected) &&
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

        public bool TryGetSelectedEntity(string setKey, out Entity entity)
        {
            entity = default;
            if (_selection == null || !TryGetSelectionOwner(out var owner))
            {
                return false;
            }

            return _selection.TryGetPrimary(owner, setKey, out entity);
        }

        public bool TryGetSelectedContainer(string setKey, out Entity container)
        {
            container = default;
            if (_selection == null || !TryGetSelectionOwner(out var owner))
            {
                return false;
            }

            return _selection.TryCreateSnapshotLease(owner, setKey, SelectionSetKeys.CommandSnapshot, SelectionContainerKind.Snapshot, out _, out container);
        }

        public bool TryGetSelectedEntities(string setKey, List<Entity> entities)
        {
            entities.Clear();
            if (_selection == null || !TryGetSelectionOwner(out var owner))
            {
                return false;
            }

            int selectionCount = _selection.GetSelectionCount(owner, setKey);
            if (selectionCount <= 0)
            {
                return false;
            }

            Entity[] selected = new Entity[selectionCount];
            int count = _selection.CopySelection(owner, setKey, selected);
            for (int i = 0; i < count; i++)
            {
                Entity entity = selected[i];
                if (_world.IsAlive(entity))
                {
                    entities.Add(entity);
                }
            }

            return entities.Count > 0;
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
