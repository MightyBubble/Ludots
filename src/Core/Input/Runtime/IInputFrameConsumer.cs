using Ludots.Core.Engine;

namespace Ludots.Core.Input.Runtime
{
    public interface IInputFrameConsumer
    {
        void Consume(GameEngine engine, PlayerInputHandler input, float deltaTime);
    }
}
