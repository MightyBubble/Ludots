using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ludots.Core.CrowdSimulation.Config;

namespace Ludots.Core.CrowdSimulation.World;

/// <summary>
/// .navsurface v2 —— CrowdSimulation 的地表资产：逐导航格的地形类型编号 + 链接候选。
/// 高度真相在 .height 资产，本文件不重复存储；桥面不作为地形写入——桥实体在运行时
/// 生成独立的桥面层，初始桥候选只作为元数据参考，生效覆盖一律由实体产生。
///
/// 二进制布局（小端）：
///   0   4    magic "LNSF"
///   4   u16  version = 2（不等于当前版本一律拒绝，无迁移）
///   6   u16  headerBytes = 32
///   8   i32  cellsX              导航格数（X）
///   12  i32  cellsY              导航格数（Y；世界正方形由加载侧校验，本格式不强制）
///   16  i32  navCellSizeCm       导航格边长（厘米）
///   20  u16  terrainCount        地形类型表条数（1..256）
///   22  u16  linkKindCount       链接种类表条数（0..255）
///   24  i32  linkCandidateCount  链接候选条数
///   28  u32  linkBakeDigest      见 <see cref="NavSurfaceContract.LinkBakeDigest"/>
///   32  ..   地形类型表：terrainCount × { u16 utf8ByteCount, bytes }（id 非空且不重复）
///   ..  ..   链接种类表：linkKindCount × { u16 utf8ByteCount, bytes }（id 非空且不重复）
///   ..  ..   格数据：cellsX × cellsY × u8，行优先 index = cy * cellsX + cx；
///            cell (0,0) = 地图局部坐标原点角，值 = 地形类型表下标
///   ..  ..   链接候选：linkCandidateCount × 16 B，按 kindIndex 升序分组
///            { u8 kindIndex, u8 flags(bit0 斜向), u16 steps, u16 fromX, fromY, toX, toY, i32 dropCm }
/// 可寻址流在读完结构后要求位置等于长度。不可寻址流不检查尾部多余字节。截断由字段读取发现。
/// </summary>
public sealed class NavSurfaceAsset
{
    public const uint Magic = 0x46534E4C; // "LNSF"，小端落盘顺序 L-N-S-F
    public const ushort CurrentVersion = 2;
    public const ushort HeaderBytes = 32;
    public const int LinkCandidateRecordBytes = 16;
    public const byte DiagonalFlag = 1;

    public required int NavCellsX { get; init; }
    public required int NavCellsY { get; init; }
    public required int NavCellSizeCm { get; init; }
    /// <summary>地形类型 id 表；格值 = 本表下标。顺序与 maps.&lt;mapId&gt;.terrainTypes 一致。</summary>
    public required string[] TerrainTypeIds { get; init; }
    /// <summary>链接种类 id 表；候选的 KindIndex = 本表下标。</summary>
    public required string[] LinkKindIds { get; init; }
    public required uint LinkBakeDigest { get; init; }
    /// <summary>逐格地形类型编号（行优先，长度 = NavCellsX × NavCellsY）。</summary>
    public required byte[] TerrainCells { get; init; }
    public required NavSurfaceJumpCandidate[] JumpCandidates { get; init; }

    public int NavCellTotal => NavCellsX * NavCellsY;

    public byte TerrainAt(int cx, int cy) => TerrainCells[cy * NavCellsX + cx];

    public static NavSurfaceAsset Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        uint magic = reader.ReadUInt32();
        if (magic != Magic)
        {
            throw new InvalidDataException($".navsurface: magic 0x{magic:X8} 不是 \"LNSF\"。");
        }

        ushort version = reader.ReadUInt16();
        if (version != CurrentVersion)
        {
            throw new InvalidDataException(
                $".navsurface: version = {version}，不是当前版本 v{CurrentVersion}（无迁移），用 nav bake-crowd-navsurface 重烘。");
        }

        ushort headerBytes = reader.ReadUInt16();
        if (headerBytes != HeaderBytes)
        {
            throw new InvalidDataException($".navsurface: headerBytes = {headerBytes}，v{CurrentVersion} 头长必须是 {HeaderBytes}。");
        }

        int cellsX = reader.ReadInt32();
        int cellsY = reader.ReadInt32();
        int navCellSizeCm = reader.ReadInt32();
        int terrainCount = reader.ReadUInt16();
        int linkKindCount = reader.ReadUInt16();
        int candidateCount = reader.ReadInt32();
        uint linkBakeDigest = reader.ReadUInt32();

        ValidateHeader(cellsX, cellsY, navCellSizeCm, terrainCount, linkKindCount);
        if (candidateCount < 0)
        {
            throw new InvalidDataException($".navsurface: linkCandidateCount = {candidateCount}，需为 ≥ 0。");
        }

        string[] terrainTypeIds = ReadIdTable(reader, terrainCount, "terrainTypes");
        string[] linkKindIds = ReadIdTable(reader, linkKindCount, "linkKinds");

        long cellCount = (long)cellsX * cellsY;
        if (cellCount > int.MaxValue)
        {
            throw new InvalidDataException($".navsurface: 格数 {cellCount} 超过格式上限。");
        }

        byte[] terrainCells = reader.ReadBytes((int)cellCount);
        if (terrainCells.Length != (int)cellCount)
        {
            throw new InvalidDataException($".navsurface: 格数据截断（期望 {cellCount} B，实得 {terrainCells.Length} B）。");
        }

        ValidateCells(terrainCells, terrainCount);

        var candidates = new NavSurfaceJumpCandidate[candidateCount];
        for (int i = 0; i < candidateCount; i++)
        {
            byte kindIndex = reader.ReadByte();
            byte flags = reader.ReadByte();
            if ((flags & ~DiagonalFlag) != 0)
            {
                throw new InvalidDataException($".navsurface: 链接候选 {i} 的 flags = 0x{flags:X2}，只允许 bit0（斜向）。");
            }

            candidates[i] = new NavSurfaceJumpCandidate
            {
                KindIndex = kindIndex,
                Diagonal = (flags & DiagonalFlag) != 0,
                Steps = reader.ReadUInt16(),
                FromX = reader.ReadUInt16(),
                FromY = reader.ReadUInt16(),
                ToX = reader.ReadUInt16(),
                ToY = reader.ReadUInt16(),
                DropCm = reader.ReadInt32(),
            };
        }

        ValidateCandidates(candidates, cellsX, cellsY, linkKindCount);

        // 精确长度校验：读取后流必须恰好到达末尾。
        if (stream.CanSeek && stream.Position != stream.Length)
        {
            throw new InvalidDataException($".navsurface: 长度校验失败（读到 {stream.Position}，文件长 {stream.Length}）。");
        }

        return new NavSurfaceAsset
        {
            NavCellsX = cellsX,
            NavCellsY = cellsY,
            NavCellSizeCm = navCellSizeCm,
            TerrainTypeIds = terrainTypeIds,
            LinkKindIds = linkKindIds,
            LinkBakeDigest = linkBakeDigest,
            TerrainCells = terrainCells,
            JumpCandidates = candidates,
        };
    }

    public static void Write(NavSurfaceAsset asset, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(stream);

        ValidateHeader(asset.NavCellsX, asset.NavCellsY, asset.NavCellSizeCm, asset.TerrainTypeIds.Length, asset.LinkKindIds.Length);
        if (asset.TerrainCells.Length != asset.NavCellsX * asset.NavCellsY)
        {
            throw new InvalidDataException($".navsurface: 格数据长度 {asset.TerrainCells.Length} 与尺寸 {asset.NavCellsX} × {asset.NavCellsY} 不符。");
        }

        ValidateCells(asset.TerrainCells, asset.TerrainTypeIds.Length);
        ValidateCandidates(asset.JumpCandidates, asset.NavCellsX, asset.NavCellsY, asset.LinkKindIds.Length);

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(CurrentVersion);
        writer.Write(HeaderBytes);
        writer.Write(asset.NavCellsX);
        writer.Write(asset.NavCellsY);
        writer.Write(asset.NavCellSizeCm);
        writer.Write((ushort)asset.TerrainTypeIds.Length);
        writer.Write((ushort)asset.LinkKindIds.Length);
        writer.Write(asset.JumpCandidates.Length);
        writer.Write(asset.LinkBakeDigest);

        WriteIdTable(writer, asset.TerrainTypeIds, "terrainTypes");
        WriteIdTable(writer, asset.LinkKindIds, "linkKinds");
        writer.Write(asset.TerrainCells);

        foreach (var c in asset.JumpCandidates)
        {
            writer.Write(c.KindIndex);
            writer.Write(c.Diagonal ? DiagonalFlag : (byte)0);
            writer.Write(c.Steps);
            writer.Write(c.FromX);
            writer.Write(c.FromY);
            writer.Write(c.ToX);
            writer.Write(c.ToY);
            writer.Write(c.DropCm);
        }

        writer.Flush();
    }

    private static void ValidateHeader(int cellsX, int cellsY, int navCellSizeCm, int terrainCount, int linkKindCount)
    {
        if (cellsX <= 0 || cellsY <= 0)
        {
            throw new InvalidDataException($".navsurface: 尺寸 {cellsX} × {cellsY} 无效（需为 > 0）。");
        }

        if (navCellSizeCm <= 0)
        {
            throw new InvalidDataException($".navsurface: navCellSizeCm = {navCellSizeCm}，需为 > 0。");
        }

        if (terrainCount < 1 || terrainCount > CrowdSimulationLimits.MaxTerrainTypeIds)
        {
            throw new InvalidDataException($".navsurface: terrainCount = {terrainCount}，需为 1~{CrowdSimulationLimits.MaxTerrainTypeIds}。");
        }

        if (linkKindCount > CrowdTraversalConfig.MaxLinkKinds)
        {
            throw new InvalidDataException($".navsurface: linkKindCount = {linkKindCount}，最多 {CrowdTraversalConfig.MaxLinkKinds}。");
        }
    }

    private static string[] ReadIdTable(BinaryReader reader, int count, string table)
    {
        var ids = new string[count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            ushort byteCount = reader.ReadUInt16();
            if (byteCount == 0)
            {
                throw new InvalidDataException($".navsurface: {table}[{i}] 为空 id。");
            }

            byte[] bytes = reader.ReadBytes(byteCount);
            if (bytes.Length != byteCount)
            {
                throw new InvalidDataException($".navsurface: {table}[{i}] 截断。");
            }

            string id = Encoding.UTF8.GetString(bytes);
            if (!seen.Add(id))
            {
                throw new InvalidDataException($".navsurface: {table} 的 id \"{id}\" 重复。");
            }

            ids[i] = id;
        }

        return ids;
    }

    private static void WriteIdTable(BinaryWriter writer, string[] ids, string table)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException($".navsurface: {table} 的 id 不能为空。");
            }

            if (!seen.Add(id))
            {
                throw new InvalidDataException($".navsurface: {table} 的 id \"{id}\" 重复。");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(id);
            if (bytes.Length > ushort.MaxValue)
            {
                throw new InvalidDataException($".navsurface: {table} 的 id \"{id}\" 过长。");
            }

            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }
    }

    private static void ValidateCells(byte[] cells, int terrainCount)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] >= terrainCount)
            {
                throw new InvalidDataException($".navsurface: 格 {i} 的地形编号 {cells[i]} 超出类型表（{terrainCount} 项）。");
            }
        }
    }

    private static void ValidateCandidates(NavSurfaceJumpCandidate[] candidates, int cellsX, int cellsY, int linkKindCount)
    {
        int previousKind = 0;
        for (int i = 0; i < candidates.Length; i++)
        {
            var c = candidates[i];
            if (c.KindIndex >= linkKindCount)
            {
                throw new InvalidDataException($".navsurface: 链接候选 {i} 的 kindIndex = {c.KindIndex}，超出链接种类表（{linkKindCount} 项）。");
            }

            if (c.KindIndex < previousKind)
            {
                throw new InvalidDataException($".navsurface: 链接候选 {i} 的 kindIndex = {c.KindIndex} 小于前一条，候选必须按种类升序分组。");
            }

            previousKind = c.KindIndex;
            if (c.Steps < 1)
            {
                throw new InvalidDataException($".navsurface: 链接候选 {i} 的 steps = {c.Steps}，需为 ≥ 1。");
            }

            if (c.FromX >= cellsX || c.FromY >= cellsY || c.ToX >= cellsX || c.ToY >= cellsY)
            {
                throw new InvalidDataException($".navsurface: 链接候选 {i} 端点越界（网格 {cellsX} × {cellsY}）。");
            }

            int dx = c.ToX - c.FromX;
            int dy = c.ToY - c.FromY;
            bool axisOk = (dx == 0 || Math.Abs(dx) == c.Steps) && (dy == 0 || Math.Abs(dy) == c.Steps);
            if (!axisOk || (dx != 0 && dy != 0) != c.Diagonal || (dx == 0 && dy == 0))
            {
                throw new InvalidDataException(
                    $".navsurface: 链接候选 {i} 的位移 ({dx}, {dy}) 与 steps = {c.Steps}、斜向 = {c.Diagonal} 不符。");
            }

            if (c.DropCm < 0)
            {
                throw new InvalidDataException($".navsurface: 链接候选 {i} 的 dropCm = {c.DropCm}，需为 ≥ 0（from 是高端）。");
            }
        }
    }
}

/// <summary>
/// 链接候选（作者态烘焙产物，运行时装配时按各移动类型的链接能力过滤为单向 / 双向链接）。
/// From 是高端格，To 是低端格；DropCm = height(from) − height(to) ≥ 0（厘米）。
/// 位移沿 8 个方向之一走 Steps 格；LengthCells 是两端点格距。
/// </summary>
public struct NavSurfaceJumpCandidate
{
    public byte KindIndex { get; init; }
    public bool Diagonal { get; init; }
    public ushort Steps { get; init; }
    public ushort FromX { get; init; }
    public ushort FromY { get; init; }
    public ushort ToX { get; init; }
    public ushort ToY { get; init; }
    public int DropCm { get; init; }

    public readonly float LengthCells => Diagonal ? (float)(Steps * Math.Sqrt(2.0)) : Steps;
}
