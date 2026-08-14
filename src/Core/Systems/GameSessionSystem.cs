using Arch.System;
using Ludots.Core.Gameplay;

namespace Ludots.Core.Systems
{
    /// <summary>
    /// Gathers player inputs for the authoritative tick already opened by the fixed-step boundary.
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
            _session.CollectFixedUpdateInputs();
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
