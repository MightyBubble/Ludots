namespace Ludots.Core.Gameplay.AI.Config
{
    public sealed class AiAtomsConfig
    {
        public AiAtomConfig[] Atoms { get; set; } = System.Array.Empty<AiAtomConfig>();
    }

    public sealed class AiAtomConfig
    {
        public string Id { get; set; } = string.Empty;
    }

    public sealed class AiProjectionConfig
    {
        public AiProjectionRuleConfig[] Rules { get; set; } = System.Array.Empty<AiProjectionRuleConfig>();
    }

    public sealed class AiProjectionRuleConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Atom { get; set; } = string.Empty;
        public string Op { get; set; } = string.Empty;
        public int IntKey { get; set; }
        public int IntValue { get; set; }
        public int EntityKey { get; set; }
    }

    public sealed class AiUtilityConfig
    {
        public AiGoalPresetConfig[] Goals { get; set; } = System.Array.Empty<AiGoalPresetConfig>();
    }

    public sealed class AiGoalPresetConfig
    {
        public string Id { get; set; } = string.Empty;
        public int GoalPresetId { get; set; }
        public int PlanningStrategyId { get; set; }
        public float Weight { get; set; } = 1f;
        public AiBoolConsiderationConfig[] Bool { get; set; } = System.Array.Empty<AiBoolConsiderationConfig>();
    }

    public sealed class AiBoolConsiderationConfig
    {
        public string Atom { get; set; } = string.Empty;
        public float TrueScore { get; set; } = 1f;
        public float FalseScore { get; set; } = 1f;
    }

    public sealed class AiGoapConfig
    {
        public AiGoapActionConfig[] Actions { get; set; } = System.Array.Empty<AiGoapActionConfig>();
        public AiGoapGoalPresetConfig[] Goals { get; set; } = System.Array.Empty<AiGoapGoalPresetConfig>();
    }

    public sealed class AiGoapActionConfig
    {
        public string Id { get; set; } = string.Empty;
        public int ActionId { get; set; }
        public int Cost { get; set; } = 1;
        public AiWorldStateConditionConfig Pre { get; set; } = new AiWorldStateConditionConfig();
        public AiWorldStateConditionConfig Post { get; set; } = new AiWorldStateConditionConfig();
        public AiOrderExecConfig Order { get; set; } = new AiOrderExecConfig();
        public AiBindingConfig[] Bindings { get; set; } = System.Array.Empty<AiBindingConfig>();
    }

    public sealed class AiGoapGoalPresetConfig
    {
        public string Id { get; set; } = string.Empty;
        public int GoalPresetId { get; set; }
        public int HeuristicWeight { get; set; } = 1;
        public AiWorldStateConditionConfig Goal { get; set; } = new AiWorldStateConditionConfig();
    }

    public sealed class AiHtnConfig
    {
        public AiHtnTaskConfig[] Tasks { get; set; } = System.Array.Empty<AiHtnTaskConfig>();
        public AiHtnMethodConfig[] Methods { get; set; } = System.Array.Empty<AiHtnMethodConfig>();
        public AiHtnSubtaskConfig[] Subtasks { get; set; } = System.Array.Empty<AiHtnSubtaskConfig>();
        public AiHtnRootConfig[] Roots { get; set; } = System.Array.Empty<AiHtnRootConfig>();
    }

    public sealed class AiHtnTaskConfig
    {
        public string Id { get; set; } = string.Empty;
        public int TaskId { get; set; }
        public int FirstMethod { get; set; }
        public int MethodCount { get; set; }
    }

    public sealed class AiHtnMethodConfig
    {
        public string Id { get; set; } = string.Empty;
        public int MethodId { get; set; }
        public int Cost { get; set; }
        public AiWorldStateConditionConfig Condition { get; set; } = new AiWorldStateConditionConfig();
        public int SubtaskOffset { get; set; }
        public int SubtaskCount { get; set; }
    }

    public sealed class AiHtnSubtaskConfig
    {
        public string Id { get; set; } = string.Empty;
        public int Index { get; set; }
        public string Kind { get; set; } = string.Empty;
        public int RefId { get; set; }
    }

    public sealed class AiHtnRootConfig
    {
        public string Id { get; set; } = string.Empty;
        public int GoalPresetId { get; set; }
        public int RootTaskId { get; set; }
    }

    public sealed class AiWorldStateConditionConfig
    {
        public string[] Mask { get; set; } = System.Array.Empty<string>();
        public string[] Values { get; set; } = System.Array.Empty<string>();
    }

    public sealed class AiOrderExecConfig
    {
        public int OrderTagId { get; set; }
        public byte SubmitMode { get; set; }
        public int PlayerId { get; set; }
    }

    public sealed class AiBindingConfig
    {
        public string Op { get; set; } = string.Empty;
        public int SourceKey { get; set; }
    }
}

