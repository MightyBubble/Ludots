using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Ludots.Tests.Gas.Graph.Codegen
{
    public sealed class GraphRoslynCompileFailureException : Exception
    {
        public GraphRoslynCompileFailureException(string message, IReadOnlyList<string> diagnostics)
            : base(message)
        {
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<string> Diagnostics { get; }
    }

    /// <summary>
    /// R0 host: emit → Roslyn compile → Collectible ALC load → bind <see cref="GraphGeneratedExecute"/>.
    /// Hot reload replaces the entry only after successful compile; failures keep the previous entry (fail-closed).
    /// Does not silently fall back to the interpret VM.
    /// </summary>
    public sealed class GraphRoslynAlcCompilerHost : IDisposable
    {
        private readonly object _gate = new();
        private GraphGeneratedAssemblyLoadContext? _activeContext;
        private Assembly? _activeAssembly;
        private GraphGeneratedExecute? _activeExecute;
        private Func<int>? _activeTightExecute;
        private string? _activeMarker;
        private string? _activeSource;
        private bool _disposed;

        public GraphGeneratedExecute? ActiveExecute
        {
            get
            {
                lock (_gate)
                {
                    return _activeExecute;
                }
            }
        }

        public Func<int>? ActiveTightExecute
        {
            get
            {
                lock (_gate)
                {
                    return _activeTightExecute;
                }
            }
        }

        public string? ActiveAssemblyMarker
        {
            get
            {
                lock (_gate)
                {
                    return _activeMarker;
                }
            }
        }

        public string? ActiveSource
        {
            get
            {
                lock (_gate)
                {
                    return _activeSource;
                }
            }
        }

        public GraphGeneratedExecute CompileAndActivate(
            ReadOnlySpan<GraphInstruction> program,
            string assemblyMarker)
        {
            string source = LinearIntGraphCsharpEmitter.Emit(program, assemblyMarker);
            return CompileSourceAndActivate(source, assemblyMarker);
        }

        public GraphGeneratedExecute CompileSourceAndActivate(string source, string assemblyMarker)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GraphRoslynAlcCompilerHost));
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException("source is required.", nameof(source));
            }

            if (string.IsNullOrWhiteSpace(assemblyMarker))
            {
                throw new ArgumentException("assemblyMarker is required.", nameof(assemblyMarker));
            }

            byte[] peImage = CompileToPeImage(source, assemblyMarker, out IReadOnlyList<string> diagnostics);
            if (peImage.Length == 0)
            {
                throw new GraphRoslynCompileFailureException(
                    $"Roslyn compilation failed for marker '{assemblyMarker}'. Previous entry retained (no interpreter fallback).",
                    diagnostics);
            }

            var nextContext = new GraphGeneratedAssemblyLoadContext(
                "Ludots.Graph.Generated." + assemblyMarker);
            Assembly nextAssembly;
            GraphGeneratedExecute nextExecute;
            Func<int> nextTightExecute;
            try
            {
                using var peStream = new MemoryStream(peImage, writable: false);
                nextAssembly = nextContext.LoadFromStream(peStream);
                nextExecute = BindExecute(nextAssembly);
                nextTightExecute = BindTightExecute(nextAssembly);
            }
            catch
            {
                nextContext.Unload();
                throw;
            }

            GraphGeneratedAssemblyLoadContext? previousContext;
            lock (_gate)
            {
                previousContext = _activeContext;
                _activeContext = nextContext;
                _activeAssembly = nextAssembly;
                _activeExecute = nextExecute;
                _activeTightExecute = nextTightExecute;
                _activeMarker = assemblyMarker;
                _activeSource = source;
            }

            previousContext?.Unload();
            return nextExecute;
        }

        /// <summary>
        /// Drops the active entry so the previous ALC can become unloadable.
        /// Callers that still hold the returned delegate keep the assembly alive (by design).
        /// </summary>
        public WeakReference DropActiveForUnloadProbe()
        {
            lock (_gate)
            {
                if (_activeContext == null || _activeAssembly == null)
                {
                    throw new InvalidOperationException("No active generated assembly to unload.");
                }

                var weak = new WeakReference(_activeAssembly, trackResurrection: false);
                _activeContext.Unload();
                _activeContext = null;
                _activeAssembly = null;
                _activeExecute = null;
                _activeTightExecute = null;
                _activeMarker = null;
                _activeSource = null;
                return weak;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (_gate)
            {
                _activeExecute = null;
                _activeTightExecute = null;
                _activeAssembly = null;
                _activeMarker = null;
                _activeSource = null;
                _activeContext?.Unload();
                _activeContext = null;
            }
        }

        private static Type RequireGeneratedType(Assembly assembly)
        {
            string typeName = LinearIntGraphCsharpEmitter.GeneratedNamespace + "." +
                              LinearIntGraphCsharpEmitter.GeneratedTypeName;
            Type? type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type == null)
            {
                throw new InvalidOperationException($"Generated type '{typeName}' was not found in assembly '{assembly.FullName}'.");
            }

            return type;
        }

        private static GraphGeneratedExecute BindExecute(Assembly assembly)
        {
            Type type = RequireGeneratedType(assembly);
            MethodInfo? method = type.GetMethod(
                LinearIntGraphCsharpEmitter.GeneratedMethodName,
                BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                throw new InvalidOperationException(
                    $"Generated method '{LinearIntGraphCsharpEmitter.GeneratedMethodName}' was not found on '{type.FullName}'.");
            }

            return method.CreateDelegate<GraphGeneratedExecute>();
        }

        private static Func<int> BindTightExecute(Assembly assembly)
        {
            Type type = RequireGeneratedType(assembly);
            MethodInfo? method = type.GetMethod(
                LinearIntGraphCsharpEmitter.GeneratedTightMethodName,
                BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                throw new InvalidOperationException(
                    $"Generated method '{LinearIntGraphCsharpEmitter.GeneratedTightMethodName}' was not found on '{type.FullName}'.");
            }

            return method.CreateDelegate<Func<int>>();
        }

        private static byte[] CompileToPeImage(
            string source,
            string assemblyName,
            out IReadOnlyList<string> diagnostics)
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName: "Ludots.Graph.Generated." + SanitizeAssemblyName(assemblyName),
                syntaxTrees: new[]
                {
                    CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8))
                },
                references: CreateMetadataReferences(),
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    allowUnsafe: false,
                    nullableContextOptions: NullableContextOptions.Enable));

            using var peStream = new MemoryStream();
            EmitResult emitResult = compilation.Emit(peStream);
            diagnostics = emitResult.Diagnostics
                .Where(d => d.Severity >= DiagnosticSeverity.Warning)
                .Select(FormatDiagnostic)
                .ToArray();

            if (!emitResult.Success)
            {
                diagnostics = emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(FormatDiagnostic)
                    .ToArray();
                return Array.Empty<byte>();
            }

            return peStream.ToArray();
        }

        private static string FormatDiagnostic(Diagnostic diagnostic)
        {
            return diagnostic.ToString();
        }

        private static string SanitizeAssemblyName(string marker)
        {
            var chars = marker.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
            return new string(chars);
        }

        private static ImmutableArray<MetadataReference> CreateMetadataReferences()
        {
            var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<MetadataReference>();

            void AddAssembly(Assembly assembly)
            {
                if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
                {
                    return;
                }

                if (!references.Add(assembly.Location))
                {
                    return;
                }

                list.Add(MetadataReference.CreateFromFile(assembly.Location));
            }

            AddAssembly(typeof(object).Assembly);
            AddAssembly(typeof(Console).Assembly);
            AddAssembly(typeof(GraphExecutionState).Assembly);
            AddAssembly(typeof(GraphInstruction).Assembly);
            AddAssembly(AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("System.Runtime")));
            AddAssembly(AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("netstandard")));

            string? trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (!string.IsNullOrWhiteSpace(trusted))
            {
                foreach (string path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    string fileName = Path.GetFileName(path);
                    if (string.Equals(fileName, "System.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fileName, "netstandard.dll", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fileName, "System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fileName, "System.Console.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        if (references.Add(path))
                        {
                            list.Add(MetadataReference.CreateFromFile(path));
                        }
                    }
                }
            }

            return list.ToImmutableArray();
        }
    }
}
