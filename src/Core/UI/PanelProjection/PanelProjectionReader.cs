using System;
using Arch.Core;
using System.Collections.Generic;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Pin reader: graph pins read materialized graph outputs and data pins read
    /// validated immutable configuration records. Source lookup failures are reported
    /// with the pin and data path; only graph pins retain their declared default contract.
    /// </summary>
    public sealed class PanelProjectionReader
    {
        private readonly World _world;
        private readonly Dictionary<PanelPinSourceKind, IPanelProjectionSource> _sources;

        public PanelProjectionReader(World world, GraphOutputValueStore values)
            : this(world, new GraphPanelProjectionSource(values))
        {
        }

        public PanelProjectionReader(World world, GraphOutputValueStore values, Ludots.Core.Config.DataSchemaRegistry dataRegistry)
            : this(world, new GraphPanelProjectionSource(values), new DataSchemaPanelProjectionSource(dataRegistry))
        {
        }

        public PanelProjectionReader(World world, params IPanelProjectionSource[] sources)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (sources == null || sources.Length == 0)
            {
                throw new ArgumentException("At least one panel projection source is required.", nameof(sources));
            }

            _sources = new Dictionary<PanelPinSourceKind, IPanelProjectionSource>();
            for (int i = 0; i < sources.Length; i++)
            {
                IPanelProjectionSource source = sources[i] ?? throw new ArgumentException("Panel projection source cannot be null.", nameof(sources));
                if (!_sources.TryAdd(source.Kind, source))
                {
                    throw new ArgumentException($"Panel projection source kind '{source.Kind}' is registered more than once.", nameof(sources));
                }
            }
        }

        public bool IsOwnerLive(Entity owner)
        {
            return owner != Entity.Null && _world.IsAlive(owner);
        }

        public PanelProjectionValue Resolve(Entity owner, PanelPin pin)
        {
            ArgumentNullException.ThrowIfNull(pin);
            if (!_sources.TryGetValue(pin.SourceKind, out IPanelProjectionSource? source))
            {
                throw new InvalidOperationException(
                    $"Panel pin '{pin.Name}' requires projection source '{pin.SourceKind}', but it is not registered.");
            }

            if (source.TryResolve(owner, pin, out PanelProjectionValue value))
            {
                return value;
            }

            throw new InvalidOperationException(
                $"Panel pin '{pin.Name}' data path '{pin.Path}' was not found in record '{pin.RecordId}'.");
        }
    }
}
