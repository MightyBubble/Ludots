using System;
using System.Numerics;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Fields;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Fields;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 地形调色板加坡度暗部。只在安装路径注入时替换路线视图的地面；
/// 没有高度场的两参演示投影器仍写流场。
/// </summary>
internal sealed class CrowdTerrainPaletteProjector : IGlobalFieldVisualProjector
{
    private readonly NavHeightField _heights;
    private readonly Vector4 _low;
    private readonly Vector4 _high;
    private readonly Vector4 _shade;
    private readonly float _strength;
    private readonly GlobalFieldVisualCell[] _cells;
    private readonly IntRect[] _dirty = new IntRect[1];
    private bool _rangeReady;
    private float _min;
    private float _span;

    public CrowdTerrainPaletteProjector(NavHeightField heights, CrowdPlayerViewConfig config)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(config);
        _heights = heights;
        _low = config.Colors.TerrainLow.ToVector4();
        _high = config.Colors.TerrainHigh.ToVector4();
        _shade = config.Colors.TerrainShade.ToVector4();
        _strength = config.HillshadeStrength;
        int n = heights.NavCellsPerSide * heights.NavCellsPerSide;
        if (n <= 0 || heights.HeightsCm.Length < n || heights.Slope.Length < n)
        {
            throw new InvalidOperationException("地形调色板需要与导航格数一致的高度和坡度。");
        }

        _cells = new GlobalFieldVisualCell[n];
    }

    public void Project(GlobalFieldVisualBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        int side = _heights.NavCellsPerSide;
        int n = _cells.Length;
        if (!_rangeReady)
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float h = (float)_heights.HeightsCm[i].ToDouble();
                if (h < min) min = h;
                if (h > max) max = h;
            }

            _min = min;
            float span = max - min;
            _span = span < 1f ? 1f : span;
            _rangeReady = true;
        }

        for (int i = 0; i < n; i++)
        {
            float heightT = Math.Clamp(((float)_heights.HeightsCm[i].ToDouble() - _min) / _span, 0f, 1f);
            float shadeT = Math.Clamp((float)_heights.Slope[i].ToDouble() * _strength, 0f, 1f);
            Vector4 color = Vector4.Lerp(Vector4.Lerp(_low, _high, heightT), _shade, shadeT);
            _cells[i] = new GlobalFieldVisualCell(new FieldCell2D(i % side, i / side), color);
        }

        _dirty[0] = new IntRect(0, 0, side, side);
        var descriptor = new GlobalFieldVisualDescriptor(
            new GlobalFieldVisualId(GlobalFieldVisualKind.Walkable, scopeKeyId: 1, layerKeyId: 0, surfaceKeyId: 0),
            _heights.NavCellSizeCm,
            new WorldCmInt2(0, 0),
            new IntRect(0, 0, side, side),
            GlobalFieldVisualValueKind.Vector4);
        buffer.Upsert(in descriptor, _cells.AsSpan(0, n), _dirty);
    }
}
