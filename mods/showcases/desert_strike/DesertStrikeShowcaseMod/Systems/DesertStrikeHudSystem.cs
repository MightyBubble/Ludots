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

        public DesertStrikeHudSystem(GameEngine engine, DesertStrikeState state, DesertStrikeConfig config)
            : base(engine.World)
        {
            _state = state;
            _config = config;
            _clock = engine.GetService(CoreServiceKeys.Clock);
            engine.TryGetService(CoreServiceKeys.ScreenOverlayBuffer, out _overlay);
            _mineralsAttributeId = EnsureAttributeId("Minerals");
        }

        public override void Update(in float dt)
        {
            if (_overlay == null)
            {
                return;
            }

            int step = _clock.Now(ClockDomainId.FixedFrame);
            int waveSeconds = Math.Max(0, (_state.NextWaveStep - step) / 60);
            _overlay.AddRect(
                x: 8,
                y: 8,
                width: 460,
                height: 110,
                fill: new Vector4(0f, 0f, 0f, 0.45f),
                border: new Vector4(1f, 1f, 1f, 0.16f));
            _overlay.AddText(16, 16, $"Minerals: {ReadMinerals(_state.PlayerBase):0}", 20, new Vector4(0.6f, 1f, 0.7f, 1f));
            _overlay.AddText(16, 42, $"Next wave: {waveSeconds}s | Wave {_state.WaveNumber} | Queue {_state.PlayerQueue.Count}", 16, new Vector4(0.78f, 0.92f, 1f, 1f));
            _overlay.AddText(16, 66, $"AI Minerals: {ReadMinerals(_state.AiBase):0} | AI Queue {_state.AiQueue.Count}", 16, new Vector4(1f, 0.78f, 0.6f, 1f));
            _overlay.AddText(16, 90, $"Units {_state.UnitsSpawned} spawned / {_state.UnitsDestroyed} destroyed", 16, new Vector4(0.7f, 0.7f, 0.7f, 1f));

            if (_state.GameOver)
            {
                bool localVictory = _state.WinnerPlayerId == 1;
                _overlay.AddRect(
                    x: 340,
                    y: 280,
                    width: 600,
                    height: 120,
                    fill: new Vector4(0f, 0f, 0f, 0.7f),
                    border: new Vector4(localVictory ? 0.3f : 1f, localVictory ? 1f : 0.3f, 0.3f, 1f));
                _overlay.AddText(
                    360,
                    316,
                    localVictory ? "VICTORY — Enemy base destroyed" : "DEFEAT — Your base was destroyed",
                    32,
                    new Vector4(1f, 1f, 1f, 1f));
            }
        }

        private float ReadMinerals(Arch.Core.Entity baseEntity)
        {
            if (!World.IsAlive(baseEntity) || !World.Has<AttributeBuffer>(baseEntity))
            {
                return 0f;
            }

            return World.Get<AttributeBuffer>(baseEntity).GetCurrent(_mineralsAttributeId);
        }

        private static int EnsureAttributeId(string attributeName)
        {
            int id = AttributeRegistry.GetId(attributeName);
            return id > 0 ? id : AttributeRegistry.Register(attributeName);
        }
    }
}
