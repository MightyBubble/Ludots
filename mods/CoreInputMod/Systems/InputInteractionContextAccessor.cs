using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;

namespace CoreInputMod.Systems
{
    internal sealed class InputInteractionContextAccessor
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly SelectionRuntime _selection;

        public InputInteractionContextAccessor(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
            _selection = globals.TryGetValue(CoreServiceKeys.SelectionRuntime.Name, out var selectionObj) &&
                         selectionObj is SelectionRuntime selection
                ? selection
                : throw new InvalidOperationException(
                    $"{nameof(InputInteractionContextAccessor)} requires {CoreServiceKeys.SelectionRuntime.Name} to be registered.");
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

            return AuthoritativeGroundPointerHelper.TryRead(input, out worldCm);
        }

        public Entity GetControlledActor(int playerId = 1)
        {
            if (TryGetSelectedEntity(SelectionSetKeys.LivePrimary, out var selected) &&
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
            if (!TryGetSelectionOwner(out var owner))
            {
                return false;
            }

            return _selection.TryGetPrimary(owner, setKey, out entity);
        }

        public bool TryGetSelectedContainer(string setKey, out Entity container)
        {
            container = default;
            if (!TryGetSelectionOwner(out var owner))
            {
                return false;
            }

            return _selection.TryCreateSnapshotLease(owner, setKey, SelectionSetKeys.CommandSnapshot, SelectionContainerKind.Snapshot, out _, out container);
        }

        public bool TryGetSelectedEntities(string setKey, List<Entity> entities)
        {
            entities.Clear();
            if (!TryGetSelectionOwner(out var owner))
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
