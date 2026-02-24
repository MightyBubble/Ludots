using System;
using System.Collections.Generic;
using Ludots.Core.Map;

namespace Ludots.Core.Maps
{
    public class Level1Map : MapDefinition
    {
        public override MapId Id => new MapId("level_1");
        public override IReadOnlyList<MapTag> Tags => new[] { MapTags.Gameplay, MapTags.Level };
    }
}
