using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
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
        private const int InitialEntityScratchCapacity = 16;

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly EntityCollectionStore? _entityCollections;
        private readonly InteractionContextStack? _interactionContextStack;
        private Entity[] _selectedScratch = new Entity[InitialEntityScratchCapacity];

        public InputInteractionContextAccessor(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
            _entityCollections = globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out var collectionsObj) &&
                                 collectionsObj is EntityCollectionStore collections
                ? collections
                : null;
            _interactionContextStack = globals.TryGetValue(CoreServiceKeys.InteractionContextStack.Name, out var stackObj) &&
                                       stackObj is InteractionContextStack stack
                ? stack
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

            if (TryGetCommandSourcePrimary(out var commandSourcePrimary) &&
                _world.IsAlive(commandSourcePrimary) &&
                _world.TryGet(commandSourcePrimary, out PlayerOwner commandSourceOwner) &&
                commandSourceOwner.PlayerId == playerId)
            {
                return commandSourcePrimary;
            }

            if (TryGetSelectedEntity(EntityCollectionKeys.CommandSource, out var selected) &&
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
            return EntityCollectionContextRuntime.TryGetCurrentPrimary(_world, _globals, out entity);
        }

        public bool TryGetSelectedEntities(string setKey, List<Entity> entities)
        {
            entities.Clear();
            int selectionCount = EntityCollectionContextRuntime.GetCurrentCount(_world, _globals);
            if (selectionCount <= 0)
            {
                return false;
            }

            EnsureSelectedScratch(selectionCount);
            int count = EntityCollectionContextRuntime.CopyCurrent(_world, _globals, _selectedScratch);
            for (int i = 0; i < count; i++)
            {
                Entity entity = _selectedScratch[i];
                if (_world.IsAlive(entity))
                {
                    entities.Add(entity);
                }
            }

            return entities.Count > 0;
        }

        private void EnsureSelectedScratch(int required)
        {
            if (_selectedScratch.Length >= required)
            {
                return;
            }

            int next = _selectedScratch.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _selectedScratch, next);
        }

        public bool TryGetCommandSourceOwner(out Entity owner)
        {
            owner = default;
            if (_interactionContextStack != null &&
                _interactionContextStack.TryPeek(out InteractionContextFrame frame) &&
                HasEntityValue(frame.ContextEntity))
            {
                if (!_world.IsAlive(frame.ContextEntity))
                {
                    return false;
                }

                owner = frame.ContextEntity;
                return true;
            }

            return TryGetSelectionOwner(out owner);
        }

        private static bool HasEntityValue(Entity entity)
        {
            return entity.Id != 0 || entity.WorldId != 0 || entity.Version != 0;
        }

        public bool TryGetCommandSourcePrimary(out Entity entity)
        {
            entity = default;
            if (!TryResolveActiveCommandSource(out EntityCollectionHandle handle) ||
                _entityCollections == null ||
                !_entityCollections.TryGetEntityAt(handle, 0, out Entity candidate) ||
                !_world.IsAlive(candidate))
            {
                return false;
            }

            entity = candidate;
            return true;
        }

        public bool TryCopyCommandSourceEntities(List<Entity> entities)
        {
            entities.Clear();
            if (!TryResolveActiveCommandSource(out EntityCollectionHandle handle) ||
                _entityCollections == null ||
                !_entityCollections.TryGetView(handle, out EntityCollectionView view) ||
                view.Count <= 0)
            {
                return false;
            }

            for (int i = 0; i < view.Count; i++)
            {
                if (!_entityCollections.TryGetEntityAt(handle, i, out Entity entity))
                {
                    continue;
                }

                if (_world.IsAlive(entity))
                {
                    entities.Add(entity);
                }
            }

            return entities.Count > 0;
        }

        private bool TryResolveActiveCommandSource(out EntityCollectionHandle handle)
        {
            handle = EntityCollectionHandle.Invalid;
            return _entityCollections != null &&
                   _interactionContextStack != null &&
                   TryGetCommandSourceOwner(out Entity owner) &&
                   owner != Entity.Null &&
                   _interactionContextStack.TryPeek(out InteractionContextFrame frame) &&
                   _entityCollections.TryGet(owner, frame.ActiveCollectionKeyId, out handle);
        }

        public bool TryGetHoveredEntity(out Entity entity)
        {
            return EntityCollectionContextRuntime.TryGetHovered(_world, _globals, out entity);
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
