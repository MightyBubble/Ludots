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
        }

        public string Id { get; }
        public BehaviorTreeNode[] Nodes { get; }
        public int RootIndex { get; }
        public int NodeCount => Nodes.Length;
    }

    public static class BehaviorTreeLimits
    {
        public const int MaxNodesPerTree = 64;
        public const int MaxStackDepth = 16;
        public const int DefaultThinkPeriodTicks = 12; // 0.2s at 60Hz
        public const int DefaultScriptBudgetSteps = 32;
    }
}
