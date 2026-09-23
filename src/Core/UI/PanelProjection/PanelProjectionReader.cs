using System;
using Arch.Core;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Pin reader: panel pins read exactly one thing — their graph's output for
    /// the owning scope, materialized in <see cref="GraphOutputValueStore"/> by
    /// GraphReturnWriter. Missing output is an explicit failure (no default
    /// fallback, no empty); structural errors were rejected at load. Values keep
    /// the graph output's actual kind — Int stays Int, Bool stays Bool.
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
            if (pin == null)
            {
                throw new ArgumentNullException(nameof(pin));
            }

            if (!_values.TryGet(owner, pin.Key, out GraphOutputValueHandle handle) ||
                !_values.TryGetView(handle, out GraphOutputValueView view))
            {
                throw new InvalidOperationException(
                    $"Panel pin '{pin.Name}' key '{pin.Key}' has no graph output for owner {owner.Id}; " +
                    "the graph must materialize every declared pin output (no default fallback).");
            }

            PanelValueKind kind = view.Kind switch
            {
                GraphOutputValueKind.Bool => PanelValueKind.Bool,
                GraphOutputValueKind.Int => PanelValueKind.Int,
                GraphOutputValueKind.Float => PanelValueKind.Float,
                GraphOutputValueKind.Entity => PanelValueKind.Entity,
                _ => throw new InvalidOperationException(
                    $"Panel pin '{pin.Name}' key '{pin.Key}' graph output has unsupported kind '{view.Kind}'."),
            };

            return new PanelProjectionValue(
                pin.Name,
                kind,
                view.Revision,
                boolValue: view.BoolValue,
                intValue: view.IntValue,
                floatValue: view.FloatValue,
                entityValue: view.EntityValue);
        }
    }
}
