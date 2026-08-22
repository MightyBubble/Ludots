using System;
using Arch.Core;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// v2 pin reader: panel pins read exactly one thing — their graph's output for
    /// the owning scope, materialized in <see cref="GraphOutputValueStore"/> by
    /// GraphReturnWriter. Missing output resolves to the pin default (no error,
    /// no empty); structural errors were rejected at load.
    /// </summary>
    public sealed class PanelProjectionReader
    {
        private readonly World _world;
        private readonly GraphOutputValueStore _values;

        public PanelProjectionReader(World world, GraphOutputValueStore values)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public bool IsOwnerLive(Entity owner)
        {
            return owner != Entity.Null && _world.IsAlive(owner);
        }

        public PanelProjectionValue Resolve(Entity owner, PanelPin pin)
        {
            if (_values.TryGet(owner, pin.Key, out GraphOutputValueHandle handle) &&
                _values.TryGetView(handle, out GraphOutputValueView view))
            {
                return new PanelProjectionValue(pin.Name, view.FloatValue, view.Revision, fromGraph: true);
            }

            return new PanelProjectionValue(pin.Name, pin.Default, revision: 0, fromGraph: false);
        }
    }
}
