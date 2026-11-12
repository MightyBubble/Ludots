using System;
using Ludots.Core.Vision;

namespace Ludots.Core.CrowdSimulation.Fog;

public static class CrowdFogLayerBinding
{
    public const string LayerKey = "crowd-fog";

    public static FogLayerId Require(FogLayerRegistry layers, int fogCellSizeCm, int updateHz)
    {
        if (layers == null)
        {
            throw new ArgumentNullException(nameof(layers));
        }

        if (fogCellSizeCm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fogCellSizeCm));
        }

        FogLayerId existing = layers.GetId(LayerKey);
        if (existing.Value > 0)
        {
            int layerCellSizeCm = layers.Get(existing).CellSizeCm;
            if (layerCellSizeCm != fogCellSizeCm)
            {
                throw new InvalidOperationException(
                    $"迷雾层 {LayerKey} 的 cellSizeCm 是 {layerCellSizeCm}，群体 FogCellSizeCm 是 {fogCellSizeCm}。");
            }

            layers.SetExternalStamp(existing, true);
            return existing;
        }

        FogLayerId created = layers.Register(LayerKey, fogCellSizeCm, updateHz);
        layers.SetExternalStamp(created, true);
        return created;
    }
}
