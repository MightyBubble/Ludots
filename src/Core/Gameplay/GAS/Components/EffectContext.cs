using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public struct EffectContext
    {
        public int RootId;
        public Entity Source;
        public Entity Target;
        public Entity TargetContext;
        // Could add Level, Seed, etc.
    }
}
