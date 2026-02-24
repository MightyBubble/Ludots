using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ludots.Core.GraphRuntime
{
    public readonly record struct GraphProgramPackage(string GraphName, string[] Symbols, GraphInstruction[] Program);

    public static class GraphProgramBlob
    {
        public const uint Magic = 0x4647524C;
        public const ushort Version = 2;

        public static void Write(Stream stream, IReadOnlyList<GraphProgramPackage> graphs)
        {
            using var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            bw.Write(Magic);
            bw.Write(Version);
            bw.Write((ushort)graphs.Count);

            for (int g = 0; g < graphs.Count; g++)
            {
                var (name, symbols, program) = graphs[g];
                if (name == null) name = string.Empty;
                var nameBytes = Encoding.UTF8.GetBytes(name);
                if (nameBytes.Length > ushort.MaxValue) throw new InvalidOperationException("Graph name too long.");

                bw.Write((ushort)nameBytes.Length);
                bw.Write(nameBytes);

                symbols ??= Array.Empty<string>();
                if (symbols.Length > ushort.MaxValue) throw new InvalidOperationException("Graph symbol table too large.");
                bw.Write((ushort)symbols.Length);
                for (int s = 0; s < symbols.Length; s++)
                {
                    var sym = symbols[s] ?? string.Empty;
                    var symBytes = Encoding.UTF8.GetBytes(sym);
                    if (symBytes.Length > ushort.MaxValue) throw new InvalidOperationException("Graph symbol too long.");
                    bw.Write((ushort)symBytes.Length);
                    bw.Write(symBytes);
                }

                var instructions = program ?? Array.Empty<GraphInstruction>();
                if (instructions.Length > ushort.MaxValue) throw new InvalidOperationException("Graph program too large.");

                bw.Write((ushort)instructions.Length);
                for (int i = 0; i < instructions.Length; i++)
                {
                    var ins = instructions[i];
                    bw.Write(ins.Op);
                    bw.Write(ins.Dst);
                    bw.Write(ins.A);
                    bw.Write(ins.B);
                    bw.Write(ins.C);
                    bw.Write(ins.Flags);
                    bw.Write(ins.Imm);
                    bw.Write(ins.ImmF);
                    bw.Write((byte)0);
                }
            }
        }

        public static void Read(Stream stream, Action<string, string[], GraphInstruction[]> onGraph)
        {
            using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            uint magic = br.ReadUInt32();
            if (magic != Magic) throw new InvalidDataException("Invalid graph blob magic.");

            ushort version = br.ReadUInt16();
            if (version != 1 && version != 2) throw new InvalidDataException($"Unsupported graph blob version {version}.");

            ushort graphCount = br.ReadUInt16();
            for (int g = 0; g < graphCount; g++)
            {
                ushort nameLen = br.ReadUInt16();
                var nameBytes = br.ReadBytes(nameLen);
                string name = Encoding.UTF8.GetString(nameBytes);

                string[] symbols = Array.Empty<string>();
                if (version >= 2)
                {
                    ushort symbolCount = br.ReadUInt16();
                    symbols = symbolCount > 0 ? new string[symbolCount] : Array.Empty<string>();
                    for (int s = 0; s < symbolCount; s++)
                    {
                        ushort symLen = br.ReadUInt16();
                        var symBytes = br.ReadBytes(symLen);
                        symbols[s] = Encoding.UTF8.GetString(symBytes);
                    }
                }

                ushort instrCount = br.ReadUInt16();
                var program = new GraphInstruction[instrCount];
                for (int i = 0; i < instrCount; i++)
                {
                    var ins = new GraphInstruction
                    {
                        Op = br.ReadUInt16(),
                        Dst = br.ReadByte(),
                        A = br.ReadByte(),
                        B = br.ReadByte(),
                        C = br.ReadByte(),
                        Flags = br.ReadByte(),
                        Imm = br.ReadInt32(),
                        ImmF = br.ReadSingle()
                    };
                    br.ReadByte();
                    program[i] = ins;
                }

                onGraph(name, symbols, program);
            }
        }

        public static void Read(Stream stream, Action<string, GraphInstruction[]> onGraph)
        {
            Read(stream, (name, _, program) => onGraph(name, program));
        }
    }
}

