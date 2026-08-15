using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Surfaces;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class SurfaceSourceLifecycleSystem : BaseSystem<World, float>
    {
        private readonly SurfaceSourceRuntimeRegistry _runtime;
        private readonly PresenterCommandBuffer _commands;
        private readonly List<int> _completedRemovals = new();

        public SurfaceSourceLifecycleSystem(
            World world,
            SurfaceSourceRuntimeRegistry runtime,
            PresenterCommandBuffer commands)
            : base(world)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        }

        public override void Update(in float dt)
        {
            _completedRemovals.Clear();
            if (_completedRemovals.Capacity < _runtime.Count)
            {
                _completedRemovals.Capacity = _runtime.Count;
            }

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
                        if (!_commands.TryAdd(new PresenterCommand
                            {
                                CommandKind = PresenterCommandKind.DestroyPresenterScope,
                                ScopeTag = record.RenderScopeId,
                            }))
                        {
                            throw new InvalidOperationException(
                                $"SurfaceSource stableId={record.SourceStableId} failed to queue baked render presenter destruction.");
                        }

                        record.RenderPresenterEntity = Entity.Null;
                    }

                    if (!World.Has<PresentationDestroyPending>(record.Entity))
                    {
                        World.Add(record.Entity, new PresentationDestroyPending());
                    }

                    if (World.Has<PresentationDestroyEventPublished>(record.Entity))
                    {
                        World.Remove<PresentationDestroyEventPublished>(record.Entity);
                    }
                }
                else
                {
                    _completedRemovals.Add(record.SourceStableId);
                }
            }

            for (int i = 0; i < _completedRemovals.Count; i++)
            {
                _runtime.Remove(_completedRemovals[i]);
            }
        }
    }
}
