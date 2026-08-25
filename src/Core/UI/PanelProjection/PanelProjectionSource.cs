using System;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.UI.PanelProjection
{
    public interface IPanelProjectionSource
    {
        PanelPinSourceKind Kind { get; }

        bool TryResolve(Entity owner, PanelPin pin, out PanelProjectionValue value);
    }

    internal sealed class GraphPanelProjectionSource : IPanelProjectionSource
    {
        private readonly GraphOutputValueStore _values;

        public GraphPanelProjectionSource(GraphOutputValueStore values)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public PanelPinSourceKind Kind => PanelPinSourceKind.Graph;

        public bool TryResolve(Entity owner, PanelPin pin, out PanelProjectionValue value)
        {
            if (_values.TryGet(owner, pin.Key, out GraphOutputValueHandle handle) &&
                _values.TryGetView(handle, out GraphOutputValueView view))
            {
                float numericValue = view.Kind switch
                {
                    GraphOutputValueKind.Int => view.IntValue,
                    GraphOutputValueKind.Bool => view.BoolValue ? 1f : 0f,
                    _ => view.FloatValue,
                };
                value = new PanelProjectionValue(pin.Name, numericValue, view.Revision, fromGraph: true);
                return true;
            }

            value = new PanelProjectionValue(pin.Name, pin.Default, revision: 0, fromGraph: false);
            return true;
        }
    }

    internal sealed class DataSchemaPanelProjectionSource : IPanelProjectionSource
    {
        private readonly DataSchemaRegistry _registry;

        public DataSchemaPanelProjectionSource(DataSchemaRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public PanelPinSourceKind Kind => PanelPinSourceKind.Data;

        public bool TryResolve(Entity owner, PanelPin pin, out PanelProjectionValue value)
        {
            if (pin.RecordId == null || pin.Path == null ||
                !_registry.TryGetNode(pin.RecordId, pin.Path, out System.Text.Json.Nodes.JsonNode? node) ||
                node == null)
            {
                value = default;
                return false;
            }

            value = new PanelProjectionValue(pin.Name, node, _registry.Revision);
            return true;
        }
    }
}
