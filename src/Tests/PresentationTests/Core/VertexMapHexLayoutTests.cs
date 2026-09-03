using System;
using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// VertexMap 视觉网格布局合同：legacy 分支必须逐字节复刻 HexCoordinates 静态常量布局
    /// （未铺满世界格网的地图零变化）；铺满 board 世界格网的地图按 GridCellSizeCm 方形格心
    /// 间距、世界原点居中，格心对齐 SpatialCoordinateConverter.GridToWorld 的世界格心合同
    /// （M2.a 城池标记摆点即该合同）。
    /// </summary>
    [TestFixture]
    public class VertexMapHexLayoutTests
    {
        [Test]
        public void Legacy_MatchesHexCoordinatesConstants()
        {
            VertexMapHexLayout legacy = VertexMapHexLayout.Legacy;

            Assert.That(legacy.ColPitchMeters, Is.EqualTo(HexCoordinates.HexWidth));
            Assert.That(legacy.RowPitchMeters, Is.EqualTo(HexCoordinates.RowSpacing));
            Assert.That(legacy.OriginXMeters, Is.EqualTo(0f));
            Assert.That(legacy.OriginZMeters, Is.EqualTo(0f));
        }

        [Test]
        public void Builder_LegacyLayout_KeepsHistoricalVertexPositions()
        {
            var map = new VertexMap();
            map.Initialize(1, 1);
            var dst = new VertexMapChunkMeshData();
            var builder = new VertexMapChunkMeshBuilder(map, VertexMapHexLayout.Legacy);
            builder.BuildChunk(0, 0, heightScale: 2f, simplifiedCliffs: false, dst);

            Assert.That(TryFindVertex(dst.Terrain, HexCoordinates.HexWidth * 3f, 0f), Is.True,
                "legacy 布局下格 (3,0) 顶点必须仍落在 HexWidth*3，保证旧地图视觉零变化");
        }

        [Test]
        public void TryResolveForBoard_MapTilingWorldGrid_ReturnsSquareContractLayout()
        {
            // 256 格 × 2000cm = 5.12km 见方，居中 ±2560m；与 sango_default board 同形。
            WorldSizeSpec world = new WorldExtentSpec(1, 1, 2000).ToWorldSizeSpec();
            var map = new VertexMap();
            map.Initialize(4, 4);

            bool resolved = VertexMapHexLayout.TryResolveForBoard(world, map, out VertexMapHexLayout layout);

            Assert.That(resolved, Is.True);
            Assert.That(layout.ColPitchMeters, Is.EqualTo(20f).Within(0.0001f));
            Assert.That(layout.RowPitchMeters, Is.EqualTo(20f).Within(0.0001f));
            Assert.That(layout.OriginXMeters, Is.EqualTo(-2550f).Within(0.0001f));
            Assert.That(layout.OriginZMeters, Is.EqualTo(-2550f).Within(0.0001f));
        }

        [Test]
        public void TryResolveForBoard_MapNotTilingWorldGrid_FallsBackToLegacy()
        {
            WorldSizeSpec world = new WorldExtentSpec(1, 1, 2000).ToWorldSizeSpec();
            var map = new VertexMap();
            map.Initialize(8, 8); // 512 格 ≠ 256 格世界

            bool resolved = VertexMapHexLayout.TryResolveForBoard(world, map, out VertexMapHexLayout layout);

            Assert.That(resolved, Is.False);
            Assert.That(layout, Is.EqualTo(VertexMapHexLayout.Legacy));
        }

        [Test]
        public void TryResolveForBoard_DegenerateInputs_FallBackToLegacy()
        {
            WorldSizeSpec world = new WorldExtentSpec(1, 1, 2000).ToWorldSizeSpec();
            var map = new VertexMap();
            map.Initialize(4, 4);

            Assert.That(VertexMapHexLayout.TryResolveForBoard(world, null, out _), Is.False);

            WorldSizeSpec empty = default;
            Assert.That(VertexMapHexLayout.TryResolveForBoard(empty, map, out _), Is.False);
        }

        [Test]
        public void Builder_WorldLayout_CellCentersAlignWithGridToWorldContract()
        {
            WorldSizeSpec world = new WorldExtentSpec(1, 1, 2000).ToWorldSizeSpec();
            var map = new VertexMap();
            map.Initialize(4, 4);
            var dst = new VertexMapChunkMeshData();
            VertexMapHexLayout.TryResolveForBoard(world, map, out VertexMapHexLayout layout);
            var builder = new VertexMapChunkMeshBuilder(map, layout);
            builder.BuildChunk(0, 0, heightScale: 1f, simplifiedCliffs: false, dst);

            // M2.a 城池标记合同：格心 (格值*20+10)m - 2560m。取偶数行格 (20,10)，
            // 标记应恰好落在本格顶点（奇数行因 odd-r 半格错位另计，不在本断言范围）。
            float expectedX = (20 * 20f + 10f) - 2560f;
            float expectedZ = (10 * 20f + 10f) - 2560f;
            Assert.That(TryFindVertex(dst.Terrain, expectedX, expectedZ), Is.True,
                $"格 (20,10) 心 ({expectedX},{expectedZ}) 必须存在顶点，否则六边形网格与城池标记错位");

            // 256 格铺满 5.12km：首末格心分别 -2550m / +2550m（63 行为奇数行，odd-r 东移半格）。
            Assert.That(TryFindVertex(dst.Terrain, -2550f, -2550f), Is.True);
            Assert.That(TryFindVertex(dst.Terrain, (63 * 20f + 10f) - 2560f + 10f, (63 * 20f + 10f) - 2560f), Is.True);

            // 奇数行东移半格：格 (20,63) X 应为 20*20+10+10-2560。
            float oddRowX = (63 & 1) * 10f + (20 * 20f + 10f) - 2560f;
            float oddRowZ = (63 * 20f + 10f) - 2560f;
            Assert.That(TryFindVertex(dst.Terrain, oddRowX, oddRowZ), Is.True);
        }

        [Test]
        public void MeshSource_ExposesLayoutDerivedSpacingsAndOrigin()
        {
            WorldSizeSpec world = new WorldExtentSpec(1, 1, 2000).ToWorldSizeSpec();
            var map = new VertexMap();
            map.Initialize(4, 4);
            VertexMapHexLayout.TryResolveForBoard(world, map, out VertexMapHexLayout layout);

            var source = new Ludots.Client.Raylib.Rendering.VertexMapTerrainChunkMeshSource(map, layout);

            Assert.That(source.ChunkSpacingXMeters, Is.EqualTo(20f * VertexChunk.ChunkSize).Within(0.0001f));
            Assert.That(source.ChunkSpacingYMeters, Is.EqualTo(20f * VertexChunk.ChunkSize).Within(0.0001f));
            Assert.That(source.ChunkOriginXMeters, Is.EqualTo(-2550f).Within(0.0001f));
            Assert.That(source.ChunkOriginYMeters, Is.EqualTo(-2550f).Within(0.0001f));
        }

        private static bool TryFindVertex(ChunkMeshWriteBuffer buffer, float x, float z)
        {
            for (int i = 0; i < buffer.VertexCount; i++)
            {
                float vx = buffer.Vertices[i * 3];
                float vz = buffer.Vertices[(i * 3) + 2];
                if (MathF.Abs(vx - x) < 0.01f && MathF.Abs(vz - z) < 0.01f)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
