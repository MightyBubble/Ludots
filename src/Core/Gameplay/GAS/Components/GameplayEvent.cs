using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Transient event entity.
    /// </summary>
    public struct GameplayEvent
    {
        public int TagId;
        public Entity Source;
        public Entity Target;
        public float Magnitude;
    }
}
