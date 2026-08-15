using System;

namespace Ludots.Core.Gameplay.AI.BehaviorTree
{
    public sealed class BehaviorTreeDefinition
    {
        public BehaviorTreeDefinition(string id, BehaviorTreeNode[] nodes, int rootIndex)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Behavior tree id is required.", nameof(id));
            if (nodes == null || nodes.Length == 0) throw new ArgumentException("Behavior tree requires nodes.", nameof(nodes));
            if ((uint)rootIndex >= (uint)nodes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(rootIndex));
            }

            Id = id;
            Nodes = nodes;
            RootIndex = rootIndex;
            AlwaysSuccessSequenceNodeVisitsPerAgent = DetectAlwaysSuccessSequence(nodes, rootIndex);
        }

        public string Id { get; }
        public BehaviorTreeNode[] Nodes { get; }
        public int RootIndex { get; }
        public int NodeCount => Nodes.Length;
        internal int AlwaysSuccessSequenceNodeVisitsPerAgent { get; }

        private static int DetectAlwaysSuccessSequence(BehaviorTreeNode[] nodes, int rootIndex)
        {
            BehaviorTreeNode root = nodes[rootIndex];
            if (root.Kind != BehaviorTreeNodeKind.Sequence ||
                root.Leaf != BehaviorTreeLeafBinding.None ||
                root.ChildCount <= 0)
            {
                return 0;
            }

            int end = root.ChildStart + root.ChildCount;
            if ((uint)root.ChildStart >= (uint)nodes.Length || end > nodes.Length)
            {
                return 0;
            }

            for (int i = root.ChildStart; i < end; i++)
            {
                BehaviorTreeNode child = nodes[i];
                if (child.Kind is not (BehaviorTreeNodeKind.Condition or BehaviorTreeNodeKind.Action) ||
                    child.ChildCount != 0 ||
                    child.Leaf != BehaviorTreeLeafBinding.AlwaysSuccess)
                {
                    return 0;
                }
            }

            return root.ChildCount + 1;
        }
    }

    public static class BehaviorTreeLimits
    {
        public const int MaxNodesPerTree = 64;
        public const int MaxStackDepth = 16;
        public const int DefaultThinkPeriodTicks = 12; // 0.2s at 60Hz
        public const int DefaultScriptBudgetSteps = 32;
    }
}
