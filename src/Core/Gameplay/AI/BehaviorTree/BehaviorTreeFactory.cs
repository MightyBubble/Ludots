using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.AI.BehaviorTree
{
    public static class BehaviorTreeFactory
    {
        /// <summary>
        /// Builds a Sequence of <paramref name="leafCount"/> AlwaysSuccess actions under one root.
        /// Used for topology pressure (N_topo = leafCount + 1).
        /// </summary>
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

        /// <summary>
        /// Patrol-shaped: Selector( Sequence(cond fail?, action hold), action success ).
        /// Minimal gameplay-shaped topology for showcases before Script wiring.
        /// </summary>
        public static BehaviorTreeDefinition CreatePatrolEngageSkeleton(string id)
        {
            // 0 Selector children → [1 Sequence, 2 Patrol]
            // 1 Sequence children → [3 SeeEnemy?, 4 Engage]
            // 2 Patrol AlwaysSuccess
            // 3 Condition AlwaysFailure (no enemy)
            // 4 Engage HoldRunning
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
    }
}
