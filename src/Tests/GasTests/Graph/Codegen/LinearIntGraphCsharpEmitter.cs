using System;
using Ludots.Core.GraphRuntime;
using Ludots.Graph.Codegen;

namespace Ludots.Tests.Gas.Graph.Codegen
{
    /// <summary>Thin facade: spike tests bind the product emitter/host.</summary>
    public static class LinearIntGraphCsharpEmitter
    {
        public const string GeneratedNamespace = GraphCsharpEmitter.GeneratedNamespace;
        public const string GeneratedTypeName = GraphCsharpEmitter.GeneratedTypeName;
        public const string GeneratedMethodName = GraphCsharpEmitter.GeneratedMethodName;
        public const string GeneratedTightMethodName = GraphCsharpEmitter.GeneratedTightMethodName;

        public static string Emit(ReadOnlySpan<GraphInstruction> program, string assemblyMarker) =>
            GraphCsharpEmitter.EmitSource(program, assemblyMarker);
    }

    public sealed class GraphRoslynCompileFailureException : Exception
    {
        public GraphRoslynCompileFailureException(string message, System.Collections.Generic.IReadOnlyList<string> diagnostics)
            : base(message)
        {
            Diagnostics = diagnostics;
        }

        public System.Collections.Generic.IReadOnlyList<string> Diagnostics { get; }

        public static GraphRoslynCompileFailureException From(GraphCodegenCompileFailureException ex) =>
            new(ex.Message, ex.Diagnostics);
    }

    public sealed class GraphRoslynAlcCompilerHost : IDisposable
    {
        private readonly GraphCodegenCompilerHost _inner = new();

        public GraphGeneratedExecute? ActiveExecute => _inner.ActiveExecute;
        public Func<int>? ActiveTightExecute => _inner.ActiveTightExecute;
        public string? ActiveAssemblyMarker => _inner.ActiveAssemblyMarker;
        public string? ActiveSource => _inner.ActiveSource;

        public GraphGeneratedExecute CompileAndActivate(
            ReadOnlySpan<GraphInstruction> program,
            string assemblyMarker) =>
            _inner.CompileAndActivate(program, assemblyMarker);

        public GraphGeneratedExecute CompileSourceAndActivate(string source, string assemblyMarker)
        {
            try
            {
                return _inner.CompileSourceAndActivate(
                    source,
                    assemblyMarker,
                    expectTightEntry: source.Contains(GraphCsharpEmitter.GeneratedTightMethodName, StringComparison.Ordinal));
            }
            catch (GraphCodegenCompileFailureException ex)
            {
                throw GraphRoslynCompileFailureException.From(ex);
            }
        }

        public WeakReference DropActiveForUnloadProbe() => _inner.DropActiveForUnloadProbe();

        public void Dispose() => _inner.Dispose();
    }
}
