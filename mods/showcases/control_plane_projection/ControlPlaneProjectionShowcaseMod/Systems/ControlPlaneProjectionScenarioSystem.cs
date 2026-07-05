using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using ControlPlaneProjectionShowcaseMod.Runtime;

namespace ControlPlaneProjectionShowcaseMod.Systems
{
    /// <summary>
    /// Bootstraps the showcase world once the map session is live (CTRL-2 slice: Owns/MemberOf/Ally
    /// edges via RelationshipRuntime.EnsureLink), binds P1Rep as the local player selection owner,
    /// and services the ToggleProxy input action.
    /// </summary>
    internal sealed class ControlPlaneProjectionScenarioSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly ControlPlaneProjectionScenarioState _state;

        public ControlPlaneProjectionScenarioSystem(GameEngine engine, ControlPlaneProjectionScenarioState state)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _world = engine.World;
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            MapSession? session = _engine.CurrentMapSession;
            if (!string.Equals(session?.MapId.Value, ControlPlaneProjectionShowcaseIds.MapId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!_state.Ready)
            {
                Bootstrap(session!);
            }

            if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input &&
                input.PressedThisFrame(ControlPlaneProjectionShowcaseIds.ToggleProxyAction))
            {
                _state.ToggleProxy();
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void Bootstrap(MapSession session)
        {
            RelationshipRuntime relationships = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime is missing.");
            RelationshipTypeRegistry relationshipTypes = _engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
                ?? throw new InvalidOperationException("RelationshipTypeRegistry is missing.");
            TagOps tagOps = _engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("TagOps is missing.");
            StringIntRegistry collectionKeys = _engine.GetService(CoreServiceKeys.EntityCollectionKeyRegistry)
                ?? throw new InvalidOperationException("EntityCollectionKeyRegistry is missing.");
            SelectionRuntime selection = _engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime is missing.");

            _state.P1Rep = session.PlayerEntityLookup.Get(1);
            _state.P2Rep = session.PlayerEntityLookup.Get(2);
            _state.TeamRep = session.TeamEntityLookup.Get(1);
            ResolveUnits(session, ControlPlaneProjectionShowcaseIds.P1UnitInstanceIds, _state.P1Units);
            ResolveUnits(session, ControlPlaneProjectionShowcaseIds.P2UnitInstanceIds, _state.P2Units);

            _state.OwnsTypeId = RequireTypeId(relationshipTypes, ControlPlaneProjectionShowcaseIds.OwnsRelationshipType);
            _state.ControlsTypeId = RequireTypeId(relationshipTypes, ControlPlaneProjectionShowcaseIds.ControlsRelationshipType);
            _state.MemberOfTypeId = RequireTypeId(relationshipTypes, ControlPlaneProjectionShowcaseIds.MemberOfRelationshipType);
            _state.AllyTypeId = RequireTypeId(relationshipTypes, ControlPlaneProjectionShowcaseIds.AllyRelationshipType);
            _state.OfflineTagId = TagRegistry.Register(ControlPlaneProjectionShowcaseIds.OfflineTag);

            _state.CommandSourceKeyId = collectionKeys.Register(EntityCollectionKeys.CommandSource);
            _state.OwnedProjectionKeyId = collectionKeys.Register(ControlPlaneProjectionShowcaseIds.OwnedProjectionCollectionKey);
            _state.ProxiedProjectionKeyId = collectionKeys.Register(ControlPlaneProjectionShowcaseIds.ProxiedProjectionCollectionKey);

            BuildRelationshipEdges(relationships);
            BindLocalPlayer(selection);

            _state.BindRuntime(_world, tagOps);
        }

        private void BuildRelationshipEdges(RelationshipRuntime relationships)
        {
            for (int i = 0; i < _state.P1Units.Length; i++)
            {
                relationships.EnsureLink(_state.P1Rep, _state.P1Units[i], _state.OwnsTypeId);
            }

            for (int i = 0; i < _state.P2Units.Length; i++)
            {
                relationships.EnsureLink(_state.P2Rep, _state.P2Units[i], _state.OwnsTypeId);
            }

            relationships.EnsureLink(_state.P1Rep, _state.TeamRep, _state.MemberOfTypeId);
            relationships.EnsureLink(_state.P2Rep, _state.TeamRep, _state.MemberOfTypeId);
            relationships.EnsureLink(_state.P1Rep, _state.P2Rep, _state.AllyTypeId);
        }

        private void BindLocalPlayer(SelectionRuntime selection)
        {
            _engine.SetService(CoreServiceKeys.LocalPlayerEntity, _state.P1Rep);
            _engine.SetService(CoreServiceKeys.LocalPlayerId, 1);

            selection.TryGetOrCreateSelectionEntity(_state.P1Rep, SelectionSetKeys.LivePrimary, out _);
            if (!SelectionContextRuntime.TrySetCurrentView(
                    _world,
                    _engine.GlobalContext,
                    selection,
                    _state.P1Rep,
                    SelectionViewKeys.Primary,
                    _state.P1Rep,
                    SelectionSetKeys.LivePrimary,
                    out _))
            {
                throw new InvalidOperationException("Control plane projection showcase failed to bind the primary selection view.");
            }
        }

        private static void ResolveUnits(MapSession session, string[] instanceIds, Entity[] destination)
        {
            for (int i = 0; i < instanceIds.Length; i++)
            {
                if (!session.EntityIndex.TryGet(instanceIds[i], out Entity entity))
                {
                    throw new InvalidOperationException($"Showcase map does not contain instance '{instanceIds[i]}'.");
                }

                destination[i] = entity;
            }
        }

        private static int RequireTypeId(RelationshipTypeRegistry registry, string typeName)
        {
            int typeId = registry.GetId(typeName);
            if (typeId < 0)
            {
                throw new InvalidOperationException($"Relationship type '{typeName}' is not registered in the core catalog.");
            }

            return typeId;
        }
    }
}
