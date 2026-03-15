using System.Collections.Generic;
using Arch.Core;
using Arch.System;

namespace Ludots.Core.Input.Selection
{
    public sealed class SelectionMaintenanceSystem : ISystem<float>
    {
        private static readonly QueryDescription SelectionQuery = new QueryDescription().WithAll<SelectionBuffer>();

        private readonly World _world;
        private readonly SelectionRuntime _selection;
        private readonly List<Entity> _toDestroy = new();

        public SelectionMaintenanceSystem(World world, SelectionRuntime selection)
        {
            _world = world;
            _selection = selection;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            _toDestroy.Clear();

            _world.Query(in SelectionQuery, (Entity entity, ref SelectionBuffer selectionBuffer) =>
            {
                if (_world.Has<SelectionSetOwner>(entity))
                {
                    ref var owner = ref _world.Get<SelectionSetOwner>(entity);
                    if (!_world.IsAlive(owner.Value))
                    {
                        _toDestroy.Add(entity);
                        return;
                    }
                }

                _selection.PruneDeadTargets(entity);
            });

            for (int i = 0; i < _toDestroy.Count; i++)
            {
                Entity entity = _toDestroy[i];
                if (_world.IsAlive(entity))
                {
                    _world.Destroy(entity);
                }
            }
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
