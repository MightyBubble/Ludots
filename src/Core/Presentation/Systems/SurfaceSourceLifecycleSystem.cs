using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Surfaces;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class SurfaceSourceLifecycleSystem : BaseSystem<World, float>
    {
        private readonly SurfaceSourceRuntimeRegistry _runtime;
        private readonly PerformerCommandBuffer _commands;

        public SurfaceSourceLifecycleSystem(
            World world,
            SurfaceSourceRuntimeRegistry runtime,
            PerformerCommandBuffer commands)
            : base(world)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
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
                    if (record.RenderScopeId > 0)
                    {
                        if (!_commands.TryAdd(new PerformerCommand
                            {
                                CommandKind = PerformerCommandKind.DestroyPerformerScope,
                                ScopeTag = record.RenderScopeId,
                            }))
                        {
                            throw new InvalidOperationException(
                                $"SurfaceSource stableId={record.SourceStableId} failed to queue baked render performer destruction.");
                        }

                        record.RenderPerformerEntity = Entity.Null;
                    }

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
