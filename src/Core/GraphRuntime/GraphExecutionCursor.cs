namespace Ludots.Core.GraphRuntime
{
    public enum GraphExecutionStatus : byte
    {
        NotStarted = 0,
        Yielded = 1,
        Halted = 2,
        BudgetSuspended = 3,
        Running = 4
    }

    public struct GraphExecutionCursor
    {
        public GraphExecutionCursor(int startPc)
        {
            Pc = startPc;
            LastInstructionPc = -1;
            Steps = 0;
            CallStackCount = 0;
            ReturnInt = 0;
            InvokeDepth = 0;
            Status = GraphExecutionStatus.NotStarted;
        }

        public int Pc;
        public int LastInstructionPc;
        public int Steps;
        public int CallStackCount;
        public int ReturnInt;
        public int InvokeDepth;
        public GraphExecutionStatus Status;
        internal Arch.Core.Entity[]? TargetSnapshot;
        internal int TargetCount;

        internal void SaveTargets(System.ReadOnlySpan<Arch.Core.Entity> targets)
        {
            if (TargetSnapshot == null || TargetSnapshot.Length < targets.Length)
                TargetSnapshot = new Arch.Core.Entity[System.Math.Max(256, targets.Length)];
            targets.CopyTo(TargetSnapshot);
            TargetCount = targets.Length;
        }

        public bool IsSuspended =>
            Status is GraphExecutionStatus.Yielded or GraphExecutionStatus.BudgetSuspended;

        public void Reset()
        {
            TargetCount = 0;
            Pc = 0;
            LastInstructionPc = -1;
            Steps = 0;
            CallStackCount = 0;
            ReturnInt = 0;
            InvokeDepth = 0;
            Status = GraphExecutionStatus.NotStarted;
        }
    }

    public readonly struct GraphSliceResult
    {
        public GraphSliceResult(GraphExecutionStatus status, int returnInt, int steps)
        {
            Status = status;
            ReturnInt = returnInt;
            Steps = steps;
        }

        public GraphExecutionStatus Status { get; }
        public bool Halted => Status == GraphExecutionStatus.Halted;
        public bool Yielded => Status == GraphExecutionStatus.Yielded;
        public bool BudgetSuspended => Status == GraphExecutionStatus.BudgetSuspended;
        public bool Running => Status == GraphExecutionStatus.Running;
        public int ReturnInt { get; }
        public int Steps { get; }
    }
}
