using System;

namespace Ludots.Core.Gameplay.AI.BehaviorTree
{
    public static class BehaviorTreeFactory
    {
        public static BehaviorTreeDefinition CreateAlwaysSuccessSequence(string id, int leafCount)
        {
            if (leafCount <= 0) throw new ArgumentOutOfRangeException(nameof(leafCount));
            int total = leafCount + 1;
            if (total > BehaviorTreeLimits.MaxNodesPerTree)
            {
                throw new ArgumentOutOfRangeException(nameof(leafCount));
            }

            var nodes = new BehaviorTreeNode[total];
            nodes[0] = new BehaviorTreeNode(BehaviorTreeNodeKind.Sequence, childStart: 1, childCount: leafCount, BehaviorTreeLeafBinding.None, 0);
            for (int i = 0; i < leafCount; i++)
            {
                nodes[1 + i] = new BehaviorTreeNode(
                    BehaviorTreeNodeKind.Action,
                    childStart: 0,
                    childCount: 0,
                    BehaviorTreeLeafBinding.AlwaysSuccess,
                    graphId: 0);
            }

            return new BehaviorTreeDefinition(id, nodes, rootIndex: 0);
        }

        public static BehaviorTreeDefinition CreateHoldRunningRoot(string id)
        {
            var nodes = new BehaviorTreeNode[]
            {
                new(BehaviorTreeNodeKind.Action, 0, 0, BehaviorTreeLeafBinding.HoldRunning, 0),
            };
            return new BehaviorTreeDefinition(id, nodes, rootIndex: 0);
        }

        public static BehaviorTreeDefinition CreatePatrolEngageSkeleton(string id)
        {
            var nodes = new BehaviorTreeNode[]
            {
                new(BehaviorTreeNodeKind.Selector, childStart: 1, childCount: 2, BehaviorTreeLeafBinding.None, 0),
                new(BehaviorTreeNodeKind.Sequence, childStart: 3, childCount: 2, BehaviorTreeLeafBinding.None, 0),
                new(BehaviorTreeNodeKind.Action, 0, 0, BehaviorTreeLeafBinding.AlwaysSuccess, 0),
                new(BehaviorTreeNodeKind.Condition, 0, 0, BehaviorTreeLeafBinding.AlwaysFailure, 0),
                new(BehaviorTreeNodeKind.Action, 0, 0, BehaviorTreeLeafBinding.HoldRunning, 0),
            };
            return new BehaviorTreeDefinition(id, nodes, rootIndex: 0);
        }

        /// <summary>
        /// Classic AI: Selector( Sequence(SeeEnemy Script, Selector(AttackIfInRange, Chase)), Patrol ).
        /// Leaves are ScriptSlice keyed by <see cref="BehaviorTreeScriptBindings"/>.
        /// </summary>
        public static BehaviorTreeDefinition CreatePatrolChaseAttackTree(string id)
        {
            var nodes = new BehaviorTreeNode[]
            {
                new(BehaviorTreeNodeKind.Selector, childStart: 1, childCount: 2, BehaviorTreeLeafBinding.None, 0),
                new(BehaviorTreeNodeKind.Sequence, childStart: 3, childCount: 2, BehaviorTreeLeafBinding.None, 0),
                new(BehaviorTreeNodeKind.Action, 0, 0, BehaviorTreeLeafBinding.ScriptSlice, BehaviorTreeScriptBindings.Patrol),
                new(BehaviorTreeNodeKind.Condition, 0, 0, BehaviorTreeLeafBinding.ScriptSlice, BehaviorTreeScriptBindings.SeeEnemy),
                new(BehaviorTreeNodeKind.Selector, childStart: 5, childCount: 2, BehaviorTreeLeafBinding.None, 0),
                new(BehaviorTreeNodeKind.Sequence, childStart: 7, childCount: 2, BehaviorTreeLeafBinding.None, 0),
                new(BehaviorTreeNodeKind.Action, 0, 0, BehaviorTreeLeafBinding.ScriptSlice, BehaviorTreeScriptBindings.Chase),
                new(BehaviorTreeNodeKind.Condition, 0, 0, BehaviorTreeLeafBinding.ScriptSlice, BehaviorTreeScriptBindings.InAttackRange),
                new(BehaviorTreeNodeKind.Action, 0, 0, BehaviorTreeLeafBinding.ScriptSlice, BehaviorTreeScriptBindings.Attack),
            };
            return new BehaviorTreeDefinition(id, nodes, rootIndex: 0);
        }
    }
}
