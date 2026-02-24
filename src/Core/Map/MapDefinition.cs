using System;
using System.Collections.Generic;

namespace Ludots.Core.Map
{
    public abstract class MapDefinition
    {
        /// <summary>
        /// Unique Identifier for the map. Defaults to Type Name.
        /// </summary>
        public virtual MapId Id => new MapId(GetType().Name);
        
        /// <summary>
        /// Strongly typed tags for this map.
        /// </summary>
        public abstract IReadOnlyList<MapTag> Tags { get; }
        
        /// <summary>
        /// Path to the data file (JSON) relative to assets.
        /// </summary>
        public virtual string DataFilePath => $"Maps/{Id}.json";
        
        /// <summary>
        /// Optional list of mod dependencies.
        /// </summary>
        public virtual IReadOnlyList<string> Dependencies => Array.Empty<string>();

        /// <summary>
        /// Width of the map in chunks (e.g., 64).
        /// Default is 64.
        /// </summary>
        public virtual int WidthInChunks => 64;

        /// <summary>
        /// Height of the map in chunks (e.g., 64).
        /// Default is 64.
        /// </summary>
        public virtual int HeightInChunks => 64;
    }
}
