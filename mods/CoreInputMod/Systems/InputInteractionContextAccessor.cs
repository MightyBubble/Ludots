using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;

namespace CoreInputMod.Systems
{
    internal sealed class InputInteractionContextAccessor
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly SelectionRuntime _selection;
        private readonly EntityCollectionStore _entityCollections;

        public InputInteractionContextAccessor(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
            _selection = globals.TryGetValue(CoreServiceKeys.SelectionRuntime.Name, out var selectionObj) &&
                         selectionObj is SelectionRuntime selection
                ? selection
                : throw new InvalidOperationException(
                    $"{nameof(InputInteractionContextAccessor)} requires {CoreServiceKeys.SelectionRuntime.Name} to be registered.");
            _entityCollections = globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out var collectionsObj) &&
                                 collectionsObj is EntityCollectionStore collections
                ? collections
                : throw new InvalidOperationException(
                    $"{nameof(InputInteractionContextAccessor)} requires {CoreServiceKeys.EntityCollectionStore.Name} to be registered.");
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

        public bool TryGetLocalPlayerId(out int playerId)
        {
            playerId = 0;
            if (!_globals.TryGetValue(CoreServiceKeys.LocalPlayerId.Name, out object? value) ||
                value is not int candidate ||
                candidate <= 0)
            {
                return false;
            }

            playerId = candidate;
            return true;
        }

        public Entity GetControlledActor(int playerId)
        {
            if (playerId <= 0)
            {
                return default;
            }

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

        public Entity GetLocalPlayerEntityOrNull()
        {
            return _globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
                   localObj is Entity local &&
                   _world.IsAlive(local)
                ? local
                : Entity.Null;
        }

        public bool TryGetSelectedEntity(string setKey, out Entity entity)
        {
            entity = default;
            if (!TryGetSelectionOwner(out var owner) ||
                !CommandSourceCollectionRuntime.TryGetPrimary(_entityCollections, owner, out Entity commandEntity) ||
                !_world.IsAlive(commandEntity))
            {
                return false;
            }

            entity = commandEntity;
            return true;
        }

        public bool TryGetSelectedContainer(string setKey, out Entity container)
        {
            container = default;
            if (!TryGetSelectionOwner(out var owner) ||
                !CommandSourceCollectionRuntime.TryGet(_entityCollections, owner, out _, out EntityCollectionView view) ||
                view.Count <= 0)
            {
                return false;
            }

            var entities = new Entity[view.Count];
            int written = CommandSourceCollectionRuntime.CopyEntities(_entityCollections, owner, entities);
            if (written <= 0)
            {
                return false;
            }

            Entity leaseOwner = _world.Create(default(SelectionLeaseOwnerTag));
            if (!_selection.TryGetOrCreateContainer(leaseOwner, SelectionSetKeys.CommandSnapshot, SelectionContainerKind.Snapshot, out container))
            {
                if (_world.IsAlive(leaseOwner))
                {
                    _world.Destroy(leaseOwner);
                }

                container = default;
                return false;
            }

            if (!_selection.ReplaceSelection(container, entities.AsSpan(0, written)))
            {
                if (_world.IsAlive(leaseOwner))
                {
                    _world.Destroy(leaseOwner);
                }

                container = default;
                return false;
            }

            _world.Add(leaseOwner, new SelectionLeaseContainer { Value = container });
            return true;
        }

        public bool TryGetSelectedEntities(string setKey, List<Entity> entities)
        {
            entities.Clear();
            if (!TryGetSelectionOwner(out var owner) ||
                !CommandSourceCollectionRuntime.TryGet(_entityCollections, owner, out _, out EntityCollectionView view) ||
                view.Count <= 0)
            {
                return false;
            }

            Entity[] selected = new Entity[view.Count];
            int count = CommandSourceCollectionRuntime.CopyEntities(_entityCollections, owner, selected);
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

        public bool TryGetHoveredEntity(out Entity entity)
        {
            return SelectionContextRuntime.TryGetCurrentHovered(_world, _globals, out entity);
        }

        public bool TryGetAbilityDefinitionRegistry(out AbilityDefinitionRegistry registry)
        {
            registry = default!;
            if (_globals.TryGetValue(CoreServiceKeys.AbilityDefinitionRegistry.Name, out var abilitiesObj) &&
                abilitiesObj is AbilityDefinitionRegistry abilities)
            {
                registry = abilities;
                return true;
            }

            return false;
        }

        public bool TryCreateAbilityAimPresentationRuntime(out AbilityAimPresentationRuntime runtime)
        {
            runtime = default!;
            if (!_globals.TryGetValue(CoreServiceKeys.AbilityDefinitionRegistry.Name, out var abilitiesObj) ||
                abilitiesObj is not AbilityDefinitionRegistry abilities ||
                !_globals.TryGetValue(CoreServiceKeys.EffectTemplateRegistry.Name, out var effectsObj) ||
                effectsObj is not EffectTemplateRegistry effects ||
                !_globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out var collectionsObj) ||
                collectionsObj is not EntityCollectionStore collections ||
                !_globals.TryGetValue(CoreServiceKeys.SpatialQueryService.Name, out var spatialObj) ||
                spatialObj is not ISpatialQueryService spatialQueries ||
                !_globals.TryGetValue(CoreServiceKeys.PresentationEventStream.Name, out var eventsObj) ||
                eventsObj is not PresentationEventStream events)
            {
                return false;
            }

            GameSession? session = _globals.TryGetValue(CoreServiceKeys.GameSession.Name, out var sessionObj) &&
                                   sessionObj is GameSession resolvedSession
                ? resolvedSession
                : null;
            GraphProgramRegistry? graphPrograms = _globals.TryGetValue(CoreServiceKeys.GraphProgramRegistry.Name, out var graphProgramsObj) &&
                                                   graphProgramsObj is GraphProgramRegistry resolvedGraphPrograms
                ? resolvedGraphPrograms
                : null;
            GasGraphRuntimeApi? graphApi = null;
            if (graphPrograms != null &&
                _globals.TryGetValue(CoreServiceKeys.SpatialCoordinateConverter.Name, out var coordsObj) &&
                coordsObj is ISpatialCoordinateConverter spatialCoords &&
                HasProductionGraphServices())
            {
                graphApi = GasGraphRuntimeApi.CreateProduction(
                    _world,
                    spatialQueries,
                    spatialCoords,
                    eventBus: null,
                    effectRequests: null,
                    _globals);
                if (_globals.TryGetValue(CoreServiceKeys.LoadedGraphRuntime.Name, out var graphRuntimeObj) &&
                    graphRuntimeObj is LoadedGraphRuntime graphRuntime)
                {
                    graphApi.BindLoadedGraphRuntime(graphRuntime);
                }
            }

            runtime = new AbilityAimPresentationRuntime(
                _world,
                abilities,
                effects,
                collections,
                spatialQueries,
                events,
                session,
                graphPrograms,
                graphApi);
            return true;
        }

        private bool HasProductionGraphServices()
        {
            return _globals.ContainsKey(CoreServiceKeys.TagOps.Name) &&
                   _globals.ContainsKey(CoreServiceKeys.RelationshipRuntime.Name) &&
                   _globals.ContainsKey(CoreServiceKeys.RelationshipTypeRegistry.Name) &&
                   _globals.ContainsKey(CoreServiceKeys.RelationshipMetricRegistry.Name) &&
                   _globals.ContainsKey(CoreServiceKeys.RelationshipFlagRegistry.Name) &&
                   _globals.ContainsKey(CoreServiceKeys.RelationshipReasonRegistry.Name) &&
                   _globals.ContainsKey(CoreServiceKeys.TargetDispatchPresetRegistry.Name) &&
                   _globals.ContainsKey(CoreServiceKeys.EntityCollectionStore.Name) &&
                   _globals.ContainsKey(CoreServiceKeys.EntitySetQueryRuntime.Name);
        }
    }
}
