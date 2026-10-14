using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class BlackboardEntityRefLifetimeSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly QueryDescription _owners = new QueryDescription().WithAll<BlackboardEntityBuffer>();

        public BlackboardEntityRefLifetimeSystem(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _world.SubscribeEntityDestroyed(OnEntityDestroyed);
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void Update(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        private void OnEntityDestroyed(in Entity entity)
        {
            // The callback runs before the entity leaves its archetype, so IsAlive is still true.
            // Match the handle. Keep the key and store Entity.Null: a missing key fails the next read.
            var scrub = new Scrub(entity);
            _world.InlineEntityQuery<Scrub, BlackboardEntityBuffer>(in _owners, ref scrub);
        }

        private readonly struct Scrub : IForEachWithEntity<BlackboardEntityBuffer>
        {
            private readonly Entity _dead;

            public Scrub(Entity dead) => _dead = dead;

            public void Update(Entity entity, ref BlackboardEntityBuffer buffer) => Clear(_dead, ref buffer);

            private static unsafe void Clear(Entity dead, ref BlackboardEntityBuffer buffer)
            {
                int count = buffer.Count;
                for (int i = 0; i < count; i++)
                {
                    if (EntityUtil.Reconstruct(buffer.EntityIds[i], buffer.WorldIds[i], buffer.Versions[i]) != dead)
                    {
                        continue;
                    }

                    buffer.EntityIds[i] = Entity.Null.Id;
                    buffer.WorldIds[i] = Entity.Null.WorldId;
                    buffer.Versions[i] = Entity.Null.Version;
                }
            }
        }
    }
}
