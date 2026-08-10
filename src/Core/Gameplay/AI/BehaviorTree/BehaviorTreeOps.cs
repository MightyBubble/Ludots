namespace Ludots.Core.Gameplay.AI.BehaviorTree
{
    public enum BehaviorTreeNodeKind : byte
    {
        None = 0,
        Sequence = 1,
        Selector = 2,
        Condition = 3,
        Action = 4
    }

    public enum BehaviorTreeStatus : byte
    {
        Inactive = 0,
        Running = 1,
        Success = 2,
        Failure = 3
    }

    public enum BehaviorTreeLeafBinding : byte
    {
        None = 0,
        /// <summary>Always Success (for topology pressure tests).</summary>
        AlwaysSuccess = 1,
        /// <summary>Always Failure.</summary>
        AlwaysFailure = 2,
        /// <summary>Stay Running until cleared by host (simulates long action without Script).</summary>
        HoldRunning = 3,
        /// <summary>Run Script program via ExecuteSlice; Yield => Running.</summary>
        ScriptSlice = 4
    }

    /// <summary>Compiled flat node. Children are a contiguous range [ChildStart, ChildStart+ChildCount).</summary>
    public readonly struct BehaviorTreeNode
    {
        public BehaviorTreeNode(
            BehaviorTreeNodeKind kind,
            int childStart,
            int childCount,
            BehaviorTreeLeafBinding leaf,
            int graphId)
        {
            Kind = kind;
            ChildStart = childStart;
            ChildCount = childCount;
            Leaf = leaf;
            GraphId = graphId;
        }

        public BehaviorTreeNodeKind Kind { get; }
        public int ChildStart { get; }
        public int ChildCount { get; }
        public BehaviorTreeLeafBinding Leaf { get; }
        public int GraphId { get; }
    }
}
