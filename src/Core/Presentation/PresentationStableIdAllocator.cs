using System;

namespace Ludots.Core.Presentation
{
    public sealed class PresentationStableIdAllocator
    {
        private int _nextId = 1;

        public int Allocate()
        {
            if (_nextId <= 0)
                throw new InvalidOperationException("Presentation stable id allocator overflowed.");

            return _nextId++;
        }
    }
}
