using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Surfaces;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class SurfaceSourceLifecycleSystem : BaseSystem<World, float>
    {
        private readonly SurfaceSourceRuntimeRegistry _runtime;

        public SurfaceSourceLifecycleSystem(World world, SurfaceSourceRuntimeRegistry runtime)
            : base(world)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public override void Update(in float dt)
        {
            _runtime.MarkStaleAsPendingRemoval();

            foreach (SurfaceSourceRecord record in _runtime.Records)
            {
                if (!record.PendingRemoval)
                {
                    continue;
                }

                if (record.Entity != Entity.Null && World.IsAlive(record.Entity))
                {
                    PresentationLifecycleState lifecycle = World.Has<PresentationLifecycleState>(record.Entity)
                        ? World.Get<PresentationLifecycleState>(record.Entity)
                        : default;
                    lifecycle.PendingDestroy = true;
                    lifecycle.DestroyEventPublished = false;
                    if (World.Has<PresentationLifecycleState>(record.Entity))
                    {
                        World.Set(record.Entity, lifecycle);
                    }
                    else
                    {
                        World.Add(record.Entity, lifecycle);
                    }
                }
                else
                {
                    _runtime.Remove(record.SourceStableId);
                }
            }
        }
    }
}
