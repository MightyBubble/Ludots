using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CoreInputMod.Systems
{
    internal sealed class InputInteractionContextAccessor
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly EntityCollectionStore? _entityCollections;

        public InputInteractionContextAccessor(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
            _entityCollections = globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out var collectionsObj) &&
                                 collectionsObj is EntityCollectionStore collections
                ? collections
                : null;
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
            if (!TryGetSolePossessedPlayerId(out int possessedPlayerId) || possessedPlayerId != playerId)
            {
                return Entity.Null;
            }

            // Selection-routed casts act on the command-source primary (the selected entity);
            // sessions without a selection (massnav-style camera seats) fall back to the
            // possessed rep.
            if (TryGetCommandSourceOwner(out Entity commandOwner) &&
                TryGetCommandSourcePrimary(commandOwner, out Entity selected) &&
                _world.IsAlive(selected) &&
                _world.TryGet(selected, out Ludots.Core.Gameplay.Components.PlayerOwner owner) &&
                owner.PlayerId == playerId)
            {
                return selected;
            }

            return GetSolePossessedRepOrNull();
        }

        public Entity GetSolePossessedRepOrNull()
        {
            return ClientLocalSeatAccess.TryGetSolePossessedRep(_globals, out Entity local) &&
                   _world.IsAlive(local)
                ? local
                : Entity.Null;
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
            if (!TryResolveCollection(owner, CoreInputCollectionKeys.CommandSource, out EntityCollectionHandle handle, out _) ||
                _entityCollections == null ||
                !_entityCollections.TryGetEntityAt(handle, 0, out Entity candidate) ||
                !_world.IsAlive(candidate))
            {
                return false;
            }

            entity = candidate;
            return true;
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
    }
}
