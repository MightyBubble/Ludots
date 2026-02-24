using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Response Chain响应类型
    /// </summary>
    public enum ResponseType : byte
    {
        Hook = 0,      // 拦截（取消Effect）
        Modify = 1,    // 修改（改变Effect数值）
        Chain = 2,     // 连锁（创建新Effect）
        PromptInput = 3
    }
    
    /// <summary>
    /// Response Chain响应项
    /// </summary>
    public struct ResponseChainItem
    {
        public Entity EffectEntity;      // 被响应的Effect
        public Entity ResponseEntity;    // 响应者（Hook/Modify/Chain的创建者）
        public ResponseType Type;        // Hook/Modify/Chain
        public int Priority;             // 优先级（用于排序）

        public int StableSequence;
        
        /// <summary>
        /// 用于Modify的修改值（仅Modify类型使用）
        /// </summary>
        public float ModifyValue;

        public ModifierOp ModifyOp;
        
        /// <summary>
        /// 用于Chain的Effect模板ID（仅Chain类型使用）
        /// </summary>
        public int EffectTemplateId;
    }
    
    /// <summary>
    /// Response Chain监听器组件
    /// 监听EffectPendingEvent并响应（Hook/Modify/Chain）
    /// </summary>
    public unsafe struct ResponseChainListener
    {
        public const int CAPACITY = 8;
        
        /// <summary>
        /// 监听的Event Tag ID（匹配EffectPendingEvent）
        /// </summary>
        public fixed int EventTagIds[CAPACITY];
        
        /// <summary>
        /// Response类型（Hook/Modify/Chain）
        /// </summary>
        public fixed byte ResponseTypes[CAPACITY];
        
        /// <summary>
        /// 优先级（用于排序，数值越大优先级越高）
        /// </summary>
        public fixed int Priorities[CAPACITY];
        
        /// <summary>
        /// Chain时创建的Effect模板ID（仅Chain类型使用）
        /// </summary>
        public fixed int EffectTemplateIds[CAPACITY];
        
        /// <summary>
        /// Modify时的修改值（仅Modify类型使用）
        /// </summary>
        public fixed float ModifyValues[CAPACITY];

        public fixed byte ModifyOps[CAPACITY];

        /// <summary>
        /// Optional Graph program IDs for dynamic response computation.
        /// 0 = use static ModifyValue/EffectTemplateId; >0 = execute Graph to compute output.
        /// Graph convention:
        ///   E[0] = responder entity (owner of this listener)
        ///   E[1] = target of the effect being responded to
        ///   Output: F[0] = computed ModifyValue (for Modify type)
        ///           I[0] = computed EffectTemplateId (for Chain type)
        /// </summary>
        public fixed int ResponseGraphIds[CAPACITY];
        
        /// <summary>
        /// 当前监听器数量
        /// </summary>
        public int Count;
        
        /// <summary>
        /// 添加监听器
        /// </summary>
        public bool Add(int eventTagId, ResponseType type, int priority, int effectTemplateId = -1, float modifyValue = 0f, ModifierOp modifyOp = ModifierOp.Add, int responseGraphId = 0)
        {
            if (Count >= CAPACITY) return false;
            
            EventTagIds[Count] = eventTagId;
            ResponseTypes[Count] = (byte)type;
            Priorities[Count] = priority;
            EffectTemplateIds[Count] = effectTemplateId;
            ModifyValues[Count] = modifyValue;
            ModifyOps[Count] = (byte)modifyOp;
            ResponseGraphIds[Count] = responseGraphId;
            Count++;
            return true;
        }
        
        /// <summary>
        /// 检查是否监听指定的Event Tag
        /// </summary>
        public bool MatchesEventTag(int eventTagId)
        {
            for (int i = 0; i < Count; i++)
            {
                if (EventTagIds[i] == eventTagId)
                {
                    return true;
                }
            }
            return false;
        }
    }
    
    /// <summary>
    /// 标记Effect已被Hook（取消）
    /// </summary>
    public struct EffectCancelled
    {
    }
    
    /// <summary>
    /// 标记Effect已被修改（用于追踪修改历史）
    /// </summary>
    public struct EffectModified
    {
        public float OriginalValue;
        public float ModifiedValue;
    }
}
