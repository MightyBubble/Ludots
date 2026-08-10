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

        /// <summary>Single Running leaf — keeps agents active for motion demos while still tickable.</summary>
        public static BehaviorTreeDefinition CreateHoldRunningRoot(string id)
        {
            var nodes = new BehaviorTreeNode[]
            {
                new(BehaviorTreeNodeKind.Action, 0, 0, BehaviorTreeLeafBinding.HoldRunning, 0),
            };
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

        /// <summary>
        /// Classic AI: Selector( Sequence(SeeEnemy, Selector(AttackIfInRange, Chase)), Patrol ).
        /// Leaves require <see cref="IBehaviorTreeLeafHost"/> with <see cref="BehaviorTreeHostBindings"/>.
        /// </summary>
        public static BehaviorTreeDefinition CreatePatrolChaseAttackTree(string id)
        {
            // 0 Selector → [1 EngageSeq, 2 Patrol]
            // 1 Sequence → [3 SeeEnemy, 4 FightSel]
            // 4 Selector → [5 AttackSeq, 6 Chase]
            // 5 Sequence → [7 InRange, 8 Attack]
            var nodes = new BehaviorTreeNode[]
            {
                new(BehaviorTreeNodeKind.Selector, childStart: 1, childCount: 2, BehaviorTreeLeafBinding.None, 0),
                new(BehaviorTreeNodeKind.Sequence, childStart: 3, childCount: 2, BehaviorTreeLeafBinding.None, 0),
                new(BehaviorTreeNodeKind.Action, 0, 0, BehaviorTreeLeafBinding.HostAction, BehaviorTreeHostBindings.Patrol),
                new(BehaviorTreeNodeKind.Condition, 0, 0, BehaviorTreeLeafBinding.HostCondition, BehaviorTreeHostBindings.SeeEnemy),
                new(BehaviorTreeNodeKind.Selector, childStart: 5, childCount: 2, BehaviorTreeLeafBinding.None, 0),
                new(BehaviorTreeNodeKind.Sequence, childStart: 7, childCount: 2, BehaviorTreeLeafBinding.None, 0),
                new(BehaviorTreeNodeKind.Action, 0, 0, BehaviorTreeLeafBinding.HostAction, BehaviorTreeHostBindings.Chase),
                new(BehaviorTreeNodeKind.Condition, 0, 0, BehaviorTreeLeafBinding.HostCondition, BehaviorTreeHostBindings.InAttackRange),
                new(BehaviorTreeNodeKind.Action, 0, 0, BehaviorTreeLeafBinding.HostAction, BehaviorTreeHostBindings.Attack),
            };
            return new BehaviorTreeDefinition(id, nodes, rootIndex: 0);
        }
    }
}
