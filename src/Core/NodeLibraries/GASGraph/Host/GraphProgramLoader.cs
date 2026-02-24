using System;
using System.IO;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    public sealed class GraphProgramLoader
    {
        private readonly VirtualFileSystem _vfs;
        private readonly ModLoader _modLoader;
        private readonly GraphProgramRegistry _registry;
        private readonly IGraphSymbolResolver _symbolResolver;

        public GraphProgramLoader(VirtualFileSystem vfs, ModLoader modLoader, GraphProgramRegistry registry, IGraphSymbolResolver symbolResolver)
        {
            _vfs = vfs;
            _modLoader = modLoader;
            _registry = registry;
            _symbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
        }

        public void Load(string relativePath = "Compiled/GAS/graphs.bin")
        {
            _registry.Clear();
            GraphIdRegistry.Clear();

            LoadFromAllSources(relativePath, stream =>
            {
                GraphProgramBlob.Read(stream, (name, symbols, program) =>
                {
                    PatchSymbols(symbols, program);
                    int id = GraphIdRegistry.Register(name);
                    _registry.Register(id, program);
                });
            });

            GraphIdRegistry.Freeze();
        }

        private void LoadFromAllSources(string relativePath, Action<Stream> onStreamOpened)
        {
            if (relativePath.StartsWith("/") || relativePath.StartsWith("\\"))
            {
                relativePath = relativePath.Substring(1);
            }

            // Core graphs are required — FileNotFoundException will propagate
            LoadRequired($"Core:assets/{relativePath}", onStreamOpened);

            if (_modLoader?.LoadedModIds != null)
            {
                foreach (var modId in _modLoader.LoadedModIds)
                {
                    TryLoadOptional($"{modId}:assets/{relativePath}", onStreamOpened);
                }
            }
        }

        private void LoadRequired(string uri, Action<Stream> onStreamOpened)
        {
            using var stream = _vfs.GetStream(uri);
            onStreamOpened(stream);
        }

        private void TryLoadOptional(string uri, Action<Stream> onStreamOpened)
        {
            try
            {
                using var stream = _vfs.GetStream(uri);
                onStreamOpened(stream);
            }
            catch (FileNotFoundException)
            {
                // Mod graph files are optional — skip if not found
            }
        }

        private void PatchSymbols(string[] symbols, GraphInstruction[] program)
        {
            if (symbols == null || symbols.Length == 0) return;
            if (program == null || program.Length == 0) return;

            for (int i = 0; i < program.Length; i++)
            {
                ref var ins = ref program[i];
                var op = (GraphNodeOp)ins.Op;
                switch (op)
                {
                    case GraphNodeOp.QueryFilterTagAll:
                    case GraphNodeOp.SendEvent:
                        ins.Imm = ResolveTag(symbols, ins.Imm);
                        break;
                    case GraphNodeOp.LoadAttribute:
                    case GraphNodeOp.ModifyAttributeAdd:
                        ins.Imm = ResolveAttribute(symbols, ins.Imm);
                        break;
                    case GraphNodeOp.ApplyEffectTemplate:
                        ins.Imm = ResolveEffectTemplate(symbols, ins.Imm);
                        break;
                }
            }
        }

        private int ResolveTag(string[] symbols, int symbolIndex)
        {
            return _symbolResolver.ResolveTag(ResolveSymbol(symbols, symbolIndex));
        }

        private int ResolveAttribute(string[] symbols, int symbolIndex)
        {
            return _symbolResolver.ResolveAttribute(ResolveSymbol(symbols, symbolIndex));
        }

        private int ResolveEffectTemplate(string[] symbols, int symbolIndex)
        {
            return _symbolResolver.ResolveEffectTemplate(ResolveSymbol(symbols, symbolIndex));
        }

        private static string ResolveSymbol(string[] symbols, int symbolIndex)
        {
            if ((uint)symbolIndex >= (uint)symbols.Length)
            {
                throw new InvalidOperationException($"Graph symbol index out of range: {symbolIndex} (len={symbols.Length}).");
            }
            return symbols[symbolIndex] ?? string.Empty;
        }
    }
}
