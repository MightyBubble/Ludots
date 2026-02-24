using Arch.System;
using Ludots.Core.Gameplay;

namespace Ludots.Core.Systems
{
    /// <summary>
    /// Wraps GameSession.FixedUpdate into an ECS System.
    /// Ensures input gathering and tick increment happen in sync with the simulation loop.
    /// </summary>
    public class GameSessionSystem : ISystem<float>
    {
        private readonly GameSession _session;

        public GameSessionSystem(GameSession session)
        {
            _session = session;
        }

        public void Initialize()
        {
            // No initialization needed
        }

        public void Update(in float dt)
        {
            _session.FixedUpdate();
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
