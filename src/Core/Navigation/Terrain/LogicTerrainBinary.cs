using System;
using System.IO;
using System.Text;

namespace Ludots.Core.Navigation.Terrain
{
    public static class LogicTerrainBinary
    {
        private const string Magic = "LTRN";
        private const int Version = 1;

        public static LogicTerrainField Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            string magic = Encoding.ASCII.GetString(ReadExact(reader, 4));
            if (!string.Equals(magic, Magic, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid LogicTerrain binary magic.");
            }

            int version = reader.ReadInt32();
            if (version != Version)
            {
                throw new InvalidDataException($"Unsupported LogicTerrain binary version: {version}.");
            }

            var topology = (LogicTerrainTopology)reader.ReadByte();
            int widthCells = reader.ReadInt32();
            int heightCells = reader.ReadInt32();
            int cellSizeCm = reader.ReadInt32();
            int chunkSizeCells = reader.ReadInt32();
            int cellCount = reader.ReadInt32();

            if (topology != LogicTerrainTopology.Grid)
            {
                throw new InvalidDataException($"LogicTerrainBinary only supports Grid topology, got '{topology}'.");
            }

            if (widthCells <= 0 || heightCells <= 0 || cellSizeCm <= 0 || chunkSizeCells <= 0)
            {
                throw new InvalidDataException("LogicTerrain binary has invalid dimensions.");
            }

            int expectedCells = checked(widthCells * heightCells);
            if (cellCount != expectedCells)
            {
                throw new InvalidDataException(
                    $"LogicTerrain binary cell count mismatch. Expected={expectedCells}, actual={cellCount}.");
            }

            var field = new MutableGridLogicTerrainField(widthCells, heightCells, cellSizeCm, chunkSizeCells);
            for (int row = 0; row < heightCells; row++)
            {
                for (int col = 0; col < widthCells; col++)
                {
                    byte height = reader.ReadByte();
                    byte water = reader.ReadByte();
                    var flags = (LogicTerrainSurfaceFlags)reader.ReadByte();
                    byte areaId = reader.ReadByte();
                    float cost = reader.ReadSingle();
                    field.SetCell(col, row, new LogicTerrainCell(height, water, flags, areaId, cost));
                }
            }

            return field;
        }

        public static void Write(Stream stream, LogicTerrainField terrain)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));
            if (terrain.Topology != LogicTerrainTopology.Grid)
            {
                throw new InvalidDataException(
                    $"LogicTerrainBinary only writes Grid topology, got '{terrain.Topology}'.");
            }

            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes(Magic));
            writer.Write(Version);
            writer.Write((byte)terrain.Topology);
            writer.Write(terrain.WidthCells);
            writer.Write(terrain.HeightCells);
            writer.Write(terrain.HorizontalStepCm);
            writer.Write(terrain.ChunkSizeCells);
            writer.Write(checked(terrain.WidthCells * terrain.HeightCells));

            for (int row = 0; row < terrain.HeightCells; row++)
            {
                for (int col = 0; col < terrain.WidthCells; col++)
                {
                    LogicTerrainCell cell = terrain.GetCell(col, row);
                    writer.Write(cell.HeightLevel);
                    writer.Write(cell.WaterHeightLevel);
                    writer.Write((byte)cell.SurfaceFlags);
                    writer.Write(cell.AreaId);
                    writer.Write(cell.Cost);
                }
            }
        }

        private static byte[] ReadExact(BinaryReader reader, int byteCount)
        {
            byte[] buffer = reader.ReadBytes(byteCount);
            if (buffer.Length != byteCount)
            {
                throw new EndOfStreamException();
            }

            return buffer;
        }
    }
}
