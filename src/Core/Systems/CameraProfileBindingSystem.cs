using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Camera.FollowTargets;
using Ludots.Core.Scripting;

namespace Ludots.Core.Systems
{
    /// <summary>
    /// Sole-seat perspective adoption: the possessed rep's <see cref="CameraProfileBinding"/> is the
    /// authority for which virtual camera profile is active. Switching possession to a rep that
    /// declares another profile switches the camera with the profile's blend; a rep without a
    /// binding declares no perspective preference and keeps the current one. Multi-seat tables own
    /// per-view cameras and are not adopted here. Empty or unknown profile ids fail fast.
    /// </summary>
    public sealed class CameraProfileBindingSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly VirtualCameraRegistry _registry;

        public CameraProfileBindingSystem(
            World world,
            Dictionary<string, object> globals,
            VirtualCameraRegistry registry)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? seatsObj) ||
                seatsObj is not ClientLocalSeatRegistry seats ||
                seats.Count != 1 ||
                !seats.TryGetSoleSeat(out ClientLocalSeat seat) ||
                !seat.HasPossession ||
                !_world.IsAlive(seat.PossessedRep) ||
                !_world.Has<CameraProfileBinding>(seat.PossessedRep))
            {
                return;
            }

            string profileId = _world.Get<CameraProfileBinding>(seat.PossessedRep).ProfileId;
            if (string.IsNullOrWhiteSpace(profileId))
            {
                throw new InvalidOperationException(
                    $"Entity {seat.PossessedRep.Id} declares CameraProfileBinding with an empty profile id.");
            }

            profileId = profileId.Trim();
            if (!_registry.TryGet(profileId, out VirtualCameraDefinition definition))
            {
                throw new InvalidOperationException(
                    $"CameraProfileBinding references unknown virtual camera profile '{profileId}'.");
            }

            CameraManager camera = ResolveSeatCamera(seat);
            if (camera.VirtualCameraBrain?.ActiveCameraId == profileId)
            {
                return;
            }

            var followTarget = CameraFollowTargetFactory.Build(
                _world,
                _globals,
                definition.FollowTargetKind,
                Entity.Null,
                definition.FollowCollectionKey);
            camera.ActivateVirtualCamera(profileId, followTarget: followTarget);
        }

        private CameraManager ResolveSeatCamera(ClientLocalSeat seat)
        {
            PresentBinding binding = seat.PresentBinding
                ?? throw new InvalidOperationException(
                    "Sole-seat perspective adoption requires the seat's PresentBinding (logic view id).");

            LogicViewRegistry views = ClientLocalSeatAccess.RequireLogicViews(_globals);
            return views.Require(binding.LogicViewId).Camera;
        }
    }
}
