using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Input.Orders;
using Ludots.Core.Scripting;

namespace CoreInputMod.Systems
{
    /// <summary>
    /// Publishes ability aim preview collections and performer state from the current input aim session.
    /// </summary>
    public sealed class AbilityAimPresentationProjectionSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly InputInteractionContextAccessor _context;
        private AbilityAimPresentationRuntime? _runtime;
        private Entity _lastActor;

        public AbilityAimPresentationProjectionSystem(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
            _context = new InputInteractionContextAccessor(world, globals);
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (_runtime == null && !_context.TryCreateAbilityAimPresentationRuntime(out _runtime))
            {
                return;
            }

            if (!TryGetActiveAiming(out InputOrderMappingSystem mappingSystem, out InputOrderMapping aimingMapping) ||
                !_context.TryGetLocalPlayerId(out int playerId))
            {
                ClearLastActor();
                return;
            }

            Entity actor = _context.GetControlledActor(playerId);
            if (!_world.IsAlive(actor))
            {
                ClearLastActor();
                return;
            }

            if (_lastActor != Entity.Null && _lastActor != actor)
            {
                _runtime!.Clear(_lastActor);
            }

            _lastActor = actor;
            if (mappingSystem.IsVectorAiming)
            {
                EmitVectorPreview(actor, aimingMapping, mappingSystem);
                return;
            }

            bool hasCursor = _context.TryGetGroundWorldCm(out var groundCm);
            Entity viewer = _context.TryGetCommandSourceOwner(out Entity owner)
                ? owner
                : _context.GetLocalPlayerEntityOrNull();
            Entity hovered = Entity.Null;
            if (viewer != Entity.Null)
            {
                _context.TryGetHoveredEntity(viewer, out hovered);
            }

            var input = new AbilityAimInputState(
                AbilityAimInputSlot.Target,
                hasCursor,
                new Vector3(groundCm.X, 0f, groundCm.Y),
                hasOriginWorldCm: false,
                originWorldCm: default,
                hovered,
                viewer);
            _runtime!.UpdateAiming(actor, aimingMapping, in input);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
            ClearLastActor();
        }

        private bool TryGetActiveAiming(out InputOrderMappingSystem mappingSystem, out InputOrderMapping aimingMapping)
        {
            mappingSystem = default!;
            aimingMapping = default!;
            if (_runtime == null ||
                !_context.TryGetLocalPlayerId(out int playerId) ||
                !_globals.TryGetValue(CoreServiceKeys.ActiveInputOrderMapping.Name, out object? mappingObj) ||
                mappingObj is not InputOrderMappingSystem activeMapping ||
                !_world.IsAlive(_context.GetControlledActor(playerId)) ||
                !activeMapping.IsAiming ||
                activeMapping.CurrentAimingMapping is not InputOrderMapping current)
            {
                return false;
            }

            mappingSystem = activeMapping;
            aimingMapping = current;
            return true;
        }

        private void EmitVectorPreview(Entity actor, InputOrderMapping aimingMapping, InputOrderMappingSystem mappingSystem)
        {
            if (!_context.TryGetGroundWorldCm(out var groundCm))
            {
                return;
            }

            Vector3 cursor = new(groundCm.X, 0f, groundCm.Y);
            Vector3 origin = mappingSystem.VectorAimSlot == VectorAimInputSlot.Origin
                ? cursor
                : mappingSystem.VectorAimOrigin;

            var input = new AbilityAimInputState(
                mappingSystem.VectorAimSlot == VectorAimInputSlot.Origin
                    ? AbilityAimInputSlot.VectorOrigin
                    : AbilityAimInputSlot.VectorDirection,
                hasCursorWorldCm: true,
                cursor,
                hasOriginWorldCm: mappingSystem.VectorAimSlot != VectorAimInputSlot.Origin,
                origin,
                Entity.Null,
                _context.TryGetCommandSourceOwner(out Entity viewer) ? viewer : _context.GetLocalPlayerEntityOrNull());
            _runtime!.UpdateAiming(actor, aimingMapping, in input);
        }

        private void ClearLastActor()
        {
            if (_runtime == null || _lastActor == Entity.Null)
            {
                return;
            }

            _runtime.Clear(_lastActor);
            _lastActor = Entity.Null;
        }
    }
}
