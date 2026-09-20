using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Modding;
using Ludots.Core.Client;
using Ludots.Core.Scripting;

namespace VirtualCameraShotsMod.Triggers
{
    public sealed class ActivateTaggedVirtualCameraOnMapLoadedTrigger : Trigger
    {
        private const string ShotTagPrefix = "camera.shot:";
        private readonly IModContext _context;

        public ActivateTaggedVirtualCameraOnMapLoadedTrigger(IModContext context)
        {
            _context = context;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var mapTags = context.Get(CoreServiceKeys.MapTags) ?? new List<string>();
            string shotId = ResolveShotId(mapTags);
            if (string.IsNullOrWhiteSpace(shotId))
            {
                return Task.CompletedTask;
            }

            var registry = context.Get(CoreServiceKeys.VirtualCameraRegistry)
                ?? throw new InvalidOperationException("VirtualCameraRegistry is required for VirtualCameraShotsMod.");
            if (!registry.TryGet(shotId, out VirtualCameraDefinition? definition) || definition == null)
            {
                throw new InvalidOperationException($"Virtual camera shot '{shotId}' was requested by tag but is not registered.");
            }

            var engine = context.GetEngine();
            if (engine == null)
            {
                throw new InvalidOperationException(
                    $"Virtual camera shot '{shotId}' was requested by tag but GameEngine is not available.");
            }

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = shotId,
                FollowCollectionOwnerOverride = ResolveFollowCollectionOwner(engine, definition.FollowTargetKind)
            });

            _context.Log($"[VirtualCameraShotsMod] Activated shot '{shotId}' from map tag.");
            return Task.CompletedTask;
        }

        private static Entity ResolveFollowCollectionOwner(GameEngine engine, CameraFollowTargetKind followTargetKind)
        {
            if (!CameraFollowTargetFactory.RequiresEntityCollection(followTargetKind))
            {
                return Entity.Null;
            }

            if (TryResolveLocalCommandSourceOwner(engine, out Entity owner))
            {
                return owner;
            }

            throw new InvalidOperationException(
                "VirtualCameraShotsMod collection follow shot requires an explicit local collection owner.");
        }

        private static bool TryResolveLocalCommandSourceOwner(GameEngine engine, out Entity owner)
        {
            owner = Entity.Null;
            Entity local = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            if (local == Entity.Null || !engine.World.IsAlive(local))
            {
                return false;
            }

            owner = local;
            return true;
        }

        private static string ResolveShotId(List<string> mapTags)
        {
            for (int i = 0; i < mapTags.Count; i++)
            {
                var tag = mapTags[i];
                if (tag != null && tag.StartsWith(ShotTagPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return tag.Substring(ShotTagPrefix.Length).Trim();
                }
            }

            return string.Empty;
        }
    }
}
