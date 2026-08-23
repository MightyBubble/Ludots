using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.Gameplay.AI.Fsm;

namespace Ludots.Core.Gameplay.AI.Config
{
    public sealed class GraphBehaviorCatalog
    {
        private readonly Dictionary<string, BehaviorTreeDefinition> _trees = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HfsmDefinition> _hfsms = new(StringComparer.Ordinal);

        public int TreeCount => _trees.Count;
        public int HfsmCount => _hfsms.Count;

        public void RegisterTree(BehaviorTreeDefinition tree)
        {
            ArgumentNullException.ThrowIfNull(tree);
            if (!_trees.TryAdd(tree.Id, tree))
            {
                throw new InvalidOperationException($"Behavior tree '{tree.Id}' is already registered.");
            }
        }

        public void RegisterHfsm(HfsmDefinition hfsm)
        {
            ArgumentNullException.ThrowIfNull(hfsm);
            if (!_hfsms.TryAdd(hfsm.Id, hfsm))
            {
                throw new InvalidOperationException($"HFSM '{hfsm.Id}' is already registered.");
            }
        }

        public bool TryGetTree(string id, out BehaviorTreeDefinition tree)
            => _trees.TryGetValue(id, out tree!);

        public bool TryGetHfsm(string id, out HfsmDefinition hfsm)
            => _hfsms.TryGetValue(id, out hfsm!);

        public BehaviorTreeDefinition RequireTree(string id)
        {
            if (!TryGetTree(id, out BehaviorTreeDefinition tree))
            {
                throw new InvalidOperationException($"Behavior tree '{id}' is not registered.");
            }

            return tree;
        }

        public HfsmDefinition RequireHfsm(string id)
        {
            if (!TryGetHfsm(id, out HfsmDefinition hfsm))
            {
                throw new InvalidOperationException($"HFSM '{id}' is not registered.");
            }

            return hfsm;
        }
    }
}
