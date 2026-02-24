using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    public struct EffectHandle
    {
        public byte Kind;
        public int Index;
        public Entity Entity;

        public static EffectHandle Proposal(int index)
        {
            return new EffectHandle { Kind = 0, Index = index, Entity = default };
        }

        public static EffectHandle EntityEffect(Entity entity)
        {
            return new EffectHandle { Kind = 1, Index = 0, Entity = entity };
        }
    }

    public struct EffectProposal
    {
        public int RootId;
        public Entity Source;
        public Entity Target;
        public Entity TargetContext;
        public int TemplateId;
        public int TagId;
        public bool ParticipatesInResponse;
        public bool Cancelled;
        public EffectModifiers Modifiers;

        /// <summary>Caller-supplied config param overrides, carried from EffectRequest.</summary>
        public EffectConfigParams CallerParams;
        public bool HasCallerParams;
    }
}
