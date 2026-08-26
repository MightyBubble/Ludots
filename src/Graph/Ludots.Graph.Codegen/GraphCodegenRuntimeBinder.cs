using System;
using System.Collections.Generic;
using System.Reflection;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Graph.Codegen
{
    /// <summary>
    /// Binds generated execute entries onto an already-registered <see cref="GraphProgramRegistry"/>.
    /// Fail-closed for <see cref="GraphCodegenLoadMode.Codegen"/>; named diagnostics for codegen-prefer.
    /// </summary>
    public sealed class GraphCodegenRuntimeBinder : IGraphCodegenRuntimeBinder
    {
        public void BindAll(GraphProgramRegistry registry, GraphCodegenLoadMode mode)
        {
            ArgumentNullException.ThrowIfNull(registry);
            if (mode == GraphCodegenLoadMode.Interpret)
            {
                return;
            }

            using var host = new GraphCodegenCompilerHost();
            var hardFailures = new List<string>();
            IReadOnlyList<KeyValuePair<int, GraphProgramRegistration>> snapshot = registry.SnapshotRegistrations();
            for (int i = 0; i < snapshot.Count; i++)
            {
                int graphId = snapshot[i].Key;
                GraphProgramRegistration registration = snapshot[i].Value;
                try
                {
                    GraphGeneratedExecute execute = host.CompileAndActivate(
                        registration.Program,
                        "runtime-" + graphId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        registration.Symbols);
                    GraphGeneratedExecuteSlice slice = host.ActiveExecuteSlice
                        ?? throw new InvalidOperationException(
                            $"Generated ExecuteSlice missing for graph id {graphId}.");
                    registry.AttachGenerated(graphId, execute, slice, GraphExecutionBackend.Codegen);
                }
                catch (Exception ex)
                {
                    string message = $"graphId={graphId} codegen bind failed: {ex.Message}";
                    if (mode == GraphCodegenLoadMode.Codegen)
                    {
                        hardFailures.Add(message);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[GraphCodegenRuntimeBinder] " + message);
                    }
                }
            }

            if (hardFailures.Count > 0)
            {
                throw new InvalidOperationException(
                    "GAS/graph_codegen_bake.json mode=codegen failed closed:\n" + string.Join("\n", hardFailures));
            }
        }

        public static IGraphCodegenRuntimeBinder ResolveFromLoadedAssemblies()
        {
            const string typeName = "Ludots.Graph.Codegen.GraphCodegenRuntimeBinder";
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, "Ludots.Graph.Codegen", StringComparison.Ordinal))
                {
                    continue;
                }

                Type? type = assembly.GetType(typeName, throwOnError: false);
                if (type != null)
                {
                    return (IGraphCodegenRuntimeBinder)(Activator.CreateInstance(type)
                        ?? throw new InvalidOperationException("Failed to construct GraphCodegenRuntimeBinder."));
                }
            }

            string? coreDir = Path.GetDirectoryName(typeof(GraphProgramRegistry).Assembly.Location);
            if (!string.IsNullOrWhiteSpace(coreDir))
            {
                string dll = Path.Combine(coreDir, "Ludots.Graph.Codegen.dll");
                if (File.Exists(dll))
                {
                    Assembly assembly = Assembly.LoadFrom(dll);
                    Type type = assembly.GetType(typeName, throwOnError: true)
                        ?? throw new InvalidOperationException("GraphCodegenRuntimeBinder type missing.");
                    return (IGraphCodegenRuntimeBinder)(Activator.CreateInstance(type)
                        ?? throw new InvalidOperationException("Failed to construct GraphCodegenRuntimeBinder."));
                }
            }

            throw new InvalidOperationException(
                "Ludots.Graph.Codegen assembly is required when GAS/graph_codegen_bake.json mode is codegen or codegen-prefer. " +
                "Reference Ludots.Graph.Codegen from the host so the DLL is deployed beside Core.");
        }
    }
}
