using System.Collections.Generic;
using Ludots.Core.Gameplay.Camera;

namespace Ludots.Core.Gameplay
{
    public class GameSession
    {
        public Dictionary<string, object> Globals { get; } = new();

        public int CurrentTick { get; private set; } = 0;

        public CameraManager Camera { get; } = new CameraManager();

        public void FixedUpdate()
        {
            // 纯逻辑 Tick 计数；输入收集由 PlayerInputHandler 链路负责。
            CurrentTick++;
        }

        public void Update(float dt)
        {
            Camera.Update(dt);
        }
    }
}
