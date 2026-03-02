using System;
using System.Collections.Generic;
using Ludots.Core.Map.Board;

namespace Ludots.Core.Map
{
    public abstract class MapDefinition
    {
        public virtual MapId Id => new MapId(GetType().Name);
        public abstract IReadOnlyList<MapTag> Tags { get; }
        public virtual string DataFilePath => $"Maps/{Id}.json";
        public virtual IReadOnlyList<string> Dependencies => Array.Empty<string>();
        public virtual IReadOnlyList<BoardConfig> Boards => Array.Empty<BoardConfig>();
        public virtual IReadOnlyList<Type> TriggerTypes => Array.Empty<Type>();
    }
}
