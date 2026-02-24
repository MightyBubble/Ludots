using System.Collections.Generic;
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

        public void AddPlayer(Player player)
        {
            _players.Add(player);
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
            Camera.Update(dt);
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
    }
}
