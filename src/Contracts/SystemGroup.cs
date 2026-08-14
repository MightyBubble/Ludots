namespace Ludots.Core.Engine;

public enum SystemGroup
{
    SchemaUpdate,
    InputCollection,
    PostMovement,
    AbilityActivation,
    EffectProcessing,
    RuntimeEntityBinding,
    AttributeCalculation,
    DeferredTriggerCollection,
    Cleanup,
    EventDispatch,
    ClearPresentationFlags,
}
