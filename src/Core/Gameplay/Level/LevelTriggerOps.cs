namespace Ludots.Core.Gameplay.Level
{
    public enum LevelTriggerKind : byte
    {
        ManualPulse = 0,
        ElapsedThinkWaves = 1,
        CounterReached = 2
    }

    public enum LevelActionKind : byte
    {
        None = 0,
        IncrementCounter = 1,
        SetPhase = 2,
        /// <summary>Records fire for tests/showcases; host may map to Spawn.</summary>
        EmitSignal = 3,
        /// <summary>Run L1 Script graph id in Arg0 via <see cref="ILevelGraphHost"/>.</summary>
        RunScript = 4
    }

    public interface ILevelGraphHost
    {
        void RunScript(int scriptGraphId);
    }

    public readonly struct LevelTriggerDef
    {
        public LevelTriggerDef(LevelTriggerKind kind, int threshold, int actionIndex)
        {
            Kind = kind;
            Threshold = threshold;
            ActionIndex = actionIndex;
        }

        public LevelTriggerKind Kind { get; }
        public int Threshold { get; }
        public int ActionIndex { get; }
    }

    public readonly struct LevelActionDef
    {
        public LevelActionDef(LevelActionKind kind, int arg0, int arg1)
        {
            Kind = kind;
            Arg0 = arg0;
            Arg1 = arg1;
        }

        public LevelActionKind Kind { get; }
        public int Arg0 { get; }
        public int Arg1 { get; }
    }

    public static class LevelDirectorLimits
    {
        public const int MaxTriggers = 128;
        public const int MaxActions = 128;
    }
}
