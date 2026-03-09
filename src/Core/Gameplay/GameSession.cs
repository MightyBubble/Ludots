using System.Collections.Generic;
using Ludots.Core.Gameplay.Camera;

namespace Ludots.Core.Gameplay
{
    public class GameSession
    {
        private readonly List<Player> _players = new List<Player>();

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
            CurrentTick++;
        }

        public void Update(float dt)
        {
            Camera.Update(dt);
        }

        public PlayerInputFrame GetInput(int playerId)
        {
            return default;
        }

        public IReadOnlyList<Player> Players => _players;
    }
}
