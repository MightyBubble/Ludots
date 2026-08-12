using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera.FollowTargets;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Camera
{
    public static class CameraFollowTargetFactory
    {
        public static ICameraFollowTarget? Build(
            World world,
            Dictionary<string, object> globals,
            CameraFollowTargetKind kind,
            Entity followCollectionOwner,
            string followCollectionKey)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(globals);

            return kind switch
            {
                CameraFollowTargetKind.None => null,
                CameraFollowTargetKind.LocalPlayer => new FollowTargets.SolePossessedRepFollowTarget(world, globals),
                CameraFollowTargetKind.EntityCollectionPrimary => BuildEntityCollectionTarget(world, globals, followCollectionOwner, followCollectionKey, group: false),
                CameraFollowTargetKind.EntityCollectionGroup => BuildEntityCollectionTarget(world, globals, followCollectionOwner, followCollectionKey, group: true),
                _ => throw new InvalidOperationException($"Unsupported camera follow target kind: {kind}")
            };
        }

        public static bool RequiresEntityCollection(CameraFollowTargetKind kind)
        {
            return kind == CameraFollowTargetKind.EntityCollectionPrimary ||
                   kind == CameraFollowTargetKind.EntityCollectionGroup;
        }

        private static ICameraFollowTarget BuildEntityCollectionTarget(
            World world,
            Dictionary<string, object> globals,
            Entity followCollectionOwner,
            string followCollectionKey,
            bool group)
        {
            if (!globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out object? collectionsObj) ||
                collectionsObj is not EntityCollectionStore collections)
            {
                throw new InvalidOperationException(
                    "EntityCollection camera follow target requires EntityCollectionStore.");
            }

            if (followCollectionOwner == Entity.Null ||
                !world.IsAlive(followCollectionOwner))
            {
                throw new InvalidOperationException(
                    "EntityCollection camera follow target requires an explicit alive collection owner.");
            }

            if (string.IsNullOrWhiteSpace(followCollectionKey))
            {
                throw new InvalidOperationException(
                    "EntityCollection camera follow target requires an explicit collection key.");
            }

            return group
                ? new EntityCollectionGroupFollowTarget(world, collections, followCollectionOwner, followCollectionKey.Trim())
                : new EntityCollectionPrimaryFollowTarget(world, collections, followCollectionOwner, followCollectionKey.Trim());
        }
    }
}
