using System;

namespace Ludots.Core.Networking
{
    public sealed class GameplayReplicationEntityIdAllocator
    {
        private int _nextId = 1;

        public int Allocate()
        {
            if (_nextId <= 0)
            {
                throw new InvalidOperationException("Gameplay replication entity id allocator overflowed.");
            }

            return _nextId++;
        }
    }
}
