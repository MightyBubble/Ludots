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
                !SelectionContextRuntime.TryGetRuntime(_engine.GlobalContext, out SelectionRuntime selection) ||
                !SelectionContextRuntime.TryGetCurrentContainer(_world, _engine.GlobalContext, out Entity container))
            {
                return;
            }

            Entity[] current = SelectionContextRuntime.SnapshotCurrentSelection(_world, _engine.GlobalContext);
            if (current.Length <= 0)
            {
                return;
            }

            Entity[] kept = new Entity[current.Length];
            int keptCount = 0;
            bool changed = false;

            for (int i = 0; i < current.Length; i++)
            {
                Entity entity = current[i];
                if (IsControllable(entity))
                {
                    kept[keptCount++] = entity;
                    continue;
                }

                changed = true;
            }

            if (changed)
            {
                selection.ReplaceSelection(container, kept.AsSpan(0, keptCount));
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
