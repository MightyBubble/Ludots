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
        AlwaysSuccess = 1,
        AlwaysFailure = 2,
        HoldRunning = 3,
        /// <summary>Run Script by <see cref="BehaviorTreeNode.GraphId"/> via ExecuteSlice.</summary>
        ScriptSlice = 4
    }

    /// <summary>
    /// Writes per-agent sensor registers before a ScriptSlice leaf runs.
    /// Contract: I[0] is the primary condition/action input cell for patrol scripts.
    /// </summary>
    public interface IBehaviorTreeSensorFeed
    {
        void WriteSensors(int agentIndex, int graphId, System.Span<int> ints, System.Span<byte> bools);
    }

    /// <summary>Well-known Script graph ids for <see cref="BehaviorTreeFactory.CreatePatrolChaseAttackTree"/>.</summary>
    public static class BehaviorTreeScriptBindings
    {
        public const int SeeEnemy = 1;
        public const int InAttackRange = 2;
        public const int Chase = 3;
        public const int Attack = 4;
        public const int Patrol = 5;
    }

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
