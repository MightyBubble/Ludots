namespace TimeFlowShowcaseMod;

public enum TimeFlowScenarioKind
{
    AtbWait,
    DotaManualUlt,
    BreakFever,
    SentinelCommandPause,
    Ck3Macro,
    BadNorthActivePause
}

public sealed class TimeFlowShowcaseSnapshot
{
    public string MapId { get; init; } = string.Empty;
    public TimeFlowScenarioKind ScenarioKind { get; init; }
    public string ScenarioTitle { get; init; } = string.Empty;
    public string InspirationLine { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public int FixedTick { get; init; }
    public int PresentationFrame { get; init; }
    public string StatusLine { get; init; } = string.Empty;
    public string SelectedActor { get; init; } = string.Empty;
    public float BreakGauge { get; init; }
    public int UiRevision { get; init; }
    public TimeFlowShowcaseTimeFlowSnapshot TimeFlow { get; init; } = new();
    public IReadOnlyList<TimeFlowShowcaseActorSnapshot> Actors { get; init; } = Array.Empty<TimeFlowShowcaseActorSnapshot>();
    public IReadOnlyList<string> RecentEvents { get; init; } = Array.Empty<string>();
}

public sealed class TimeFlowShowcaseActorSnapshot
{
    public string Name { get; init; } = string.Empty;
    public int Team { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Health { get; init; }
    public float Charge { get; init; }
    public float Energy { get; init; }
    public int WaitTicks { get; init; }
    public int OrdersQueued { get; init; }
}

public sealed class TimeFlowShowcaseTimeFlowSnapshot
{
    public string ActiveProfileId { get; init; } = "(baseline)";
    public string ActiveOwner { get; init; } = "(baseline)";
    public int ActiveRequestCount { get; init; }
    public float GlobalTimeScale { get; init; }
    public string LoopMode { get; init; } = "Realtime";
    public string GasMode { get; init; } = "Auto";
    public int GasStepEveryFixedTicks { get; init; }
    public int SimulationScalePermille { get; init; }
    public int GasScalePermille { get; init; }
    public int PhysicsScalePermille { get; init; }
    public int NavigationScalePermille { get; init; }
    public int TasksScalePermille { get; init; }
    public int PhysicsTargetHz { get; init; }
    public int PhysicsMaxStepsPerFixedTick { get; init; }
    public int NavigationTargetHz { get; init; }
    public int NavigationMaxStepsPerFixedTick { get; init; }
}
