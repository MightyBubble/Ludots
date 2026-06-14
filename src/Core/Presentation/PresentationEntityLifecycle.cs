using System;
using Arch.Core;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation
{
    public static class PresentationEntityLifecycle
    {
        public static void RequestDestroy(World world, Entity entity, string diagnosticLabel)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!world.IsAlive(entity))
            {
                return;
            }

            if (!world.Has<PresentationStableId>(entity))
            {
                throw new InvalidOperationException($"{diagnosticLabel} cannot be destroyed through presentation lifecycle without PresentationStableId.");
            }

            if (!world.Has<PresentationDestroyPending>(entity))
            {
                world.Add(entity, new PresentationDestroyPending());
            }

            if (world.Has<PresentationDestroyEventPublished>(entity))
            {
                world.Remove<PresentationDestroyEventPublished>(entity);
            }
        }
    }
}
