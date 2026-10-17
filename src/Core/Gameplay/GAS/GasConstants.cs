namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// GAS系统常量定义（参考10_关键裁决条款.md）
    /// </summary>
    public static class GasConstants
    {
        /// <summary>
        /// 响应链局部窗口深度上限
        /// </summary>
        public const int MAX_DEPTH = 5;
        
        /// <summary>
        /// 单个根Effect的最大创建数
        /// </summary>
        public const int MAX_CREATES_PER_ROOT = 256;

        public const int MAX_RESPONSE_STEPS_PER_WINDOW = 5000;
        public const int MAX_RESPONSES_PER_WINDOW = 4096;
        public const int MAX_EFFECT_PROCESSING_PASSES_PER_FRAME = 64;

        public const int MAX_BLACKBOARD_ENTRIES = 32;
        public const int MAX_CHILDREN_BUFFER_CAPACITY = 16;
        
        public const int MAX_TAG_RULE_TRANSACTION_STEPS = 256;
        
        public const int MAX_PROCESSED_SET_CAPACITY = 256;
        
        public const int MAX_DEFERRED_TRIGGERS_PER_FRAME = 1024;

        public const int MAX_GAMEPLAY_EVENTS_PER_FRAME = 4096;

        public const int MAX_EFFECT_REQUESTS_PER_FRAME = 4096;

        public const int EFFECT_MODIFIERS_CAPACITY = 8;

        public const int EFFECT_CONFIG_PARAMS_MAX = 32;

        public const int EFFECT_GRANTED_TAGS_MAX = 8;

        public const int ACTIVE_EFFECT_CONTAINER_CAPACITY = 32;

        public const int EFFECT_PHASE_LISTENER_CAPACITY = 8;

        public const int EFFECT_PHASE_GRAPH_MAX_STEPS = EffectPhaseConstants.PhaseCount * 3;

        public const int GLOBAL_PHASE_LISTENER_MAX = 32;
    }
}
