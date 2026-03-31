using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace RtsDemoMod.Runtime
{
    internal static class RtsPresentationBootstrapper
    {
        public static void EnsureReadableActors(GameEngine engine, World world)
        {
            if (engine == null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            PresentationStableIdAllocator? allocator = engine.GetService(CoreServiceKeys.PresentationStableIdAllocator);
            var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
            world.Query(in query, (Entity entity, ref Name name, ref WorldPositionCm position) =>
            {
                FixMissingPreviousPosition(world, entity, position);
                FixMissingVisualTransform(world, entity, name.Value, position);
                FixMissingStableId(world, allocator, entity);
            });
        }

        private static void FixMissingPreviousPosition(World world, Entity entity, in WorldPositionCm position)
        {
            if (!world.Has<PreviousWorldPositionCm>(entity))
            {
                world.Add(entity, new PreviousWorldPositionCm { Value = position.Value });
            }
        }

        private static void FixMissingVisualTransform(World world, Entity entity, string? displayName, in WorldPositionCm position)
        {
            Vector3 scale = ResolveMarkerScale(displayName);
            Vector3 visualPosition = new(
                position.Value.X.ToFloat() * 0.01f,
                0f,
                position.Value.Y.ToFloat() * 0.01f);

            if (world.Has<VisualTransform>(entity))
            {
                ref var visual = ref world.Get<VisualTransform>(entity);
                visual.Position = visualPosition;
                if (visual.Scale == Vector3.Zero)
                {
                    visual.Scale = scale;
                }

                if (visual.Rotation == Quaternion.Zero)
                {
                    visual.Rotation = Quaternion.Identity;
                }

                return;
            }

            world.Add(entity, new VisualTransform
            {
                Position = visualPosition,
                Rotation = Quaternion.Identity,
                Scale = scale
            });
        }

        private static void FixMissingStableId(World world, PresentationStableIdAllocator? allocator, Entity entity)
        {
            if (allocator == null || world.Has<PresentationStableId>(entity))
            {
                return;
            }

            world.Add(entity, new PresentationStableId { Value = allocator.Allocate() });
        }

        private static Vector3 ResolveMarkerScale(string? displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return new Vector3(1.15f, 1.15f, 1.15f);
            }

            if (displayName.Contains("Barracks", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Tower", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Construction Yard", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Factory", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Bunker", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Gateway", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Refinery", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Power Plant", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Pool", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("Mill", StringComparison.OrdinalIgnoreCase))
            {
                return new Vector3(1.85f, 1.55f, 1.85f);
            }

            return new Vector3(1.15f, 1.15f, 1.15f);
        }
    }
}
