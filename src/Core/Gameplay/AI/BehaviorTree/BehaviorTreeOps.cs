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
    /// Contract: I[0] is the primary condition input cell for sensor HaltReturnInt scripts.
    /// </summary>
    public interface IBehaviorTreeSensorFeed
    {
        void WriteSensors(int agentIndex, int graphId, System.Span<int> ints, System.Span<byte> bools);
    }

    /// <summary>Authored graph keys (GAS/graphs.json) for patrol/chase/attack leaves.</summary>
    public static class BehaviorTreeScriptKeys
    {
        public const string SeeEnemy = "Graph.BT.Leaf.SeeEnemy";
        public const string InAttackRange = "Graph.BT.Leaf.InAttackRange";
        public const string Chase = "Graph.BT.Leaf.Chase";
        public const string Attack = "Graph.BT.Leaf.Attack";
        public const string Patrol = "Graph.BT.Leaf.Patrol";
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
