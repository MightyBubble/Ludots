using System;
using System.IO;
using System.Text;

namespace Ludots.Core.CrowdSimulation.World;

/// <summary>
/// .navsurface v1 —— CrowdSimulation 的地表资产：逐导航格的地形类型编号 + 跳跃候选。
/// 高度真相在 .height 资产，本文件不重复存储；桥面不作为地形写入——桥实体在运行时
/// 生成独立的桥面层，初始桥候选只作为元数据参考，生效覆盖一律由实体产生。
///
/// 二进制布局（小端）：
///   0   4    magic "LNSF"
///   4   u16  version = 1（不等于当前版本一律拒绝，无迁移）
///   6   u16  headerBytes = 32（头部总长度，供前向兼容扩展）
///   8   i32  cellsX        导航格数（X）
///   12  i32  cellsY        导航格数（Y；世界正方形由加载侧校验，本格式不强制）
///   16  i32  cellSizeCm    导航格边长（厘米）
///   20  i32  jumpCount     跳跃候选条数
///   24  u16  terrainCount  地形类型表条数（1..256）
///   26  u16  reserved = 0
///   28  i32  reserved = 0
///   32  ..   地形类型表：terrainCount × { u16 utf8ByteCount, bytes }（id 非空且不重复）
///   ..  ..   格数据：cellsX × cellsY × u8，行优先 index = cy * cellsX + cx；
///            cell (0,0) = 地图局部坐标原点角，值 = 地形类型表下标
///   ..  ..   跳跃候选：jumpCount × JumpCandidateRecord（16 B）
/// 长度校验：文件总字节数必须与头字段推导的精确长度一致；多一字节少一字节都拒绝。
/// </summary>
public sealed class NavSurfaceAsset
{
    public const uint Magic = 0x46534E4C; // "LNSF"，小端落盘顺序 L-N-S-F
    public const ushort CurrentVersion = 1;
    public const ushort HeaderBytes = 32;
    public const int JumpCandidateRecordBytes = 16;

    public required int CellsX { get; init; }
    public required int CellsY { get; init; }
    public required int CellSizeCm { get; init; }
    /// <summary>地形类型 id 表；格值 = 本表下标。顺序与 CrowdSimulationConfig.world.terrainTypes 一致。</summary>
    public required string[] TerrainTypeIds { get; init; }
    /// <summary>逐格地形类型编号（行优先，长度 = CellsX × CellsY）。</summary>
    public required byte[] TerrainCells { get; init; }
    public required NavSurfaceJumpCandidate[] JumpCandidates { get; init; }

    public int CellCount => CellsX * CellsY;

    public byte TerrainAt(int cx, int cy) => TerrainCells[cy * CellsX + cx];

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
            throw new InvalidDataException($".navsurface: version = {version}，不是当前版本 v{CurrentVersion}（无迁移）。");
        }

        ushort headerBytes = reader.ReadUInt16();
        if (headerBytes < HeaderBytes)
        {
            throw new InvalidDataException($".navsurface: headerBytes = {headerBytes}，小于 v1 头长 {HeaderBytes}。");
        }

        int cellsX = reader.ReadInt32();
        int cellsY = reader.ReadInt32();
        int cellSizeCm = reader.ReadInt32();
        int jumpCount = reader.ReadInt32();
        int terrainCount = reader.ReadUInt16();
        ushort reserved16 = reader.ReadUInt16();
        int reserved32 = reader.ReadInt32();

        if (cellsX <= 0 || cellsY <= 0)
        {
            throw new InvalidDataException($".navsurface: 尺寸 {cellsX} × {cellsY} 无效（需为 > 0）。");
        }

        if (cellSizeCm <= 0)
        {
            throw new InvalidDataException($".navsurface: cellSizeCm = {cellSizeCm}，需为 > 0。");
        }

        if (terrainCount < 1 || terrainCount > 256)
        {
            throw new InvalidDataException($".navsurface: terrainCount = {terrainCount}，需为 1~256。");
        }

        if (jumpCount < 0)
        {
            throw new InvalidDataException($".navsurface: jumpCount = {jumpCount}，需为 ≥ 0。");
        }

        if (reserved16 != 0 || reserved32 != 0)
        {
            throw new InvalidDataException(".navsurface: 保留字段必须为 0。");
        }

        var terrainTypeIds = new string[terrainCount];
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < terrainCount; i++)
        {
            ushort byteCount = reader.ReadUInt16();
            if (byteCount == 0)
            {
                throw new InvalidDataException($".navsurface: terrainTypes[{i}] 为空 id。");
            }

            string id = Encoding.UTF8.GetString(reader.ReadBytes(byteCount));
            if (!seen.Add(id))
            {
                throw new InvalidDataException($".navsurface: 地形类型 id \"{id}\" 重复。");
            }

            terrainTypeIds[i] = id;
        }

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

        for (int i = 0; i < terrainCells.Length; i++)
        {
            if (terrainCells[i] >= terrainCount)
            {
                throw new InvalidDataException($".navsurface: 格 {i} 的地形编号 {terrainCells[i]} 超出类型表（{terrainCount} 项）。");
            }
        }

        var jumps = new NavSurfaceJumpCandidate[jumpCount];
        for (int i = 0; i < jumpCount; i++)
        {
            var candidate = new NavSurfaceJumpCandidate
            {
                FromX = reader.ReadUInt16(),
                FromY = reader.ReadUInt16(),
                ToX = reader.ReadUInt16(),
                ToY = reader.ReadUInt16(),
                DropCm = reader.ReadInt32(),
                LengthCells = reader.ReadSingle(),
            };
            if (candidate.FromX >= cellsX || candidate.FromY >= cellsY || candidate.ToX >= cellsX || candidate.ToY >= cellsY)
            {
                throw new InvalidDataException($".navsurface: 跳跃候选 {i} 端点越界（网格 {cellsX} × {cellsY}）。");
            }

            jumps[i] = candidate;
        }

        // 精确长度校验：读取后流必须恰好到达末尾。
        if (stream.CanSeek && stream.Position != stream.Length)
        {
            throw new InvalidDataException($".navsurface: 长度校验失败（读到 {stream.Position}，文件长 {stream.Length}）。");
        }

        return new NavSurfaceAsset
        {
            CellsX = cellsX,
            CellsY = cellsY,
            CellSizeCm = cellSizeCm,
            TerrainTypeIds = terrainTypeIds,
            TerrainCells = terrainCells,
            JumpCandidates = jumps,
        };
    }

    public static void Write(NavSurfaceAsset asset, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(stream);

        if (asset.CellsX <= 0 || asset.CellsY <= 0)
        {
            throw new InvalidDataException($".navsurface: 尺寸 {asset.CellsX} × {asset.CellsY} 无效。");
        }

        if (asset.CellSizeCm <= 0)
        {
            throw new InvalidDataException($".navsurface: cellSizeCm = {asset.CellSizeCm}，需为 > 0。");
        }

        if (asset.TerrainTypeIds.Length < 1 || asset.TerrainTypeIds.Length > 256)
        {
            throw new InvalidDataException($".navsurface: terrainCount = {asset.TerrainTypeIds.Length}，需为 1~256。");
        }

        if (asset.TerrainCells.Length != asset.CellsX * asset.CellsY)
        {
            throw new InvalidDataException($".navsurface: 格数据长度 {asset.TerrainCells.Length} 与尺寸 {asset.CellsX} × {asset.CellsY} 不符。");
        }

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(CurrentVersion);
        writer.Write(HeaderBytes);
        writer.Write(asset.CellsX);
        writer.Write(asset.CellsY);
        writer.Write(asset.CellSizeCm);
        writer.Write(asset.JumpCandidates.Length);
        writer.Write((ushort)asset.TerrainTypeIds.Length);
        writer.Write((ushort)0);
        writer.Write(0);

        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var id in asset.TerrainTypeIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException(".navsurface: 地形类型 id 不能为空。");
            }

            if (!seen.Add(id))
            {
                throw new InvalidDataException($".navsurface: 地形类型 id \"{id}\" 重复。");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(id);
            if (bytes.Length > ushort.MaxValue)
            {
                throw new InvalidDataException($".navsurface: 地形类型 id \"{id}\" 过长。");
            }

            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        foreach (var cell in asset.TerrainCells)
        {
            if (cell >= asset.TerrainTypeIds.Length)
            {
                throw new InvalidDataException($".navsurface: 格地形编号 {cell} 超出类型表（{asset.TerrainTypeIds.Length} 项）。");
            }
        }

        writer.Write(asset.TerrainCells);

        foreach (var j in asset.JumpCandidates)
        {
            writer.Write(j.FromX);
            writer.Write(j.FromY);
            writer.Write(j.ToX);
            writer.Write(j.ToY);
            writer.Write(j.DropCm);
            writer.Write(j.LengthCells);
        }

        writer.Flush();
    }
}

/// <summary>
/// 跳跃候选（作者态烘焙产物，运行时装配时按各几何 profile 的跳跃能力过滤为单向 / 双向链接）。
/// DropCm = height(from) − height(to)（厘米，正 = 向下跳）；LengthCells = 两端点格距。
/// </summary>
public struct NavSurfaceJumpCandidate
{
    public ushort FromX { get; init; }
    public ushort FromY { get; init; }
    public ushort ToX { get; init; }
    public ushort ToY { get; init; }
    public int DropCm { get; init; }
    public float LengthCells { get; init; }
}
