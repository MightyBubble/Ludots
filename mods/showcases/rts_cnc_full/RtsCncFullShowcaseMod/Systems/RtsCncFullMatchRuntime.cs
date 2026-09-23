namespace RtsCncFullShowcaseMod.Systems;

public enum RtsCncFullMatchPhase
{
    AwaitingInput,
    Mining,
    ReadyToTrain,
    Training,
    ReadyToAttack,
    Combat,
    Victory,
}

public enum RtsCncFullMatchAction
{
    StartHarvest,
    TrainArmy,
    AttackEnemy,
    Reset,
}

public sealed class RtsCncFullMatchRuntime
{
    private readonly Queue<RtsCncFullQueuedAction> _pendingActions = new();
    private readonly List<string> _log = new(16);

    public RtsCncFullMatchRuntime()
    {
        Instruction = "Click Start Mining to begin the recorded C&C loop.";
        LastInput = "none";
        LastEvent = "match ready";
        SetEconomy(400f, 0f, 0f);
        SetCounts(0, 0, 0, 0);
    }

    public int Revision { get; private set; }
    public RtsCncFullMatchPhase Phase { get; private set; } = RtsCncFullMatchPhase.AwaitingInput;
    public float PhaseProgress { get; private set; }
    public float Credits { get; private set; }
    public float Ore { get; private set; }
    public float HarvestRate { get; private set; }
    public int HarvestLoads { get; private set; }
    public int UnitsTrained { get; private set; }
    public int PlayerArmyAlive { get; private set; }
    public int EnemyUnitsAlive { get; private set; }
    public int EnemyStructuresAlive { get; private set; }
    public int EnemyDestroyed { get; private set; }
    public bool Victory { get; private set; }
    public string Instruction { get; private set; }
    public string LastInput { get; private set; }
    public string LastEvent { get; private set; }

    public bool TryQueueAction(RtsCncFullMatchAction action, string source, out string message)
    {
        if (action == RtsCncFullMatchAction.Reset)
        {
            _pendingActions.Enqueue(new RtsCncFullQueuedAction(action, source));
            RecordInput(source, action, accepted: true, "reset queued");
            message = "reset queued";
            return true;
        }

        if (!CanAccept(action))
        {
            message = $"Cannot {DisplayAction(action)} while phase is {PhaseLabel}.";
            RecordInput(source, action, accepted: false, message);
            return false;
        }

        _pendingActions.Enqueue(new RtsCncFullQueuedAction(action, source));
        message = $"{DisplayAction(action)} queued";
        RecordInput(source, action, accepted: true, message);
        return true;
    }

    internal bool TryDequeueAction(out RtsCncFullQueuedAction action)
    {
        if (_pendingActions.Count == 0)
        {
            action = default;
            return false;
        }

        action = _pendingActions.Dequeue();
        return true;
    }

    internal void Reset()
    {
        _pendingActions.Clear();
        Phase = RtsCncFullMatchPhase.AwaitingInput;
        PhaseProgress = 0f;
        HarvestLoads = 0;
        UnitsTrained = 0;
        EnemyDestroyed = 0;
        Victory = false;
        Instruction = "Click Start Mining to begin the recorded C&C loop.";
        LastEvent = "match reset";
        SetEconomy(400f, 0f, 0f);
        SetCounts(PlayerArmyAlive, EnemyUnitsAlive, EnemyStructuresAlive, EnemyDestroyed);
        Record("Reset to opening state.");
    }

    internal void EnterPhase(RtsCncFullMatchPhase phase, string instruction, string evt)
    {
        Phase = phase;
        PhaseProgress = 0f;
        Instruction = instruction;
        LastEvent = evt;
        if (phase == RtsCncFullMatchPhase.Victory)
        {
            Victory = true;
        }

        Record(evt);
    }

    internal void SetPhaseProgress(float progress)
    {
        float clamped = Math.Clamp(progress, 0f, 1f);
        if (MathF.Abs(PhaseProgress - clamped) > 0.005f)
        {
            PhaseProgress = clamped;
            Revision++;
        }
    }

    internal void SetEconomy(float credits, float ore, float harvestRate)
    {
        Credits = MathF.Round(credits, 1);
        Ore = MathF.Round(ore, 1);
        HarvestRate = MathF.Round(harvestRate, 1);
        Revision++;
    }

    internal void SetHarvestLoads(int harvestLoads)
    {
        if (HarvestLoads == harvestLoads)
        {
            return;
        }

        HarvestLoads = harvestLoads;
        Revision++;
    }

    internal void SetUnitsTrained(int unitsTrained)
    {
        if (UnitsTrained == unitsTrained)
        {
            return;
        }

        UnitsTrained = unitsTrained;
        Revision++;
    }

    internal void SetCounts(int playerArmyAlive, int enemyUnitsAlive, int enemyStructuresAlive, int enemyDestroyed)
    {
        PlayerArmyAlive = playerArmyAlive;
        EnemyUnitsAlive = enemyUnitsAlive;
        EnemyStructuresAlive = enemyStructuresAlive;
        EnemyDestroyed = enemyDestroyed;
        Revision++;
    }

    internal void Record(string message)
    {
        LastEvent = message;
        _log.Add($"{DateTime.UtcNow:HH:mm:ss}Z {message}");
        while (_log.Count > 8)
        {
            _log.RemoveAt(0);
        }

        Revision++;
    }

    public RtsCncFullMatchView ToView()
    {
        return new RtsCncFullMatchView(
            Phase.ToString(),
            PhaseLabel,
            PhaseProgress,
            Credits,
            Ore,
            HarvestRate,
            HarvestLoads,
            UnitsTrained,
            PlayerArmyAlive,
            EnemyUnitsAlive,
            EnemyStructuresAlive,
            EnemyDestroyed,
            Victory,
            Instruction,
            LastInput,
            LastEvent,
            _log.ToArray());
    }

    private bool CanAccept(RtsCncFullMatchAction action)
    {
        return action switch
        {
            RtsCncFullMatchAction.StartHarvest => Phase == RtsCncFullMatchPhase.AwaitingInput,
            RtsCncFullMatchAction.TrainArmy => Phase == RtsCncFullMatchPhase.ReadyToTrain,
            RtsCncFullMatchAction.AttackEnemy => Phase == RtsCncFullMatchPhase.ReadyToAttack,
            _ => false,
        };
    }

    private void RecordInput(string source, RtsCncFullMatchAction action, bool accepted, string message)
    {
        LastInput = $"{source}: {DisplayAction(action)} {(accepted ? "accepted" : "rejected")}";
        Record(message);
    }

    private string PhaseLabel => Phase switch
    {
        RtsCncFullMatchPhase.AwaitingInput => "Opening",
        RtsCncFullMatchPhase.Mining => "Harvesting Ore",
        RtsCncFullMatchPhase.ReadyToTrain => "Ore Banked",
        RtsCncFullMatchPhase.Training => "Training Army",
        RtsCncFullMatchPhase.ReadyToAttack => "Army Ready",
        RtsCncFullMatchPhase.Combat => "Assaulting Volkov",
        RtsCncFullMatchPhase.Victory => "Victory",
        _ => Phase.ToString(),
    };

    private static string DisplayAction(RtsCncFullMatchAction action)
    {
        return action switch
        {
            RtsCncFullMatchAction.StartHarvest => "Start Mining",
            RtsCncFullMatchAction.TrainArmy => "Train Army",
            RtsCncFullMatchAction.AttackEnemy => "Attack Enemy",
            RtsCncFullMatchAction.Reset => "Reset",
            _ => action.ToString(),
        };
    }
}

internal readonly record struct RtsCncFullQueuedAction(RtsCncFullMatchAction Action, string Source);

public sealed record RtsCncFullMatchView(
    string Phase,
    string PhaseLabel,
    float PhaseProgress,
    float Credits,
    float Ore,
    float HarvestRate,
    int HarvestLoads,
    int UnitsTrained,
    int PlayerArmyAlive,
    int EnemyUnitsAlive,
    int EnemyStructuresAlive,
    int EnemyDestroyed,
    bool Victory,
    string Instruction,
    string LastInput,
    string LastEvent,
    string[] Log);
