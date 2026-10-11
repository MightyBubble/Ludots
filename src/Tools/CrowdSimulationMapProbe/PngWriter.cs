using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace CrowdSimulationMapProbe;

/// <summary>最小 PNG 编码（8-bit RGB，zlib stored blocks，无外部依赖）。</summary>
public static class PngWriter
{
    public static void Write(string path, int width, int height, byte[] rgb)
    {
        if (rgb.Length != width * height * 3) throw new ArgumentException("rgb 长度与尺寸不符", nameof(rgb));

        // 扫描线:每行前置 filter byte 0
        var raw = new byte[(width * 3 + 1) * height];
        for (int y = 0; y < height; y++)
        {
            Buffer.BlockCopy(rgb, y * width * 3, raw, y * (width * 3 + 1) + 1, width * 3);
        }

        using var stream = File.Create(path);
        WriteSignature(stream);
        // IHDR
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // color type RGB
        WriteChunk(stream, "IHDR", ihdr);
        // IDAT: zlib stream with stored blocks
        WriteChunk(stream, "IDAT", ZlibStore(raw));
        WriteChunk(stream, "IEND", Array.Empty<byte>());
    }

    private static void WriteSignature(Stream s) => s.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = new Crc32();
        crc.Update(typeBytes);
        crc.Update(data);
        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc.Value);
        s.Write(crcBytes);
    }

    private static byte[] ZlibStore(byte[] data)
    {
        // zlib 流(deflate):PNG IDAT 的合法编码;用运行时自带的 ZLibStream,不引入外部包
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }

        return ms.ToArray();
    }

    private sealed class Crc32
    {
        private static readonly uint[] Table = BuildTable();
        private uint _crc = 0xFFFFFFFF;

        public uint Value => _crc ^ 0xFFFFFFFF;

        public void Update(byte[] data)
        {
            foreach (byte b in data) _crc = Table[(_crc ^ b) & 0xFF] ^ (_crc >> 8);
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[i] = c;
            }

            return table;
        }
    }
}
