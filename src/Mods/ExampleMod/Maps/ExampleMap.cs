using System;
using System.Collections.Generic;
using Ludots.Core.Map;

namespace ExampleMod.Maps
{
    public class ExampleMap : MapDefinition
    {
        public override MapId Id => new MapId("example_map");
        public override IReadOnlyList<MapTag> Tags => new[] { new MapTag("Example") };
    }
}
