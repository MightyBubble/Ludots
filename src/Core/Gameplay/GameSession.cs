using System.Collections.Generic;
using System;
using Ludots.Core.Gameplay.Camera;

namespace Ludots.Core.Gameplay
{
    public sealed record GameSessionSnapshot(
        int CurrentTick,
        IReadOnlyList<PlayerSnapshot> Players,
        IReadOnlyDictionary<string, object> Globals,
        CameraStateSnapshot Camera);

    public sealed record PlayerSnapshot(int Id, int TeamId, CameraStateSnapshot Camera);

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

        public GameSessionSnapshot CaptureSnapshot()
        {
            var players = new PlayerSnapshot[_players.Count];
            for (int i = 0; i < _players.Count; i++)
            {
                Player player = _players[i];
                players[i] = new PlayerSnapshot(
                    player.Id,
                    player.TeamId,
                    CameraStateSnapshot.FromState(player.Camera));
            }

            var globals = new Dictionary<string, object>(Globals.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> pair in Globals)
            {
                globals[pair.Key] = CopySerializableGlobal(pair.Key, pair.Value);
            }

            return new GameSessionSnapshot(
                CurrentTick,
                players,
                globals,
                CameraStateSnapshot.FromState(Camera.State));
        }

        public void RestoreSnapshot(GameSessionSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.CurrentTick < 0)
            {
                throw new InvalidOperationException("GameSession snapshot CurrentTick must not be negative.");
            }

            _players.Clear();
            _inputCache.Clear();
            for (int i = 0; i < snapshot.Players.Count; i++)
            {
                PlayerSnapshot playerSnapshot = snapshot.Players[i];
                if (playerSnapshot.Id <= 0)
                {
                    throw new InvalidOperationException("GameSession snapshot player id must be positive.");
                }

                var player = new Player(playerSnapshot.Id, NullInputSource.Instance)
                {
                    TeamId = playerSnapshot.TeamId
                };
                playerSnapshot.Camera.ApplyTo(player.Camera);
                _players.Add(player);
            }

            Globals.Clear();
            foreach (KeyValuePair<string, object> pair in snapshot.Globals)
            {
                Globals[pair.Key] = CopySerializableGlobal(pair.Key, pair.Value);
            }

            CurrentTick = snapshot.CurrentTick;
            snapshot.Camera.ApplyTo(Camera.State);
            snapshot.Camera.ApplyTo(Camera.PreviousState);
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

        private static object CopySerializableGlobal(string key, object value)
        {
            return value switch
            {
                null => string.Empty,
                string text => text,
                bool boolean => boolean,
                int integer => integer,
                long longInteger => longInteger,
                float single => single,
                double number => number,
                _ => throw new InvalidOperationException(
                    $"GameSession.Globals['{key}'] has unsupported save value type '{value.GetType().FullName}'.")
            };
        }

        private sealed class NullInputSource : IInputSource
        {
            public static readonly NullInputSource Instance = new();

            public PlayerInputFrame GetInput(int tick)
            {
                return new PlayerInputFrame { Tick = tick };
            }
        }
    }
}
