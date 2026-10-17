namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Tag规则集（编译期生成，运行时只读）
    /// </summary>
    public unsafe struct TagRuleSet
    {
        public const int MAX_REQUIRED_TAGS = 8;
        public const int MAX_BLOCKED_TAGS = 8;
        public const int MAX_ATTACHED_TAGS = 8;
        public const int MAX_REMOVED_TAGS = 8;
        public const int MAX_DISABLED_IF_TAGS = 8;
        public const int MAX_REMOVE_IF_TAGS = 8;
        
        // Required tags（前置条件）：添加该Tag前，目标必须已有这些Tag
        public fixed int RequiredTags[MAX_REQUIRED_TAGS];
        public int RequiredCount;
        
        // Blocked tags（互斥/阻止）：添加该Tag前，目标不能有这些Tag
        public fixed int BlockedTags[MAX_BLOCKED_TAGS];
        public int BlockedCount;
        
        // Attached tags（派生/连带）：添加该Tag时，同时附加这些Tag
        public fixed int AttachedTags[MAX_ATTACHED_TAGS];
        public int AttachedCount;
        
        // Removed tags（互斥/覆盖）：添加该Tag时，同时移除这些Tag
        public fixed int RemovedTags[MAX_REMOVED_TAGS];
        public int RemovedCount;
        
        // Disabled if tags（禁用条件）：若满足条件，该Tag处于"已存在但被禁用"的状态
        public fixed int DisabledIfTags[MAX_DISABLED_IF_TAGS];
        public int DisabledIfCount;
        
        // Remove if tags（自清理条件）：若满足条件，该Tag会被自动移除
        public fixed int RemoveIfTags[MAX_REMOVE_IF_TAGS];
        public int RemoveIfCount;
        
        public bool HasRequiredTags => RequiredCount > 0;
        
        public bool HasBlockedTags => BlockedCount > 0;
        
        public bool HasAttachedTags => AttachedCount > 0;
        
        public bool HasRemovedTags => RemovedCount > 0;
        
        public bool HasDisabledIfTags => DisabledIfCount > 0;
        
        public bool HasRemoveIfTags => RemoveIfCount > 0;
    }
}
