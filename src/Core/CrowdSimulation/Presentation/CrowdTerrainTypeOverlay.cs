using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.System;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 导航格地形类型 RGBA。每格一个像素，格值是地形类型表下标。
/// 缓冲和调色板在会话激活时一次建好。打开期间只按各真导航的 ChangedTiles 重写像素。
/// 着色器把贴图像素 alpha 再乘混合强度，这里混合强度取 1，透明度只由颜色表和阻挡色决定。
/// </summary>
public sealed class CrowdTerrainTypeOverlay
{
    public const float BlendStrength = 1f;

    private readonly byte[] _terrain;
    private readonly byte[] _blocked;
    private readonly byte[] _palette;
    private readonly NavContext[] _navs;
    private readonly int[] _seenVersion;
    private readonly int _cellsPerTile;
    private readonly int _tilesPerSide;
    private readonly byte[] _stamp;
    private readonly IntRect[] _dirty;
    private readonly byte _blockedR;
    private readonly byte _blockedG;
    private readonly byte _blockedB;
    private readonly byte _blockedA;
    private int _dirtyCount;
    private byte _stampGen;

    private CrowdTerrainTypeOverlay(
        byte[] terrain,
        byte[] blocked,
        byte[] palette,
        NavContext[] navs,
        int cellsPerSide,
        int cellSizeCm,
        int cellsPerTile,
        byte blockedR,
        byte blockedG,
        byte blockedB,
        byte blockedA)
    {
        _terrain = terrain;
        _blocked = blocked;
        _palette = palette;
        _navs = navs;
        _seenVersion = new int[navs.Length];
        _cellsPerTile = cellsPerTile;
        _tilesPerSide = cellsPerSide / cellsPerTile;
        _stamp = new byte[_tilesPerSide * _tilesPerSide];
        _dirty = new IntRect[_stamp.Length + 1];
        _blockedR = blockedR;
        _blockedG = blockedG;
        _blockedB = blockedB;
        _blockedA = blockedA;
        Width = cellsPerSide;
        Height = cellsPerSide;
        Rgba = new byte[cellsPerSide * cellsPerSide * 4];
        float span = cellsPerSide * (float)cellSizeCm;
        BoundsCm = new Vector4(0f, 0f, span, span);
        PaintAll();
        SnapshotVersions();
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Rgba { get; }
    public Vector4 BoundsCm { get; }
    public bool Enabled { get; private set; }
    public int DirtyCount => _dirtyCount;
    public ReadOnlySpan<IntRect> DirtyRects => _dirty.AsSpan(0, _dirtyCount);

    public static CrowdTerrainTypeOverlay Create(
        CrowdSimSession session,
        CrowdTerrainTypeColorTable colors,
        int blockedRed,
        int blockedGreen,
        int blockedBlue,
        int blockedAlpha)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(colors);
        Channel(blockedRed);
        Channel(blockedGreen);
        Channel(blockedBlue);
        Channel(blockedAlpha);
        if (session.Structures == null)
        {
            throw new InvalidOperationException("地形类型叠层需要结构仓。");
        }

        var ids = session.Config.TerrainTypeIds;
        colors.RequireCovers(ids, session.Config.MapId);
        int cellsPerSide = session.Config.NavCellsPerSide;
        int cellsPerTile = session.Config.Hpa.NavCellsPerTile;
        if (cellsPerSide < 1 || cellsPerTile < 1 || cellsPerSide % cellsPerTile != 0)
        {
            throw new InvalidOperationException(
                $"地形类型叠层的导航边长 {cellsPerSide} 无法被每 tile 格数 {cellsPerTile} 整除。");
        }

        var palette = new byte[ids.Count * 4];
        for (int i = 0; i < ids.Count; i++)
        {
            if (!colors.Colors.TryGetValue(ids[i], out CrowdTerrainTypeColor? color))
            {
                throw new InvalidOperationException(
                    $"{CrowdTerrainTypeColors.RelativePath}: 地图 \"{session.Config.MapId}\" 的 terrainId \"{ids[i]}\" 没有颜色。");
            }

            palette[i * 4] = (byte)color.Red;
            palette[i * 4 + 1] = (byte)color.Green;
            palette[i * 4 + 2] = (byte)color.Blue;
            palette[i * 4 + 3] = (byte)color.Alpha;
        }

        var surface = session.Structures.SnapshotSurface();
        int n2 = cellsPerSide * cellsPerSide;
        if (surface.TerrainType.Length < n2 || surface.Blocked.Length < n2)
        {
            throw new InvalidOperationException("地形类型叠层的栅格短于导航格数。");
        }

        for (int i = 0; i < n2; i++)
        {
            if (surface.TerrainType[i] >= ids.Count)
            {
                throw new InvalidOperationException(
                    $"地形类型叠层格 {i} 的编号 {surface.TerrainType[i]} 超出类型表 {ids.Count}。");
            }
        }

        var navs = new NavContext[session.Navs.Count];
        int n = 0;
        foreach (KeyValuePair<int, NavContext> pair in session.Navs)
        {
            navs[n++] = pair.Value;
        }

        return new CrowdTerrainTypeOverlay(
            surface.TerrainType,
            surface.Blocked,
            palette,
            navs,
            cellsPerSide,
            session.Config.NavCellSizeCm,
            cellsPerTile,
            (byte)blockedRed,
            (byte)blockedGreen,
            (byte)blockedBlue,
            (byte)blockedAlpha);
    }

    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        if (!enabled) return;
        PaintAll();
        _dirtyCount = 0;
        _stampGen = 0;
        PushDirty(new IntRect(0, 0, Width, Height));
        SnapshotVersions();
    }

    public void Sync()
    {
        if (!Enabled) return;
        _stampGen++;
        if (_stampGen == 0)
        {
            Array.Clear(_stamp);
            _stampGen = 1;
        }

        for (int n = 0; n < _navs.Length; n++)
        {
            NavContext nav = _navs[n];
            int version = nav.Version;
            int seen = _seenVersion[n];
            if (version == seen) continue;
            List<int>? tiles = nav.ChangedTiles;
            // ChangedTiles 只留最近一次重烘。两次同步之间同一导航重烘超过一次时，更早的 tile 已经不在这份列表里。
            if (version != seen + 1 || tiles == null)
            {
                PaintAll();
                _dirtyCount = 0;
                _stampGen = 0;
                PushDirty(new IntRect(0, 0, Width, Height));
                SnapshotVersions();
                return;
            }

            _seenVersion[n] = version;
            for (int i = 0; i < tiles.Count; i++)
            {
                PaintTile(tiles[i]);
            }
        }
    }

    public void ConsumeDirty() => _dirtyCount = 0;

    private void SnapshotVersions()
    {
        for (int n = 0; n < _navs.Length; n++)
        {
            _seenVersion[n] = _navs[n].Version;
        }
    }

    private void PaintAll()
    {
        int n2 = Width * Height;
        for (int i = 0; i < n2; i++)
        {
            WritePixel(i, _terrain[i], _blocked[i]);
        }
    }

    private void PaintTile(int tile)
    {
        if ((uint)tile >= (uint)_stamp.Length)
        {
            throw new InvalidOperationException($"地形类型叠层的 tile {tile} 超出 {_stamp.Length}。");
        }

        if (_stamp[tile] == _stampGen) return;
        _stamp[tile] = _stampGen;
        int tx = tile % _tilesPerSide;
        int ty = tile / _tilesPerSide;
        int x0 = tx * _cellsPerTile;
        int y0 = ty * _cellsPerTile;
        int x1 = x0 + _cellsPerTile;
        int y1 = y0 + _cellsPerTile;
        for (int y = y0; y < y1; y++)
        {
            int row = y * Width;
            for (int x = x0; x < x1; x++)
            {
                int cell = row + x;
                WritePixel(cell, _terrain[cell], _blocked[cell]);
            }
        }

        PushDirty(new IntRect(x0, y0, _cellsPerTile, _cellsPerTile));
    }

    private void WritePixel(int cell, byte terrain, byte blocked)
    {
        int pixel = cell * 4;
        if (blocked != 0)
        {
            Rgba[pixel] = _blockedR;
            Rgba[pixel + 1] = _blockedG;
            Rgba[pixel + 2] = _blockedB;
            Rgba[pixel + 3] = _blockedA;
            return;
        }

        int src = terrain * 4;
        Rgba[pixel] = _palette[src];
        Rgba[pixel + 1] = _palette[src + 1];
        Rgba[pixel + 2] = _palette[src + 2];
        Rgba[pixel + 3] = _palette[src + 3];
    }

    private void PushDirty(IntRect rect)
    {
        if (_dirtyCount == 1 && _dirty[0].Width == Width && _dirty[0].Height == Height) return;
        if (_dirtyCount >= _dirty.Length)
        {
            _dirty[0] = new IntRect(0, 0, Width, Height);
            _dirtyCount = 1;
            return;
        }

        _dirty[_dirtyCount++] = rect;
    }

    private static void Channel(int value)
    {
        if (value < 0 || value > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "颜色通道需为 0..255。");
        }
    }
}

/// <summary>
/// 地形类型开关只读能力 Mod 的动作。调试视图帧只读叠层是否打开，避免同一按键再切一次。
/// </summary>
internal sealed class CrowdTerrainOverlayInputSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly string _action;

    public CrowdTerrainOverlayInputSystem(GameEngine engine, string action)
    {
        _engine = engine;
        _action = action;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_engine.TryGetService(CoreServiceKeys.CrowdSimulationRuntime, out CrowdSimulationRuntime? runtime)
            || runtime?.TerrainTypeOverlay is not { } overlay)
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is not FrozenInputActionReader actions) return;
        if (actions.PressedThisTick(_action)) overlay.SetEnabled(!overlay.Enabled);
    }
}
