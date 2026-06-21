using Arch.System;
using CameraAcceptanceMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceInputOwnershipSystem : ISystem<float>, IInputFrameConsumer
    {
        private readonly GameEngine _engine;

        public CameraAcceptanceInputOwnershipSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            CameraAcceptanceRuntime.SyncMapScopedInputOwnership(_engine);
        }

        public void Consume(GameEngine engine, PlayerInputHandler input, float deltaTime)
        {
            CameraAcceptanceRuntime.SyncMapScopedInputOwnership(engine);
        }
    }
}
