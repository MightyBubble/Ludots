using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public enum AbilityTaskStepType : byte
    {
        None = 0,
        DelayTicks = 1,
        WaitEvent = 2,
        WaitInputResponse = 3,
        WaitSelectionResponse = 4,
        SendEvent = 5,
        CommitOnActivateEffects = 6,
        AddTag = 7,
        RemoveTag = 8,
        AddTagToTarget = 9,
        RemoveTagFromTarget = 10,
        End = 255
    }

    public struct AbilityTaskStep
    {
        public AbilityTaskStepType Type;
        public GasClockId ClockId;
        public int TagId;
        public int Ticks;
        public int PayloadA;
        public int PayloadB;
    }

    public unsafe struct AbilityTaskSpec
    {
        public GasClockId ClockId;
        public GameplayTagContainer InterruptAny;
        public int StepCount;
        public fixed byte StepTypes[8];
        public fixed byte StepClockIds[8];
        public fixed int StepTagIds[8];
        public fixed int StepTicks[8];
        public fixed int StepPayloadAs[8];
        public fixed int StepPayloadBs[8];

        public AbilityTaskStep GetStep(int index)
        {
            AbilityTaskStep s = default;
            fixed (byte* types = StepTypes) s.Type = (AbilityTaskStepType)types[index];
            fixed (byte* clockIds = StepClockIds) s.ClockId = (GasClockId)clockIds[index];
            fixed (int* tags = StepTagIds) s.TagId = tags[index];
            fixed (int* ticks = StepTicks) s.Ticks = ticks[index];
            fixed (int* a = StepPayloadAs) s.PayloadA = a[index];
            fixed (int* b = StepPayloadBs) s.PayloadB = b[index];
            return s;
        }

        public void SetStep(int index, in AbilityTaskStep step)
        {
            fixed (byte* types = StepTypes) types[index] = (byte)step.Type;
            fixed (byte* clockIds = StepClockIds) clockIds[index] = (byte)step.ClockId;
            fixed (int* tags = StepTagIds) tags[index] = step.TagId;
            fixed (int* ticks = StepTicks) ticks[index] = step.Ticks;
            fixed (int* a = StepPayloadAs) a[index] = step.PayloadA;
            fixed (int* b = StepPayloadBs) b[index] = step.PayloadB;
            if (index + 1 > StepCount) StepCount = index + 1;
        }
    }

    public enum AbilityTaskRunState : byte
    {
        Running = 0,
        Waiting = 1,
        Committed = 2,
        Finished = 3,
        Interrupted = 4
    }

    public unsafe struct AbilityTaskInstance
    {
        public int OrderId;
        public int AbilitySlot;
        public Entity Target;
        public Entity TargetContext;
        public int MultiTargetCount;
        public fixed int MultiTargetIds[64];
        public fixed int MultiTargetWorldIds[64];
        public fixed int MultiTargetVersions[64];
        public AbilityTaskRunState State;
        public int StepIndex;
        public int Deadline;
        public int WaitTagId;
        public int WaitRequestId;
        public GasClockId WaitClockId;
    }
}
