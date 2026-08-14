using System;
using System.Collections.Generic;

namespace Ludots.Core.Engine
{
    public static class SystemGroupOrder
    {
        public static IReadOnlyList<SystemGroup> All { get; } = Enum.GetValues<SystemGroup>();
    }
}
