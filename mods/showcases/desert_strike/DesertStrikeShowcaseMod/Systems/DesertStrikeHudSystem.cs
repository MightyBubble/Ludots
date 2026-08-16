using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using DesertStrikeShowcaseMod.Runtime;

namespace DesertStrikeShowcaseMod.Systems
{
    public sealed class DesertStrikeHudSystem : BaseSystem<World, float>
    {
        private readonly DesertStrikeState _state;
        private readonly DesertStrikeConfig _config;
        private readonly IClock _clock;
        private readonly ScreenOverlayBuffer? _overlay;
        private readonly int _mineralsAttributeId;
        private readonly int _healthAttributeId;

        public DesertStrikeHudSystem(GameEngine engine, DesertStrikeState state, DesertStrikeConfig config)
            : base(engine.World)
        {
            _state = state;
            _config = config;
            _clock = engine.GetService(CoreServiceKeys.Clock);
            engine.TryGetService(CoreServiceKeys.ScreenOverlayBuffer, out _overlay);
            _mineralsAttributeId = EnsureAttributeId("Minerals");
            _healthAttributeId = EnsureAttributeId("Health");
        }

        public override void Update(in float dt)
        {
            if (_overlay == null)
            {
                return;
            }

            int step = _clock.Now(ClockDomainId.FixedFrame);
            int waveSeconds = Math.Max(0, (_state.NextWaveStep - step) / 60);

            _overlay.AddRect(8, 8, 480, 132, new Vector4(0f, 0f, 0f, 0.55f), new Vector4(1f, 1f, 1f, 0.18f), stableId: 100, dirtySerial: 0);
            _overlay.AddText(16, 14, "沙漠风暴 · Desert Strike (Tug of War)", 22, new Vector4(1f, 0.9f, 0.5f, 1f), stableId: 101, dirtySerial: 0);
            _overlay.AddText(16, 42, $"水晶: {ReadMinerals(_state.PlayerBase):0}    下一波: {waveSeconds}s    波次: {_state.WaveNumber}", 18, new Vector4(0.6f, 1f, 0.7f, 1f), stableId: 102, dirtySerial: 0);
            _overlay.AddText(16, 66, $"本波待发: {_state.PlayerQueue.Count} 单位    AI 水晶: {ReadMinerals(_state.AiBase):0}    AI 待发: {_state.AiQueue.Count}", 18, new Vector4(0.78f, 0.92f, 1f, 1f), stableId: 103, dirtySerial: 0);
            _overlay.AddText(16, 90, $"我方基地 HP: {ReadHealth(_state.PlayerBase):0}    敌方基地 HP: {ReadHealth(_state.AiBase):0}", 18, new Vector4(1f, 0.6f, 0.6f, 1f), stableId: 104, dirtySerial: 0);
            _overlay.AddText(16, 112, "玩法：选中我方基地（绿圈）→ 点击下方按钮购买单位 → 每 30 秒自动出兵，摧毁敌方基地获胜", 16, new Vector4(0.85f, 0.85f, 0.85f, 1f), stableId: 105, dirtySerial: 0);

            if (_state.GameOver)
            {
                bool localVictory = _state.WinnerPlayerId == 1;
                _overlay.AddRect(340, 280, 600, 120, new Vector4(0f, 0f, 0f, 0.72f), new Vector4(localVictory ? 0.3f : 1f, localVictory ? 1f : 0.3f, 0.3f, 1f), stableId: 200, dirtySerial: 0);
                _overlay.AddText(
                    360,
                    316,
                    localVictory ? "胜利！敌方基地已被摧毁" : "失败！我方基地已被摧毁",
                    34,
                    new Vector4(1f, 1f, 1f, 1f),
                    stableId: 201,
                    dirtySerial: 0);
            }
        }

        private float ReadMinerals(Entity baseEntity)
        {
            if (!World.IsAlive(baseEntity) || !World.Has<AttributeBuffer>(baseEntity))
            {
                return 0f;
            }

            return World.Get<AttributeBuffer>(baseEntity).GetCurrent(_mineralsAttributeId);
        }

        private float ReadHealth(Entity baseEntity)
        {
            if (!World.IsAlive(baseEntity) || !World.Has<AttributeBuffer>(baseEntity))
            {
                return 0f;
            }

            return World.Get<AttributeBuffer>(baseEntity).GetCurrent(_healthAttributeId);
        }

        private static int EnsureAttributeId(string attributeName)
        {
            int id = AttributeRegistry.GetId(attributeName);
            return id > 0 ? id : AttributeRegistry.Register(attributeName);
        }
    }
}
