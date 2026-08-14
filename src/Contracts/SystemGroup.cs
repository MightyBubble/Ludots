namespace Ludots.Core.Engine;

public enum SystemGroup
{
    SchemaUpdate,

    // 本地输入意图。复制客户端只执行这一组，不运行权威仿真。
    LocalInput,

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
