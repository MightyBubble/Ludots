using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Selection
{
    public sealed class SelectionBridgeProjectionSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly SelectionRuntime _selection;
        private readonly Entity[] _projected = new Entity[SelectionBuffer.CAPACITY];
        private int _projectedCount;

        public SelectionBridgeProjectionSystem(World world, Dictionary<string, object> globals, SelectionRuntime selection)
        {
            _world = world;
            _globals = globals;
            _selection = selection;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            ClearProjectedTags();

            Span<Entity> selected = stackalloc Entity[SelectionBuffer.CAPACITY];
            int count = SelectionViewRuntime.CopyViewedSelection(_world, _globals, _selection, selected);
            for (int i = 0; i < count; i++)
            {
                Entity entity = selected[i];
                if (!_world.IsAlive(entity))
                {
                    continue;
                }

                if (!_world.Has<SelectedTag>(entity))
                {
                    _world.Add(entity, default(SelectedTag));
                }

                _projected[_projectedCount++] = entity;
            }

            if (SelectionViewRuntime.TryGetViewedPrimary(_world, _globals, _selection, out var primary))
            {
                _globals[CoreServiceKeys.SelectedEntity.Name] = primary;
            }
            else
            {
                _globals.Remove(CoreServiceKeys.SelectedEntity.Name);
            }
        }

        private void ClearProjectedTags()
        {
            for (int i = 0; i < _projectedCount; i++)
            {
                Entity entity = _projected[i];
                if (_world.IsAlive(entity) && _world.Has<SelectedTag>(entity))
                {
                    _world.Remove<SelectedTag>(entity);
                }

                _projected[i] = default;
            }

            _projectedCount = 0;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
