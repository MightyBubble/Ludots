using System;
using System.Collections.Generic;
using Ludots.Core.Map;

namespace UiTestMod.Maps
{
    public class UiTestMap : MapDefinition
    {
        public override MapId Id => new MapId("ui_test");
        public override IReadOnlyList<MapTag> Tags => new[] { new MapTag("UI_Test") };
    }
}
