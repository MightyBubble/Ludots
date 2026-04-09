using System;
using Arch.System;
using Ludots.Core.Engine;
using RtsDemoMod.Runtime;

namespace RtsDemoMod.Systems
{
    public sealed class RtsPresentationBootstrapSystem : ISystem<float>
    {
        private readonly GameEngine _engine;

        public RtsPresentationBootstrapSystem(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (_engine.CurrentMapSession?.MapConfig == null)
            {
                return;
            }

            RtsPresentationBootstrapper.EnsureReadableActors(_engine, _engine.World);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }
    }
}
