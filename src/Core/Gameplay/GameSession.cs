using System.Collections.Generic;
using System;
using Ludots.Core.Gameplay.Camera;

namespace Ludots.Core.Gameplay
{
    public class GameSession
    {
        private readonly List<Player> _players = new List<Player>();
        private readonly Dictionary<int, PlayerInputFrame> _inputCache = new Dictionary<int, PlayerInputFrame>();

        public Dictionary<string, object> Globals { get; } = new Dictionary<string, object>();

        public int CurrentTick { get; private set; } = 0;

        public CameraManager Camera { get; } = new CameraManager();
        public int LocalPlayerId { get; private set; }

        public void AddPlayer(Player player)
        {
            _players.Add(player);
            if (LocalPlayerId <= 0)
            {
                LocalPlayerId = player.Id;
            }
        }

        public void RemovePlayer(Player player)
        {
            _players.Remove(player);
        }

        public void FixedUpdate()
        {
            // Gather inputs for the current tick
            _inputCache.Clear();
            foreach (var player in _players)
            {
                var input = player.Source.GetInput(CurrentTick);
                _inputCache[player.Id] = input;
            }

            CurrentTick++;
        }

        public void Update(float dt)
        {
            // Reserved for render-frame/session-level hooks.
            // Camera logic advances in fixed-step via CameraRuntimeSystem.
        }

        public PlayerInputFrame GetInput(int playerId)
        {
            if (_inputCache.TryGetValue(playerId, out var input))
            {
                return input;
            }
            return default;
        }

        public IReadOnlyList<Player> Players => _players;

        public void SelectLocalPlayer(int playerId)
        {
            if (playerId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playerId), "Local player id must be positive.");
            }

            LocalPlayerId = playerId;
        }
    }
}
