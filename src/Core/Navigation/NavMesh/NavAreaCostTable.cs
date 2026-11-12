using System;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Navigation.NavMesh
{
    public sealed class NavAreaCostTable
    {
        private readonly Fix64[] _costs;
        private readonly bool[] _impassable;
        public Fix64 MinCost { get; }

        public NavAreaCostTable(Fix64[] costs)
            : this(costs, impassable: null)
        {
        }

        public NavAreaCostTable(Fix64[] costs, bool[] impassable)
        {
            if (costs == null) throw new ArgumentNullException(nameof(costs));
            if (costs.Length != 256) throw new ArgumentException("Cost table must be length 256.", nameof(costs));
            _costs = costs;

            if (impassable != null)
            {
                if (impassable.Length != 256) throw new ArgumentException("Impassable mask must be length 256.", nameof(impassable));
                for (int i = 0; i < impassable.Length; i++)
                {
                    if (impassable[i])
                    {
                        _impassable = (bool[])impassable.Clone();
                        break;
                    }
                }
            }

            Fix64 min = Fix64.MaxValue;
            bool anyPassable = false;
            for (int i = 0; i < _costs.Length; i++)
            {
                var c = _costs[i];
                if (c <= Fix64.Zero) throw new ArgumentException("Cost must be > 0.", nameof(costs));
                if (_impassable != null && _impassable[i]) continue;
                anyPassable = true;
                if (c < min) min = c;
            }

            MinCost = anyPassable ? min : Fix64.OneValue;
        }

        public Fix64 Get(byte areaId) => _costs[areaId];

        public bool IsPassable(byte areaId) => _impassable == null || !_impassable[areaId];

        public static NavAreaCostTable CreateDefault()
        {
            var arr = new Fix64[256];
            for (int i = 0; i < arr.Length; i++) arr[i] = Fix64.OneValue;
            return new NavAreaCostTable(arr);
        }
    }
}
