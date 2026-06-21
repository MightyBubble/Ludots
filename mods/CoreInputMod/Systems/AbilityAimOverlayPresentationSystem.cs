using System.Numerics;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Input.Orders;
using Ludots.Core.Scripting;

namespace CoreInputMod.Systems
{
    /// <summary>
    /// Emits generic ability aiming previews during the presentation pass so overlays
    /// are rendered from the latest aiming state instead of simulation-time callbacks.
    /// </summary>
    public sealed class AbilityAimOverlayPresentationSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly InputInteractionContextAccessor _context;
        private AbilityIndicatorOverlayBridge? _bridge;

        public AbilityAimOverlayPresentationSystem(World world, Dictionary<string, object> globals)
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
            if (_bridge == null && !_context.TryCreateAbilityIndicatorBridge(out _bridge))
            {
                return;
            }

            if (!TryGetActiveAiming(out var mappingSystem, out var aimingMapping))
            {
                _bridge.ClearPreview();
                return;
            }

            if (!_context.TryGetLocalPlayerId(out int playerId))
            {
                _bridge.ClearPreview();
                return;
            }

            Entity actor = _context.GetControlledActor(playerId);
            if (!_world.IsAlive(actor))
            {
                _bridge.ClearPreview();
                return;
            }

            if (mappingSystem.IsVectorAiming)
            {
                EmitVectorPreview(actor, aimingMapping, mappingSystem);
                return;
            }

            bool hasCursor = _context.TryGetGroundWorldCm(out var groundCm);
            _context.TryGetEntity(CoreServiceKeys.HoveredEntity.Name, out var hovered);
            _bridge.UpdateAiming(
                actor,
                aimingMapping,
                hasCursor,
                new Vector3(groundCm.X, 0f, groundCm.Y),
                hovered);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
            _bridge?.ClearPreview();
        }

        private bool TryGetActiveAiming(out InputOrderMappingSystem mappingSystem, out InputOrderMapping aimingMapping)
        {
            mappingSystem = default!;
            aimingMapping = default!;
            if (!_context.TryGetLocalPlayerId(out int playerId))
            {
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.ActiveInputOrderMapping.Name, out var mappingObj) ||
                mappingObj is not InputOrderMappingSystem activeMapping ||
                !_world.IsAlive(_context.GetControlledActor(playerId)) ||
                _bridge == null)
            {
                return false;
            }

            mappingSystem = activeMapping;
            if (!mappingSystem.IsAiming)
            {
                return false;
            }

            if (mappingSystem.CurrentAimingMapping is not InputOrderMapping current)
            {
                return false;
            }

            aimingMapping = current;
            return true;
        }

        private void EmitVectorPreview(Entity actor, InputOrderMapping aimingMapping, InputOrderMappingSystem mappingSystem)
        {
            bool hasCursor = _context.TryGetGroundWorldCm(out var groundCm);
            if (!hasCursor)
            {
                return;
            }

            Vector3 cursor = new Vector3(groundCm.X, 0f, groundCm.Y);
            Vector3 origin = mappingSystem.VectorAimPhase == VectorAimPhase.Origin
                ? cursor
                : mappingSystem.VectorAimOrigin;

            _bridge!.UpdateVectorAiming(actor, aimingMapping, origin, cursor, mappingSystem.VectorAimPhase);
        }
    }
}
