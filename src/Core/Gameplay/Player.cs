using Ludots.Core.Gameplay.Camera;

namespace Ludots.Core.Gameplay
{
    public class Player
    {
        public int Id { get; }
        public int TeamId { get; set; }
        public IInputSource Source { get; }

        /// <summary>
        /// Pure data state for the player's camera.
        /// Each player has their own camera configuration.
        /// </summary>
        public CameraState Camera { get; set; } = new CameraState();

        public Player(int id, IInputSource source)
        {
            Id = id;
            Source = source;
        }
    }
}
