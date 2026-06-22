using Arch.Core;
using Arch.System;
using ChampionSkillSandboxMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace ChampionSkillSandboxMod.Systems
{
    internal sealed class ChampionSkillPhysicsAuthoringSystem : ISystem<float>
    {
        private static readonly QueryDescription ChampionBodyQuery = new QueryDescription()
            .WithAll<WorldPositionCm, Collider2D, AbilityStateBuffer>();

        private readonly GameEngine _engine;

        public ChampionSkillPhysicsAuthoringSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!ChampionSkillSandboxIds.IsSandboxMap(_engine.CurrentMapSession?.MapId.Value))
            {
                return;
            }

            _engine.World.Query(in ChampionBodyQuery, (Entity entity, ref WorldPositionCm worldPosition, ref Collider2D _, ref AbilityStateBuffer __) =>
            {
                if (!_engine.World.Has<Position2D>(entity))
                {
                    _engine.World.Add(entity, new Position2D { Value = worldPosition.Value });
                }
                else
                {
                    ref var position = ref _engine.World.Get<Position2D>(entity);
                    position.Value = worldPosition.Value;
                }

                if (!_engine.World.Has<PreviousPosition2D>(entity))
                {
                    _engine.World.Add(entity, new PreviousPosition2D { Value = worldPosition.Value });
                }

                if (!_engine.World.Has<Velocity2D>(entity))
                {
                    _engine.World.Add(entity, Velocity2D.Zero);
                }

                if (!_engine.World.Has<Mass2D>(entity))
                {
                    _engine.World.Add(entity, Mass2D.FromFloat(inverseMass: 1f, inverseInertia: 1f));
                }
            });
        }
    }
}
