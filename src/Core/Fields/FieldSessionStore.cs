using System;
using System.Collections.Generic;
using Ludots.Core.Fields.Config;

namespace Ludots.Core.Fields
{
    /// <summary>
    /// Field layers hosted by one map session: catalog ∩ map enablement, built at
    /// map load and released with the session. Authored cell data (if any) is
    /// applied here, so a store is fully populated the moment it exists.
    /// </summary>
    public sealed class FieldSessionStore
    {
        private readonly Dictionary<FieldLayerId, FieldLayerData> _layers = new();

        public int Count => _layers.Count;

        public IEnumerable<FieldLayerData> Layers => _layers.Values;

        public bool TryGet(FieldLayerId layerId, out FieldLayerData layer)
        {
            return _layers.TryGetValue(layerId, out layer!);
        }

        public bool TryGetByKey(string layerKey, out FieldLayerData layer)
        {
            foreach (FieldLayerData candidate in _layers.Values)
            {
                if (string.Equals(candidate.LayerKey, layerKey, StringComparison.Ordinal))
                {
                    layer = candidate;
                    return true;
                }
            }

            layer = null!;
            return false;
        }

        public TLayer Get<TLayer>(FieldLayerId layerId)
            where TLayer : FieldLayerData
        {
            if (!_layers.TryGetValue(layerId, out FieldLayerData layer))
            {
                throw new InvalidOperationException(
                    $"Field layer id {layerId.Value} is not enabled on this map.");
            }

            if (layer is not TLayer typed)
            {
                throw new InvalidOperationException(
                    $"Field layer '{layer.LayerKey}' is {layer.GetType().Name} but was requested as {typeof(TLayer).Name}.");
            }

            return typed;
        }

        public static FieldSessionStore Create(
            FieldLayerRegistry catalog,
            IReadOnlyList<string>? enabledLayerKeys,
            FieldCellsConfigLoader? cellsLoader = null)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var store = new FieldSessionStore();
            if (enabledLayerKeys == null || enabledLayerKeys.Count == 0)
            {
                return store;
            }

            foreach (string layerKey in enabledLayerKeys)
            {
                FieldLayerId layerId = catalog.GetId(layerKey);
                if (layerId.Value == 0 || !catalog.TryGet(layerId, out FieldLayerDefinition definition))
                {
                    throw new InvalidOperationException(
                        $"Map enables field layer '{layerKey}' which is not declared in Fields/layers.json.");
                }

                FieldLayerData data = CreateLayerData(definition);
                if (data is DiscreteIdFieldLayerData discrete && cellsLoader != null)
                {
                    FieldCellsAsset? asset = cellsLoader.Load(layerKey);
                    if (asset != null)
                    {
                        ApplyAuthoredCells(discrete, asset);
                    }
                }

                store._layers.Add(layerId, data);
            }

            return store;
        }

        private static FieldLayerData CreateLayerData(FieldLayerDefinition definition)
        {
            return definition.Kind switch
            {
                FieldLayerKind.DiscreteId => new DiscreteIdFieldLayerData(definition),
                FieldLayerKind.Scalar32 => new Scalar32FieldLayerData(definition),
                FieldLayerKind.Vector2 => new Vector2FieldLayerData(definition),
                FieldLayerKind.Vector3 => new Vector3FieldLayerData(definition),
                _ => throw new InvalidOperationException(
                    $"Field layer '{definition.Key}' has unsupported kind {definition.Kind}."),
            };
        }

        private static void ApplyAuthoredCells(DiscreteIdFieldLayerData layer, FieldCellsAsset asset)
        {
            foreach (string regionKey in asset.RegionKeys)
            {
                layer.Regions.Register(regionKey);
            }

            foreach (FieldCellRectEntry rect in asset.Rects)
            {
                int regionId = layer.Regions.GetId(rect.RegionKey);
                layer.Field.FillRect(rect.X0, rect.Y0, rect.X1, rect.Y1, regionId);
            }

            foreach (FieldCellRegionEntry cell in asset.Points)
            {
                layer.Field.Set(new FieldCell2D(cell.X, cell.Y), layer.Regions.GetId(cell.RegionKey));
            }
        }
    }
}
