using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Knowledge;

namespace Ludots.Core.EntityHistory;

public static class EffectExecutionRecordFactory
{
    public static EffectExecutionRecord Create(
        in EffectContext context,
        int effectTemplateId,
        in EffectTargetRef target,
        int executedTick,
        EffectTargetResolveResult result,
        int delayTicks,
        int knowledgeTtlTicks,
        long attributeDeltaRaw,
        in KnowledgeIdMask256 tagAdded,
        in KnowledgeIdMask256 tagRemoved)
    {
        EntityRef source = EntityRef.From(context.Source);
        return new EffectExecutionRecord(
            context.RootId,
            effectTemplateId,
            in source,
            in target,
            executedTick,
            result,
            delayTicks,
            knowledgeTtlTicks,
            attributeDeltaRaw,
            in tagAdded,
            in tagRemoved);
    }
}
