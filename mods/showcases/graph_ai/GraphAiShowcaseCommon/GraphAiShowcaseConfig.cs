using System.Collections.Generic;

namespace GraphAiShowcaseCommon;

public sealed class GraphAiShowcaseConfig
{
    public int SchemaVersion { get; set; }
    public string ShowcaseId { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public float BeatSeconds { get; set; }
    public string GraphProgramId { get; set; } = string.Empty;
    public string Boundary { get; set; } = string.Empty;
    public GraphAiOutputConfig Outputs { get; set; } = new();
    public GraphAiHotPathConfig HotPath { get; set; } = new();
    public GraphAiStressFieldConfig StressField { get; set; } = new();
    public GraphAiLevelFlowConfig LevelFlow { get; set; } = new();
    public GraphAiWorldTargetConfig WorldTargets { get; set; } = new();
    public Dictionary<string, string> StateLabels { get; set; } = new();
    public Dictionary<string, string> IntentLabels { get; set; } = new();
    public Dictionary<string, string> TaskLabels { get; set; } = new();
    public List<GraphAiProgramConfig> Programs { get; set; } = new();
    public List<GraphAiActorConfig> Actors { get; set; } = new();
}

public sealed class GraphAiOutputConfig
{
    public int StateRegister { get; set; } = 10;
    public int IntentRegister { get; set; } = 11;
    public int BtNodeRegister { get; set; } = 12;
    public int TaskIdRegister { get; set; } = 13;
    public int TaskDurationRegister { get; set; } = 14;
}

public sealed class GraphAiProgramConfig
{
    public string Id { get; set; } = string.Empty;
    public List<GraphAiInstructionConfig> Instructions { get; set; } = new();
}

public sealed class GraphAiInstructionConfig
{
    public string Op { get; set; } = string.Empty;
    public int Dst { get; set; }
    public int A { get; set; }
    public int B { get; set; }
    public int C { get; set; }
    public int Imm { get; set; }
}

public sealed class GraphAiActorConfig
{
    public string Name { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public int State { get; set; }
    public int BtNode { get; set; }
    public int EnemyDistanceCm { get; set; }
    public int Health { get; set; } = 100;
    public int Morale { get; set; } = 50;
}

public sealed class GraphAiHotPathConfig
{
    public int EntityCount { get; set; }
}

public sealed class GraphAiStressFieldConfig
{
    public int EntityCount { get; set; }
    public string FsmProgramId { get; set; } = string.Empty;
    public string BtProgramId { get; set; } = string.Empty;
    public int PrimitiveStableIdBase { get; set; }
    public int Columns { get; set; }
    public int BaseXCm { get; set; }
    public int BaseYCm { get; set; }
    public int SpacingCm { get; set; }
    public int WaveAmplitudeCm { get; set; }
    public float WaveFrequency { get; set; }
    public float PrimitiveScaleMeters { get; set; }
    public List<GraphAiStressStateColorConfig> StateColors { get; set; } = new();
}

public sealed class GraphAiStressStateColorConfig
{
    public int State { get; set; }
    public float R { get; set; }
    public float G { get; set; }
    public float B { get; set; }
    public float A { get; set; } = 1f;
}

public sealed class GraphAiLevelFlowConfig
{
    public string CursorInstanceId { get; set; } = string.Empty;
    public string MoveActionId { get; set; } = string.Empty;
    public int CursorSpeedCmPerSecond { get; set; }
    public int TriggerRadiusCm { get; set; }
    public List<GraphAiLevelStepConfig> Steps { get; set; } = new();
}

public sealed class GraphAiLevelStepConfig
{
    public string InstanceId { get; set; } = string.Empty;
    public string TargetInstanceId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = string.Empty;
    public int TargetActiveOffsetXCm { get; set; }
    public int TargetActiveOffsetYCm { get; set; }
    public int TargetCompleteOffsetXCm { get; set; }
    public int TargetCompleteOffsetYCm { get; set; }
    public int TargetWobbleXCm { get; set; }
    public int TargetWobbleYCm { get; set; }
    public float TargetWobbleXFrequency { get; set; }
    public float TargetWobbleYFrequency { get; set; }
}

public sealed class GraphAiWorldTargetConfig
{
    public List<GraphAiMotionTargetConfig> StanceByState { get; set; } = new();
    public List<GraphAiMotionTargetConfig> BehaviorByTask { get; set; } = new();
}

public sealed class GraphAiMotionTargetConfig
{
    public int Key { get; set; }
    public string InstanceId { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = string.Empty;
    public int SpeedCmPerSecond { get; set; } = 900;
    public int OffsetXCm { get; set; }
    public int OffsetYCm { get; set; }
    public int WobbleXCm { get; set; }
    public int WobbleYCm { get; set; }
    public float WobbleXFrequency { get; set; }
    public float WobbleYFrequency { get; set; }
    public bool ForceFacing { get; set; }
    public float FacingRad { get; set; }
    public bool RotateFacing { get; set; }
    public bool UseActorHomeY { get; set; }
}
