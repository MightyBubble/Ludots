using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.AI.Tasks
{
    public sealed class TaskNodeRegistry
    {
        private readonly List<ITaskNode> _nodes = new();

        public int Register(ITaskNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            int id = _nodes.Count;
            _nodes.Add(node);
            return id;
        }

        public bool TryGet(int nodeId, out ITaskNode node)
        {
            if ((uint)nodeId >= (uint)_nodes.Count)
            {
                node = null!;
                return false;
            }
            node = _nodes[nodeId];
            return true;
        }
    }
}

