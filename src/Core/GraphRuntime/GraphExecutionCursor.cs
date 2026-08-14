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
        public int Pc;
        public int Steps;
        public int CallStackCount;
        public int ReturnInt;
        public int InvokeDepth;
        public GraphExecutionStatus Status;

        public bool IsSuspended =>
            Status is GraphExecutionStatus.Yielded or GraphExecutionStatus.BudgetSuspended;

        public void Reset()
        {
            Pc = 0;
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
