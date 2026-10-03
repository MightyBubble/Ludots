using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// navmesh.json 的 layers[] 声明到运行时层号的解析。
    ///
    /// 层名是作者词汇，层号是查询与 tile 寻址词汇；两者在此处唯一对应一次，
    /// 之后所有 {layer, profile} 查询都只用层号。
    /// </summary>
    public sealed class NavLayerResolver
    {
        private readonly Dictionary<string, int> _layerByName;
        private readonly Dictionary<int, string> _nameByLayer;

        public NavLayerResolver(IReadOnlyList<NavLayerConfig> layers)
        {
            if (layers == null)
            {
                throw new ArgumentNullException(nameof(layers));
            }

            _layerByName = new Dictionary<string, int>(layers.Count, StringComparer.Ordinal);
            _nameByLayer = new Dictionary<int, string>(layers.Count);

            for (int i = 0; i < layers.Count; i++)
            {
                NavLayerConfig layer = layers[i]
                    ?? throw new InvalidOperationException($"NavMeshBakeConfig.layers[{i}] is null.");
                if (string.IsNullOrWhiteSpace(layer.Id))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.layers[{i}].id is required.");
                }

                if (!_layerByName.TryAdd(layer.Id, layer.Layer))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.layers contains duplicate name '{layer.Id}'.");
                }

                if (!_nameByLayer.TryAdd(layer.Layer, layer.Id))
                {
                    throw new InvalidOperationException(
                        $"NavMeshBakeConfig.layers declares layer {layer.Layer} more than once ('{_nameByLayer[layer.Layer]}' and '{layer.Id}').");
                }
            }
        }

        public int Count => _layerByName.Count;

        public bool TryGetLayer(string layerId, out int layer)
        {
            if (string.IsNullOrWhiteSpace(layerId))
            {
                layer = -1;
                return false;
            }

            return _layerByName.TryGetValue(layerId, out layer);
        }

        public int RequireLayer(string layerId)
        {
            if (!TryGetLayer(layerId, out int layer))
            {
                throw new InvalidOperationException($"NavMesh layer '{layerId}' is not declared by NavMeshBakeConfig.layers.");
            }

            return layer;
        }

        public bool TryGetName(int layer, out string name)
        {
            return _nameByLayer.TryGetValue(layer, out name!);
        }

        public string GetName(int layer)
        {
            if (!_nameByLayer.TryGetValue(layer, out string? name))
            {
                throw new InvalidOperationException($"NavMesh layer index {layer} is not declared by NavMeshBakeConfig.layers.");
            }

            return name;
        }
    }
}
