using System;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Navigation.NavMesh
{
    public sealed class NavAreaCostTable
    {
        private readonly Fix64[] _costs;
        public Fix64 MinCost { get; }

        public NavAreaCostTable(Fix64[] costs)
        {
            if (costs == null) throw new ArgumentNullException(nameof(costs));
            if (costs.Length != 256) throw new ArgumentException("Cost table must be length 256.", nameof(costs));
            _costs = costs;

            Fix64 min = Fix64.MaxValue;
            for (int i = 0; i < _costs.Length; i++)
            {
                var c = _costs[i];
                if (c <= Fix64.Zero) throw new ArgumentException("Cost must be > 0.", nameof(costs));
                if (c < min) min = c;
            }
            MinCost = min;
        }

        public Fix64 Get(byte areaId) => _costs[areaId];

        public static NavAreaCostTable CreateDefault()
        {
            var arr = new Fix64[256];
            for (int i = 0; i < arr.Length; i++) arr[i] = Fix64.OneValue;
            return new NavAreaCostTable(arr);
        }
    }
}
