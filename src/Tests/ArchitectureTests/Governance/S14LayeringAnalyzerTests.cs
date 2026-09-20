using System.Collections.Immutable;
using Ludots.Analyzers.Layering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance;

[Category("ci-gate")]
[Category("arch-guard")]
public sealed class S14LayeringAnalyzerTests
{
    [Test]
    public void PresentationAssembly_WritingSimulationOwnedComponent_IsDiagnostic()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze(
            assemblyName: "Fake.Presentation",
            source: """
            using System;
            namespace Fake.Presentation
            {
                [WriteOwner(LayerOwner.Simulation)]
                public struct PresentationStableId { public int Value; }

                public static class CameraWriter
                {
                    public static PresentationStableId Write() => new PresentationStableId { Value = 1 };
                }
            }
            """);

        Assert.That(diagnostics.Any(d => d.Id == WriteOwnerAnalyzer.DiagnosticId), Is.True);
    }

    [Test]
    public void BootstrapType_InPresentationAssembly_IsAllowed()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze(
            assemblyName: "Fake.Presentation",
            source: """
            namespace Fake.Presentation
            {
                [WriteOwner(LayerOwner.Simulation)]
                public struct PresentationStableId { public int Value; }

                public static class PresentationStableIdBootstrapSystem
                {
                    public static PresentationStableId Write() => new PresentationStableId { Value = 1 };
                }
            }
            """);

        Assert.That(diagnostics.Any(d => d.Id == WriteOwnerAnalyzer.DiagnosticId), Is.False);
    }

    [Test]
    public void SimulationAssembly_WritingSimulationOwnedComponent_IsAllowed()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze(
            assemblyName: "Ludots.GAS",
            source: """
            namespace Ludots.Gameplay
            {
                [WriteOwner(LayerOwner.Simulation)]
                public struct PresentationStableId { public int Value; }

                public static class Spawn
                {
                    public static PresentationStableId Write() => new PresentationStableId { Value = 7 };
                }
            }
            """);

        Assert.That(diagnostics.Any(d => d.Id == WriteOwnerAnalyzer.DiagnosticId), Is.False);
    }

    private static ImmutableArray<Diagnostic> Analyze(string assemblyName, string source)
    {
        string prelude = """
            namespace System
            {
                public class Attribute {}
                public enum AttributeTargets { Struct, Class, Field }
                public class AttributeUsageAttribute : Attribute
                {
                    public AttributeUsageAttribute(AttributeTargets targets) {}
                    public bool Inherited { get; set; }
                }
            }
            public enum LayerOwner : byte { Simulation = 1, Presentation = 2 }
            public sealed class WriteOwnerAttribute : System.Attribute
            {
                public WriteOwnerAttribute(LayerOwner owner) { Owner = owner; }
                public LayerOwner Owner { get; }
            }
            """;

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            new[]
            {
                CSharpSyntaxTree.ParseText(prelude),
                CSharpSyntaxTree.ParseText(source),
            },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new WriteOwnerAnalyzer();
        CompilationWithAnalyzers compiled = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return compiled.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }
}
