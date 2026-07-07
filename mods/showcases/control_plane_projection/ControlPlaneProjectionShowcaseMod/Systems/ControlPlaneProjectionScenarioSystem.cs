using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.Components;
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
        private const int RefereeRevokeVisibleFrame = 120;

        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly ControlPlaneProjectionScenarioState _state;
        private readonly bool _autoDemoEnabled;
        private readonly bool _cefAutoTimelineEnabled;
        private readonly bool _refereeUatEnabled;
        private int _autoDemoReadyTicks;
        private bool _autoDemoApplied;
        private int _refereeUatFrame;
        private bool _refereeFixtureSeeded;
        private bool _refereeP2Revoked;

        public ControlPlaneProjectionScenarioSystem(GameEngine engine, ControlPlaneProjectionScenarioState state)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _world = engine.World;
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _autoDemoEnabled = IsEnabled("LUDOTS_CONTROL_PLANE_PROJECTION_AUTO_DEMO");
            _cefAutoTimelineEnabled = IsEnabled("LUDOTS_CONTROL_PLANE_PROJECTION_CEF_AUTO_TIMELINE");
            _refereeUatEnabled = IsEnabled(ControlPlaneProjectionShowcaseIds.RefereeUatEnvKey);
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

            if (_autoDemoEnabled)
            {
                ApplyAutoDemo();
            }

            if (_refereeUatEnabled)
            {
                ApplyRefereeVisibleUat(session!);
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
            _state.RefereePhase0ProjectionKeyId = collectionKeys.Register(ControlPlaneProjectionShowcaseIds.RefereePhase0ProjectionCollectionKey);
            _state.RefereePhase1ProjectionKeyId = collectionKeys.Register(ControlPlaneProjectionShowcaseIds.RefereePhase1ProjectionCollectionKey);

            BuildRelationshipEdges(relationships);
            BindLocalPlayer(selection);

            _state.BindRuntime(_world, tagOps);
        }

        private void ApplyRefereeVisibleUat(MapSession session)
        {
            _refereeUatFrame++;
            int visibleFrame = _engine.TryGetService(CoreServiceKeys.HostFrameIndex, out int hostFrameIndex) && hostFrameIndex > 0
                ? hostFrameIndex
                : _refereeUatFrame;
            _engine.GlobalContext[ControlPlaneProjectionShowcaseIds.RefereeUatFrameKey] = visibleFrame;

            if (!_refereeFixtureSeeded)
            {
                SeedRefereeFixture(session);
            }

            if (visibleFrame < RefereeRevokeVisibleFrame || _refereeP2Revoked)
            {
                return;
            }

            RelationshipRuntime relationships = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime is missing.");
            relationships.RemoveLink(_state.RefereeRep, _state.P2Rep, _state.ControlsTypeId);
            _state.RefereeP2GrantActive = false;
            _refereeP2Revoked = true;
        }

        private void SeedRefereeFixture(MapSession session)
        {
            RelationshipRuntime relationships = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime is missing.");
            EntityCollectionStore store = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");

            _state.RefereeRep = _world.Create(
                new PlayerIdentity { PlayerId = 99 },
                new Name { Value = "RefereeRep" },
                new MapEntity { MapId = session.MapId });
            _state.ForeignRep = _world.Create(
                new PlayerIdentity { PlayerId = 77 },
                new Name { Value = "ForeignRep" },
                new MapEntity { MapId = session.MapId });
            _state.RefereeOwnedMarker = _state.P1Units[1];
            _state.ForeignMarker = _state.P2Units[1];

            relationships.EnsureLink(_state.RefereeRep, _state.RefereeOwnedMarker, _state.OwnsTypeId);
            relationships.EnsureLink(_state.ForeignRep, _state.ForeignMarker, _state.OwnsTypeId);
            relationships.EnsureLink(_state.RefereeRep, _state.P1Rep, _state.ControlsTypeId);
            relationships.EnsureLink(_state.RefereeRep, _state.P2Rep, _state.ControlsTypeId);

            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.SelectionView,
                EntityCollectionRoleKind.CommandSource,
                title: "SHOW-3 referee command-source fixture",
                summary: "Visible UAT rows for referee projection evidence; projection still reads through ControlPlaneView.");

            Entity[] refereeRows = { _state.RefereeOwnedMarker };
            Entity[] p1Rows = { _state.P1Units[0] };
            Entity[] p2Rows = { _state.P2Units[0] };
            Entity[] foreignRows = { _state.ForeignMarker };
            store.Replace(_state.RefereeRep, in descriptor, refereeRows, _state.RefereeRep);
            store.Replace(_state.P1Rep, in descriptor, p1Rows, _state.RefereeRep);
            store.Replace(_state.P2Rep, in descriptor, p2Rows, _state.RefereeRep);
            store.Replace(_state.ForeignRep, in descriptor, foreignRows, _state.ForeignRep);

            _state.RefereeReady = true;
            _state.RefereeP2GrantActive = true;
            _refereeFixtureSeeded = true;
        }

        private void ApplyAutoDemo()
        {
            if (_autoDemoApplied)
            {
                return;
            }

            _autoDemoReadyTicks++;
            if (_autoDemoReadyTicks < 2)
            {
                return;
            }

            SelectionRuntime selection = _engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime is missing.");

            if (!_cefAutoTimelineEnabled && !_state.ProxyActive)
            {
                _state.ToggleProxy();
            }

            Span<Entity> selected = stackalloc Entity[2];
            selected[0] = _state.P1Units[0];
            selected[1] = _state.P2Units[0];
            if (!selection.ReplaceSelection(_state.P1Rep, SelectionSetKeys.LivePrimary, selected))
            {
                throw new InvalidOperationException("Control plane projection auto demo failed to publish mixed selection.");
            }

            _engine.GlobalContext[ControlPlaneProjectionShowcaseIds.AutoDemoAppliedKey] = true;
            _autoDemoApplied = true;
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
                throw new InvalidOperationException($"Relationship type '{typeName}' is not registered in the merged relationship catalog.");
            }

            return typeId;
        }

        private static bool IsEnabled(string key)
        {
            string? value = Environment.GetEnvironmentVariable(key);
            return string.Equals(value, "1", StringComparison.Ordinal) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
