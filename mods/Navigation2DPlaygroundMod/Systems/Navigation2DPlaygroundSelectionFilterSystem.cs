using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Scripting;

namespace Navigation2DPlaygroundMod.Systems
{
    internal sealed class Navigation2DPlaygroundSelectionFilterSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly World _world;

        public Navigation2DPlaygroundSelectionFilterSystem(GameEngine engine)
        {
            _engine = engine;
            _world = engine.World;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!Navigation2DPlaygroundState.Enabled ||
                !_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) ||
                localObj is not Entity local ||
                !_world.IsAlive(local) ||
                !_world.Has<SelectionBuffer>(local))
            {
                return;
            }

            ref var selection = ref _world.Get<SelectionBuffer>(local);
            if (selection.Count <= 0)
            {
                return;
            }

            Span<Entity> kept = stackalloc Entity[SelectionBuffer.CAPACITY];
            int keptCount = 0;
            bool changed = false;

            for (int i = 0; i < selection.Count; i++)
            {
                Entity entity = selection.Get(i);
                if (IsControllable(entity))
                {
                    kept[keptCount++] = entity;
                    continue;
                }

                changed = true;
            }

            if (changed)
            {
                selection.Clear();
                for (int i = 0; i < keptCount; i++)
                {
                    Entity entity = kept[i];
                    if (!selection.Add(entity))
                    {
                        break;
                    }
                }

                _world.Set(local, selection);
            }
        }

        private bool IsControllable(Entity entity)
        {
            return _world.IsAlive(entity) &&
                   _world.Has<NavPlaygroundControllable>(entity) &&
                   _world.Has<NavPlaygroundTeam>(entity) &&
                   _world.Has<NavGoal2D>(entity) &&
                   _world.Get<NavPlaygroundTeam>(entity).Id == 0 &&
                   !_world.Has<NavPlaygroundBlocker>(entity);
        }
    }
}
