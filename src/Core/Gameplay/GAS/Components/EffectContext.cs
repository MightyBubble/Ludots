using Arch.Core;
using Ludots.Core.EntityHistory;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public struct EffectContext
    {
        public int RootId;
        public Entity Source;
        public Entity Target;
        public Entity TargetContext;
        public byte HasTargetRef;
        public EffectTargetRef TargetRef;
        // Could add Level, Seed, etc.
    }
}
