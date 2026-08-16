using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using DesertStrikeShowcaseMod.Runtime;

namespace DesertStrikeShowcaseMod.Systems
{
    public sealed class DesertStrikeWaveSystem : BaseSystem<World, float>
    {
        private readonly DesertStrikeState _state;
        private readonly DesertStrikeConfig _config;
        private readonly IClock _clock;
        private readonly RuntimeEntitySpawnQueue _spawnQueue;
        private readonly RuntimeEntitySpawnReceiptQueue? _receipts;

        public DesertStrikeWaveSystem(GameEngine engine, DesertStrikeState state, DesertStrikeConfig config)
            : base(engine.World)
        {
            _state = state;
            _config = config;
            _clock = engine.GetService(CoreServiceKeys.Clock);
            _spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue);
            engine.TryGetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue, out _receipts);
        }

        public override void Update(in float dt)
        {
            CollectReceipts();
            if (_state.GameOver)
            {
                return;
            }

            if (!World.IsAlive(_state.PlayerBase) || !World.IsAlive(_state.AiBase))
            {
                return;
            }

            int step = _clock.Now(ClockDomainId.FixedFrame);
            if (step < _state.NextWaveStep)
            {
                return;
            }

            _state.NextWaveStep = step + _config.WaveIntervalTicks;
            _state.WaveNumber++;
            SpawnSide(player: true, _state.PlayerTeam, playerId: 1);
            SpawnSide(player: false, _state.AiTeam, playerId: 2);
        }

        private void CollectReceipts()
        {
            if (_receipts == null)
            {
                return;
            }

            while (_receipts.TryDequeueForChannel(_state.WaveReceiptChannelId, out var receipt))
            {
                if (World.IsAlive(receipt.Entity))
                {
                    _state.UnitsSpawned++;
                }
            }
        }

        private void SpawnSide(bool player, int team, int playerId)
        {
            var queue = player ? _state.PlayerQueue : _state.AiQueue;
            var markers = player ? _state.PlayerSpawnMarkers : _state.AiSpawnMarkers;

            for (int i = 0; i < queue.Count; i++)
            {
                DesertStrikePurchase purchase = queue[i];
                if (!_config.Units.TryGetValue(purchase.UnitId, out DesertStrikeConfig.UnitConfig? unit))
                {
                    throw new InvalidOperationException(
                        $"DS.WAVE.ERR.UnknownUnit: unitId={purchase.UnitId}.");
                }

                if (!markers.TryGetValue(purchase.LaneIndex, out Arch.Core.Entity marker) || !World.IsAlive(marker))
                {
                    throw new InvalidOperationException(
                        $"DS.WAVE.ERR.MissingLaneMarker: lane={purchase.LaneIndex}.");
                }

                var markerCm = World.Get<Ludots.Core.Components.WorldPositionCm>(marker).Value.ToWorldCmInt2();
                (int offsetX, int offsetY) = ScatterOffset(i);
                var request = new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = unit.Template,
                    HasWorldPosition = 1,
                    WorldPositionCm = Fix64Vec2.FromInt(markerCm.X + offsetX, markerCm.Y + offsetY),
                    TeamIdOverride = team,
                    PlayerOwnerIdOverride = playerId,
                    EmitReceipt = 1,
                    ReceiptChannelId = _state.WaveReceiptChannelId,
                };

                if (!_spawnQueue.TryEnqueue(in request))
                {
                    throw new InvalidOperationException("DS.WAVE.ERR.SpawnQueueFull");
                }
            }

            queue.Clear();
        }

        private static (int OffsetX, int OffsetY) ScatterOffset(int index)
        {
            int slot = index % 9;
            int column = slot % 3 - 1;
            int row = slot / 3 - 1;
            return (column * 130, row * 130);
        }
    }
}
