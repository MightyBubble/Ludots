using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.EntityCollections;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.EntityView;
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
            if (TryGetCommandSourceEntities(out List<Entity> entities))
            {
                entity = entities[0];
                return true;
            }

            if (!TryGetSelectionOwner(out var owner))
            {
                return false;
            }

            return _selection.TryGetPrimary(owner, setKey, out entity);
        }

        public bool TryGetCommandSourceHandle(out Entity owner, out EntityCollectionHandle handle)
        {
            owner = default;
            handle = EntityCollectionHandle.Invalid;
            if (!_globals.TryGetValue(CoreServiceKeys.EntityViewConfig.Name, out object? configObj) ||
                configObj is not EntityViewRuntimeConfig config)
            {
                return false;
            }

            return EntityViewRuntime.TryGetCommandSourceHandle(_world, _globals, config, out owner, out handle);
        }

        public bool TryGetCommandSourceEntities(out List<Entity> entities)
        {
            entities = new List<Entity>();
            if (!_globals.TryGetValue(CoreServiceKeys.EntityViewConfig.Name, out object? configObj) ||
                configObj is not EntityViewRuntimeConfig config ||
                !_globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out object? storeObj) ||
                storeObj is not EntityCollectionStore collections ||
                !EntityViewRuntime.TryGetCommandSourceHandle(_world, _globals, config, out _, out EntityCollectionHandle handle) ||
                !collections.TryGetView(handle, out EntityCollectionView view) ||
                view.Count <= 0)
            {
                return false;
            }

            Entity[] scratch = new Entity[view.Count];
            int written = collections.CopyEntities(handle, 0, scratch);
            for (int i = 0; i < written; i++)
            {
                Entity candidate = scratch[i];
                if (_world.IsAlive(candidate))
                {
                    entities.Add(candidate);
                }
            }

            return entities.Count > 0;
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
            if (TryGetCommandSourceEntities(out List<Entity> commandSourceEntities))
            {
                entities.Clear();
                entities.AddRange(commandSourceEntities);
                return entities.Count > 0;
            }

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
