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
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace CoreInputMod.Systems
{
    internal sealed class InputInteractionContextAccessor
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly EntityCollectionStore? _entityCollections;
        private readonly Entity[] _collectionScratch;

        public InputInteractionContextAccessor(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
            _collectionScratch = new Entity[ResolveCommandIntentScratchCapacity(globals)];
            _entityCollections = globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out var collectionsObj) &&
                                 collectionsObj is EntityCollectionStore collections
                ? collections
                : null;
        }

        private static int ResolveCommandIntentScratchCapacity(Dictionary<string, object> globals)
        {
            if (!globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out object? configObj) ||
                configObj is not Ludots.Core.Config.GameConfig gameConfig)
            {
                throw new InvalidOperationException(
                    $"{nameof(InputInteractionContextAccessor)} requires {CoreServiceKeys.GameConfig.Name} with gasRuntimeCapacity.commandIntentScratchCapacity.");
            }

            Ludots.Core.Config.GasRuntimeCapacityConfig capacity = gameConfig.GasRuntimeCapacity
                ?? throw new InvalidOperationException(
                    $"{nameof(InputInteractionContextAccessor)} requires GameConfig.gasRuntimeCapacity.");
            if (capacity.CommandIntentScratchCapacity <= 0)
            {
                throw new InvalidOperationException(
                    "GameConfig.gasRuntimeCapacity.commandIntentScratchCapacity must be positive.");
            }

            return capacity.CommandIntentScratchCapacity;
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

        public bool TryResolveLocalCommandSourceOwner(out Entity owner)
        {
            owner = default;
            return ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out Entity local) &&
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

        public bool TryGetSolePossessedPlayerId(out int playerId)
        {
            playerId = 0;
            Ludots.Core.Client.ClientLocalSeatRegistry seats = Ludots.Core.Client.ClientLocalSeatAccess.RequireRegistry(_globals);
            if (!seats.TryGetSoleSeat(out Ludots.Core.Client.ClientLocalSeat seat) || !seat.HasPossession)
            {
                return false;
            }

            playerId = seat.PossessedPlayerId;
            return true;
        }

        public Entity GetControlledActor(int playerId)
        {
            if (playerId <= 0)
            {
                return default;
            }

            Entity commandOwner = TryGetCommandSourceOwner(out Entity resolvedOwner)
                ? resolvedOwner
                : Entity.Null;
            if (commandOwner != Entity.Null &&
                TryGetCommandSourcePrimary(commandOwner, out var commandSourcePrimary) &&
                _world.IsAlive(commandSourcePrimary) &&
                _world.TryGet(commandSourcePrimary, out PlayerOwner owner) &&
                owner.PlayerId == playerId)
            {
                return commandSourcePrimary;
            }

            if (commandOwner != Entity.Null &&
                TryGetCollectionPrimary(commandOwner, EntityCollectionKeys.CommandSource, out var collectionPrimary) &&
                _world.IsAlive(collectionPrimary) &&
                _world.TryGet(collectionPrimary, out PlayerOwner collectionOwner) &&
                collectionOwner.PlayerId == playerId)
            {
                return collectionPrimary;
            }

            return default;
        }

        public Entity GetSolePossessedRepOrNull()
        {
            return ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out Entity local) &&
                   _world.IsAlive(local)
                ? local
                : Entity.Null;
        }

        public bool TryGetCollectionPrimary(Entity owner, string collectionKey, out Entity entity)
        {
            entity = default;
            if (!TryResolveCollection(owner, collectionKey, out EntityCollectionHandle handle, out EntityCollectionView view))
            {
                return false;
            }

            Entity primary = view.PrimaryEntity != Entity.Null
                ? view.PrimaryEntity
                : _entityCollections!.TryGetEntityAt(handle, 0, out Entity first)
                    ? first
                    : Entity.Null;
            if (primary == Entity.Null || !_world.IsAlive(primary))
            {
                return false;
            }

            entity = primary;
            return true;
        }

        public bool TryCopyCollectionEntities(
            Entity owner,
            string collectionKey,
            List<Entity> entities,
            int capacity,
            out OrderSubmitResult rejection)
        {
            entities.Clear();
            rejection = OrderSubmitResult.RejectedInvalidActor;
            if (!TryResolveCollection(owner, collectionKey, out _, out EntityCollectionView view) ||
                view.Count <= 0)
            {
                return false;
            }

            if (view.Count > capacity)
            {
                rejection = OrderSubmitResult.RejectedAdmissionCapacity;
                return false;
            }

            EnsureCollectionScratch(view.Count);
            int count = EntityCollectionContextRuntime.Copy(
                _entityCollections!,
                owner,
                collectionKey,
                _collectionScratch.AsSpan(0, view.Count));
            for (int i = 0; i < count; i++)
            {
                Entity entity = _collectionScratch[i];
                if (_world.IsAlive(entity))
                {
                    if (entities.Count >= capacity)
                    {
                        entities.Clear();
                        rejection = OrderSubmitResult.RejectedAdmissionCapacity;
                        return false;
                    }

                    entities.Add(entity);
                }
            }

            if (entities.Count <= 0)
            {
                rejection = OrderSubmitResult.RejectedInvalidActor;
                return false;
            }

            rejection = OrderSubmitResult.Activated;
            return entities.Count > 0;
        }

        private bool TryResolveCollection(
            Entity owner,
            string collectionKey,
            out EntityCollectionHandle handle,
            out EntityCollectionView view)
        {
            handle = EntityCollectionHandle.Invalid;
            view = default;
            return _entityCollections != null &&
                   owner != Entity.Null &&
                   _world.IsAlive(owner) &&
                   !string.IsNullOrWhiteSpace(collectionKey) &&
                   _entityCollections.TryGet(owner, collectionKey, out handle) &&
                   _entityCollections.TryGetView(handle, out view);
        }

        private void EnsureCollectionScratch(int required)
        {
            if (_collectionScratch.Length >= required)
            {
                return;
            }

            throw new InvalidOperationException(
                $"INPUT.INTERACTION_CONTEXT.ERR.CollectionScratchCapacityExceeded: required={required}, capacity={_collectionScratch.Length}.");
        }

        public bool TryGetCommandSourceOwner(out Entity owner)
        {
            owner = default;
            if (!TryResolveLocalCommandSourceOwner(out Entity subject) ||
                !_world.IsAlive(subject))
            {
                return false;
            }

            if (_world.TryGet<InteractionContextInstance>(subject, out InteractionContextInstance context))
            {
                if (!HasEntityValue(context.ContextEntity) || !_world.IsAlive(context.ContextEntity))
                {
                    return false;
                }

                owner = context.ContextEntity;
                return true;
            }

            owner = subject;
            return true;
        }

        private static bool HasEntityValue(Entity entity)
        {
            return entity.Id != 0 || entity.WorldId != 0 || entity.Version != 0;
        }

        public bool TryGetCommandSourcePrimary(Entity owner, out Entity entity)
        {
            entity = default;
            if (!TryResolveCollection(owner, EntityCollectionKeys.CommandSource, out EntityCollectionHandle handle, out _) ||
                _entityCollections == null ||
                !_entityCollections.TryGetEntityAt(handle, 0, out Entity candidate) ||
                !_world.IsAlive(candidate))
            {
                return false;
            }

            entity = candidate;
            return true;
        }

        public bool TryCopyCommandSourceEntities(Entity owner, List<Entity> entities)
        {
            entities.Clear();
            if (!TryResolveCollection(owner, EntityCollectionKeys.CommandSource, out EntityCollectionHandle handle, out EntityCollectionView view) ||
                _entityCollections == null ||
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
        public bool TryGetHoveredEntity(Entity owner, out Entity entity)
        {
            entity = default;
            return _entityCollections != null &&
                   EntityCollectionContextRuntime.TryGetHovered(_world, _entityCollections, owner, out entity);
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
            GasGraphOpHandlerTable? graphHandlers = _globals.TryGetValue(CoreServiceKeys.GasGraphOpHandlerTable.Name, out var graphHandlersObj) &&
                                                     graphHandlersObj is GasGraphOpHandlerTable resolvedGraphHandlers
                ? resolvedGraphHandlers
                : null;
            if (graphPrograms != null && graphHandlers == null)
            {
                throw new InvalidOperationException(
                    "Ability aim presentation graph support requires CoreServiceKeys.GasGraphOpHandlerTable.");
            }

            GasGraphRuntimeApi? graphApi = null;
            if (graphPrograms != null)
            {
                if (!_globals.TryGetValue(CoreServiceKeys.GasGraphRuntimeApi.Name, out var graphApiObj) ||
                    graphApiObj is not GasGraphRuntimeApi productionGraphApi)
                {
                    throw new InvalidOperationException(
                        "Ability aim presentation requires the engine-owned production GasGraphRuntimeApi when GraphProgramRegistry is present.");
                }

                graphApi = productionGraphApi;
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
                graphApi,
                graphHandlers);
            return true;
        }

    }
}
