namespace Ludots.Core.GraphRuntime
{
    public enum GraphVmOpcode : ushort
    {
        Nop = 0,
        ConstInt = 1,
        AddInt = 2,
        LessThanInt = 3,
        Jump = 4,
        JumpIfFalse = 5,
        ReturnInt = 6,
        BranchBool = 7,
        LoadInt = 8,
        StoreInt = 9,
        Call = 10,
        Return = 11,
        Yield = 12
    }

    public enum GraphVmValueType : byte
    {
        Void = 0,
        Bool = 1,
        Int = 2
    }

    public enum GraphVmExecutionStatus : byte
    {
        Running = 0,
        Yielded = 1,
        Halted = 2
    }

    public static class GraphVmRuntimeLimits
    {
        public const int MaxIntRegisters = 32;
        public const int MaxBoolRegisters = 32;
        public const int MaxInstructions = 256;
        public const int MaxInstructionsPerExecution = 1024;

        /// <summary>
        /// Instruction budget for one contiguous execution segment: the steps a
        /// cursor may consume between two <see cref="GraphVmOpcode.Yield"/>
        /// points (including the segment from start to the first Yield and from
        /// the last Yield to Halt). The segment counter is
        /// <see cref="GraphVmExecutionCursor.StepsSinceYield"/>, reset to zero
        /// at every Yield, so long-lived coroutines that yield once per
        /// scheduler slice are never budget-capped across their lifetime. A
        /// segment that reaches the budget without yielding is a non-terminating
        /// loop and fails closed instead of being resumed forever. The value is
        /// 16 per-slice budgets: generous headroom for a legitimate segment that
        /// spans several slices, while a runaway loop is still caught within a
        /// few frames.
        /// </summary>
        public const int MaxInstructionsBetweenYields = MaxInstructionsPerExecution * 16;
        public const int MaxCallStackDepth = 16;
    }

    public static class GraphVmControlPorts
    {
        public const string Enter = "enter";
        public const string Next = "next";
        public const string True = "true";
        public const string False = "false";
        public const string Call = "call";
        public const string Target = "target";
    }

    public static class GraphVmValuePorts
    {
        public const string Value = "value";
        public const string A = "a";
        public const string B = "b";
        public const string Condition = "condition";
    }
}
