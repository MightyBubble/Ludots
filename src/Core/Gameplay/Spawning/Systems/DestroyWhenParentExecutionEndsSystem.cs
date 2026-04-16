using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Gameplay.Spawning.Systems
{
    /// <summary>
    /// Removes child manifestations that are explicitly tied to a parent's
    /// active ability execution.
    /// </summary>
    public sealed class DestroyWhenParentExecutionEndsSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<DestroyWhenParentExecutionEnds, ChildOf>();

        private readonly List<Entity> _pendingDestroy = new(32);

        public DestroyWhenParentExecutionEndsSystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            _pendingDestroy.Clear();
            World.Query(in Query, (Entity entity, ref DestroyWhenParentExecutionEnds _, ref ChildOf child) =>
            {
                Entity parent = child.Parent;
                if (!World.IsAlive(parent) || !World.Has<AbilityExecInstance>(parent))
                {
                    _pendingDestroy.Add(entity);
                }
            });

            for (int i = 0; i < _pendingDestroy.Count; i++)
            {
                if (World.IsAlive(_pendingDestroy[i]))
                {
                    if (World.Has<PresentationStableId>(_pendingDestroy[i]))
                    {
                        var lifecycle = World.Has<PresentationLifecycleState>(_pendingDestroy[i])
                            ? World.Get<PresentationLifecycleState>(_pendingDestroy[i])
                            : default;
                        lifecycle.PendingDestroy = true;
                        if (World.Has<PresentationLifecycleState>(_pendingDestroy[i]))
                        {
                            World.Set(_pendingDestroy[i], lifecycle);
                        }
                        else
                        {
                            World.Add(_pendingDestroy[i], lifecycle);
                        }
                        continue;
                    }

                    World.Destroy(_pendingDestroy[i]);
                }
            }
        }
    }
}
